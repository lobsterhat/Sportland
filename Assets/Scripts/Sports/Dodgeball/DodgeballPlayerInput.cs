using UnityEngine;
using UnityEngine.InputSystem;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Routes PS4 / keyboard input into a single human-controlled Dodgeball
    /// player. Move uses analog magnitude; L2 holds sprint. Face buttons split
    /// by possession:
    ///   offense — Cross jumps, Square throws, Circle passes
    ///   defense — Cross jumps, Circle catches, Triangle takes the nearest teammate
    /// L1 force-returns the ball for testing.
    ///
    /// Player control is single-active: a static Current reference points at
    /// the live input component. When a pass arrives at the intended target,
    /// TransferControl destroys this component and re-attaches a fresh one to
    /// the receiver.
    /// </summary>
    [RequireComponent(typeof(PlayerMovement))]
    [RequireComponent(typeof(PlayerZoneTracker))]
    public class DodgeballPlayerInput : MonoBehaviour
    {
        public static DodgeballPlayerInput Current { get; private set; }

        [Header("Power")]
        // Release speed comes from the shared rating→u/s curve on the Ball
        // (Ball.ReleaseSpeed), so human and AI throws use the same mapping.
        [SerializeField] private float lobPassSpeed = 10f;
        [SerializeField] private float chestPassSpeed = 18f;

        [Header("Throw accuracy")]
        [Tooltip("At accuracy 0, the aim point is offset by up to this many units PER unit of throw distance; " +
                 "scales to 0 at accuracy 100.")]
        [SerializeField] private float accuracyErrorPerUnit = 0.15f;
        [Tooltip("At accuracy 0, an untargeted throw's direction can be off by up to this many degrees.")]
        [SerializeField] private float maxInaccuracyAngleDeg = 25f;

        [Header("Charge throw (hold Square/Q to load up)")]
        [Tooltip("Hold the throw button this long (s) for full power; a quick tap is weak and wild.")]
        [SerializeField] private float maxChargeTime = 1.1f;
        [Tooltip("Throw power at minimum charge (tap), as a fraction of full.")]
        [Range(0.1f, 1f)] [SerializeField] private float tapPowerFraction = 0.5f;
        [Tooltip("Accuracy scatter multiplier at minimum charge (tap). >1 = wilder.")]
        [SerializeField] private float tapAccuracyMul = 1.8f;
        [Tooltip("Throw power right after a catch (unsettled), fraction of full; ramps up as the ball is secured (a rushed counter is weaker).")]
        [Range(0.1f, 1f)] [SerializeField] private float settlePowerFloor = 0.7f;
        [Tooltip("Accuracy scatter multiplier right after a catch (unsettled), fading to 1.")]
        [SerializeField] private float settleScatterMul = 1.6f;

        [Header("Pass timing")]
        [Tooltip("Hold longer than this (seconds) for chest pass; release sooner for lob.")]
        [SerializeField] private float passTapThreshold = 0.18f;

        [Header("Throw aim assist")]
        [Tooltip("Half-angle of the throw cone (degrees). Opponents whose bearing from the thrower " +
                 "lies within this many degrees of the press direction are eligible targets; the " +
                 "nearest one becomes the actual throw target.")]
        [SerializeField] private float throwConeHalfAngleDegrees = 15f;

        [Header("Run mode (D-pad second-tap-while-moving)")]
        [Tooltip("Seconds with no movement input before run mode disengages. " +
                 "Tiny grace lets D-pad rolls between directions stay running.")]
        [SerializeField] private float runReleaseGrace = 0.08f;
        [Tooltip("How long the previous D-pad press direction stays \"in memory\" after movement " +
                 "input has dropped (seconds). A second press of that direction within this window " +
                 "engages run; longer than this and the next press is a fresh first tap.")]
        [SerializeField] private float directionMemorySeconds = 0.5f;

        private PlayerMovement movement;
        private PlayerZoneTracker tracker;
        private DodgeballInputActions actions;

        private Vector2 lastMoveDirection = Vector2.right;
        private float passPressTime = -1f;
        private float throwChargeStart = -1f;   // when Square/Q was pressed (charge clock)

        /// <summary>Telemetry of the last user attack throw, consumed by the attack-tuning lab.</summary>
        public struct UserAttackInfo
        {
            public bool valid;
            public float time;
            public string type;     // Stationary / Running / Jump / RunJump
            public string input;    // run / charge / settle execution summary
            public float power;     // computed throw power (pre-momentum)
            public float aimError;  // distance (u) the post-scatter aim missed the target center; -1 = no target
            public PlayerZoneTracker target;
        }
        public static UserAttackInfo LastUserAttack;

        private bool isRunning;
        private float idleTime;

        // Defensive stance (R2 / Left-Ctrl toggle): face the ball + move slower.
        private bool inStance;
        private Ball cachedBall;            // ball ref (stance facing + attach subscription)
        private bool subscribedToBall;

        // Direction memory for second-tap-while-moving run detection. The
        // previous D-pad press direction is held until movement has been
        // idle for longer than directionMemorySeconds; pressing that same
        // direction inside the window engages run.
        private int previousDpadDirIndex = -1;
        private float lastMovementActiveTime = -10f;

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            tracker = GetComponent<PlayerZoneTracker>();

            // Human input overrides the CPU brain while this component lives.
            var ai = GetComponent<DodgeballAI>();
            if (ai != null) ai.enabled = false;

            actions = new DodgeballInputActions();
            actions.Evade.performed      += OnJumpPressed;
            actions.Throw.started        += OnThrowStarted;
            actions.Throw.canceled       += OnThrowReleased;
            actions.Circle.started       += OnCircleStarted;
            actions.Circle.canceled      += OnCircleCanceled;
            actions.Switch.performed     += OnSwitchPressed;
            actions.Stance.performed     += OnStancePressed;
            actions.ReturnBall.performed += OnReturnBallPressed;
            actions.DpadUp.started       += OnDpadUpPressed;
            actions.DpadDown.started     += OnDpadDownPressed;
            actions.DpadLeft.started     += OnDpadLeftPressed;
            actions.DpadRight.started    += OnDpadRightPressed;

            Current = this;
            EnsureBallSubscription();
        }

        private void OnEnable()  => actions?.Enable();
        private void OnDisable() => actions?.Disable();

        private void OnDestroy()
        {
            if (actions != null)
            {
                actions.Evade.performed      -= OnJumpPressed;
                actions.Throw.started        -= OnThrowStarted;
                actions.Throw.canceled       -= OnThrowReleased;
                actions.Circle.started       -= OnCircleStarted;
                actions.Circle.canceled      -= OnCircleCanceled;
                actions.Switch.performed     -= OnSwitchPressed;
                actions.Stance.performed     -= OnStancePressed;
                actions.ReturnBall.performed -= OnReturnBallPressed;
                actions.DpadUp.started       -= OnDpadUpPressed;
                actions.DpadDown.started     -= OnDpadDownPressed;
                actions.DpadLeft.started     -= OnDpadLeftPressed;
                actions.DpadRight.started    -= OnDpadRightPressed;
                actions.Disable();
            }
            if (cachedBall != null && subscribedToBall) cachedBall.OnAttached -= OnBallAttached;
            if (Current == this) Current = null;

            // Hand the player back to its CPU brain when control leaves.
            var ai = GetComponent<DodgeballAI>();
            if (ai != null) ai.enabled = true;
        }

        private void Update()
        {
            EnsureBallSubscription();

            Vector2 input = actions.Move.ReadValue<Vector2>();
            if (input.sqrMagnitude > 0.04f)
            {
                lastMoveDirection = input.normalized;
                idleTime = 0f;
                lastMovementActiveTime = Time.unscaledTime;
            }
            else
            {
                idleTime += Time.deltaTime;
                if (idleTime >= runReleaseGrace) isRunning = false;
                // Once movement input has been idle long enough, forget the
                // previous direction so the next press starts a fresh session.
                if (Time.unscaledTime - lastMovementActiveTime > directionMemorySeconds)
                {
                    previousDpadDirIndex = -1;
                }
            }

            // Defensive stance: drop it on gaining the ball; otherwise face the
            // ball each frame so the player backpedals/strafes. PlayerMovement
            // applies the slow-down; the catch/evade upside comes for free.
            if (inStance && tracker.HasBall) { inStance = false; movement.SetStance(false); }

            // L2 held (Sprint binding) → look for the ball: snap facing toward
            // the ball each frame, the same way stance does. Momentary instead
            // of a toggle. Carrying disables it — facing your own held ball is
            // a no-op anyway.
            bool lookForBall = actions.Sprint.IsPressed() && !tracker.HasBall;
            if (inStance || lookForBall)
            {
                if (cachedBall == null) cachedBall = FindAnyObjectByType<Ball>();
                if (cachedBall != null)
                    movement.SetFacing((Vector2)cachedBall.transform.position - (Vector2)transform.position);
            }

            // Keep the catch armed through a dive so the extended-reach grab
            // lands (AI still dives; the human no longer starts one from Cross).
            if (movement.IsDiving) tracker.ArmCatch();

            movement.IsRunning = isRunning || actions.Sprint.IsPressed();
            movement.ApplyMove(input);
        }

        // Cross is always a jump. Holding the ball it's an attack hop so a
        // throw can cross a line before landing; empty-handed it's the same
        // hop. Duck / dash / dive used to share this button and
        // made X feel unreliable — those stay available to the AI.
        private void OnJumpPressed(InputAction.CallbackContext _)
        {
            movement.TryJump(attackJump: tracker.HasBall);
        }

        private void OnDpadUpPressed(InputAction.CallbackContext _)    => HandleDpadPress(0);
        private void OnDpadDownPressed(InputAction.CallbackContext _)  => HandleDpadPress(1);
        private void OnDpadLeftPressed(InputAction.CallbackContext _)  => HandleDpadPress(2);
        private void OnDpadRightPressed(InputAction.CallbackContext _) => HandleDpadPress(3);

        // Run engages on a second press of the direction the player is
        // currently moving in. Time between presses is irrelevant — what
        // matters is that movement has stayed active (input non-zero within
        // directionMemorySeconds) since the previous press of this same
        // direction. The check uses previousDpadDirIndex, which the Update
        // loop resets when movement has been idle long enough.
        private void HandleDpadPress(int dirIndex)
        {
            if (previousDpadDirIndex == dirIndex)
            {
                isRunning = true;
            }
            previousDpadDirIndex = dirIndex;
            lastMovementActiveTime = Time.unscaledTime;
        }

        // Square/Q: charge a throw. Does nothing empty-handed — switching
        // players is Triangle on defense.
        private void OnThrowStarted(InputAction.CallbackContext _)
        {
            if (!tracker.HasBall) return;
            throwChargeStart = Time.unscaledTime;
        }

        // Released: fire with power + accuracy scaled by how long it was charged
        // (tap = weak/wild, hold to maxChargeTime = hard/accurate), minus the
        // rushed-counter settle penalty if we just caught.
        private void OnThrowReleased(InputAction.CallbackContext _)
        {
            if (throwChargeStart < 0f) return;   // wasn't charging (pressed with no ball)
            float held = Time.unscaledTime - throwChargeStart;
            throwChargeStart = -1f;

            var ball = tracker.HeldBall;
            if (ball == null) return;
            if (!tracker.IsInZone && !movement.IsAirborne) { ball.Drop(); return; }

            float charge01 = Mathf.Clamp01(held / Mathf.Max(0.01f, maxChargeTime));
            float settle01 = movement.CatchSettle01;

            float power = ThrowReleaseSpeed()
                        * Mathf.Lerp(tapPowerFraction, 1f, charge01)
                        * Mathf.Lerp(settlePowerFloor, 1f, settle01);

            var target = FindThrowTargetInCone(lastMoveDirection);
            ball.IntendedTarget = target;   // for the play-by-play log (may be null)
            if (target != null)
            {
                // Anticipation leads the target; accuracy then scatters the aim.
                Vector2 lead = ball.LeadAim(transform.position, target.transform.position,
                                            TargetVelocity(target), power, OwnAnticipation01());
                Vector2 aim = ApplyAccuracyToAim(lead, charge01, settle01);
                CaptureUserAttack(held, settle01, power, target, aim);
                ball.ThrowAt(aim, power);
            }
            else
            {
                Vector2 dir = lastMoveDirection.sqrMagnitude > 0.0001f
                    ? lastMoveDirection.normalized
                    : Vector2.right;
                CaptureUserAttack(held, settle01, power, null, Vector2.zero);
                ball.Throw(ApplyAccuracyToDirection(dir, charge01, settle01), power);
            }
        }

        /// <summary>
        /// Programmatic throw at a target for the attack-lab auto-sweep: same power /
        /// aim / accuracy-scatter + telemetry as a human release at the given charge,
        /// with no settle penalty. Caller must give this player the ball first.
        /// </summary>
        public void AutoThrowAt(PlayerZoneTracker target, float charge01)
        {
            var ball = tracker.HeldBall;
            if (ball == null || target == null) return;
            charge01 = Mathf.Clamp01(charge01);
            const float settle01 = 1f;

            float power = ThrowReleaseSpeed()
                        * Mathf.Lerp(tapPowerFraction, 1f, charge01)
                        * Mathf.Lerp(settlePowerFloor, 1f, settle01);

            ball.IntendedTarget = target;
            Vector2 lead = ball.LeadAim(transform.position, target.transform.position,
                                        TargetVelocity(target), power, OwnAnticipation01());
            Vector2 aim = ApplyAccuracyToAim(lead, charge01, settle01);
            CaptureUserAttack(charge01 * maxChargeTime, settle01, power, target, aim);
            ball.ThrowAt(aim, power);
        }

        // Snapshot the attack's execution for the tuning lab: classify the type
        // from the movement state and summarize the input (run / charge /
        // settle), the computed power, and the aim error vs the target center.
        private void CaptureUserAttack(float held, float settle01, float power, PlayerZoneTracker target, Vector2 aim)
        {
            string type = movement.IsAirborne ? (movement.Velocity.magnitude > 4f ? "RunJump" : "Jump")
                        : movement.IsRunning ? "Running"
                        : "Stationary";
            string input = (movement.IsRunning ? "run " : "") + $"chg {held:F2}s"
                         + (settle01 < 0.999f ? $" settle {settle01:F2}" : "");
            LastUserAttack = new UserAttackInfo
            {
                valid = true,
                time = Time.time,
                type = type,
                input = input,
                power = power,
                aimError = target != null ? Vector2.Distance(aim, (Vector2)target.transform.position) : -1f,
                target = target,
            };
        }

        // Release speed comes straight from the thrower's ThrowSpeedRating.
        private float ThrowReleaseSpeed()
        {
            var attr = GetComponent<DodgeballAttributes>();
            float s01 = attr != null ? attr.EffectiveThrowSpeed01 : 0.6f;
            if (cachedBall == null) cachedBall = FindAnyObjectByType<Ball>();
            return cachedBall != null ? cachedBall.ReleaseSpeed(s01) : Mathf.Lerp(12f, 36f, s01);
        }

        private float OwnAnticipation01()
        {
            var attr = GetComponent<DodgeballAttributes>();
            return attr != null ? attr.EffectiveAnticipation01 : 0f;
        }

        private static Vector2 TargetVelocity(PlayerZoneTracker t)
        {
            var rb = t.GetComponent<Rigidbody2D>();
            return rb != null ? rb.linearVelocity : Vector2.zero;
        }

        // Accuracy scatters the aim point; the miss grows with distance, with how
        // far below 100 the thrower's accuracy is, with a low charge (a quick tap
        // is wilder), and with a low settle (a rushed counter off a fresh catch).
        private Vector2 ApplyAccuracyToAim(Vector2 aimPoint, float charge01 = 1f, float settle01 = 1f)
        {
            var attr = GetComponent<DodgeballAttributes>();
            float acc01 = attr != null ? attr.EffectiveThrowAccuracy01 : 0.6f;
            float dist = Vector2.Distance(transform.position, aimPoint);
            float scatter = Mathf.Lerp(tapAccuracyMul, 1f, charge01) * Mathf.Lerp(settleScatterMul, 1f, settle01);
            float maxError = (1f - acc01) * accuracyErrorPerUnit * dist * scatter;
            return aimPoint + Random.insideUnitCircle * maxError;
        }

        // Untargeted throws scatter on angle instead of a point.
        private Vector2 ApplyAccuracyToDirection(Vector2 dir, float charge01 = 1f, float settle01 = 1f)
        {
            var attr = GetComponent<DodgeballAttributes>();
            float acc01 = attr != null ? attr.EffectiveThrowAccuracy01 : 0.6f;
            float scatter = Mathf.Lerp(tapAccuracyMul, 1f, charge01) * Mathf.Lerp(settleScatterMul, 1f, settle01);
            float maxAngle = (1f - acc01) * maxInaccuracyAngleDeg * scatter;
            float angle = Random.Range(-maxAngle, maxAngle);
            Vector2 rotated = Quaternion.Euler(0f, 0f, angle) * (Vector3)dir;
            return rotated.normalized;
        }

        /// <summary>
        /// Aim-assist target: nearest opponent whose bearing from the thrower
        /// is within throwConeHalfAngleDegrees of the press direction, or null.
        /// </summary>
        private PlayerZoneTracker FindThrowTargetInCone(Vector2 desired)
        {
            if (desired.sqrMagnitude < 0.0001f) desired = Vector2.right;
            Vector2 dirNorm = desired.normalized;

            float cosThreshold = Mathf.Cos(throwConeHalfAngleDegrees * Mathf.Deg2Rad);
            Vector2 origin = transform.position;
            var team = tracker.Spawn.team;

            PlayerZoneTracker best = null;
            float bestDistSq = float.MaxValue;

            var trackers = PlayerZoneTracker.All;
            for (int i = 0; i < trackers.Count; i++)
            {
                var t = trackers[i];
                if (t == null || t == tracker) continue;
                if (t.Spawn.team == team) continue;  // throw targets opponents only

                Vector2 toT = (Vector2)t.transform.position - origin;
                float distSq = toT.sqrMagnitude;
                if (distSq < 0.0001f) continue;
                if (Vector2.Dot(dirNorm, toT / Mathf.Sqrt(distSq)) < cosThreshold) continue;

                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = t;
                }
            }
            return best;
        }

        // Circle / E: pass while holding the ball (tap = lob, hold = chest),
        // catch while empty-handed.
        private void OnCircleStarted(InputAction.CallbackContext _)
        {
            if (tracker.HasBall) passPressTime = Time.unscaledTime;
            else tracker.ArmCatch();
        }

        private void OnCircleCanceled(InputAction.CallbackContext _)
        {
            if (passPressTime < 0f) return;
            float duration = Time.unscaledTime - passPressTime;
            passPressTime = -1f;

            var ball = tracker.HeldBall;
            if (ball == null) return;
            DoPass(ball, isChest: duration >= passTapThreshold);
        }

        private void DoPass(Ball ball, bool isChest)
        {
            if (!tracker.IsInZone && !movement.IsAirborne) { ball.Drop(); return; }   // pass while ineligible -> drop
            var target = FindPassTarget();
            if (target == null) return;

            // Parametric drive — ball lerps to target's current position; lob
            // adds an arc, chest stays flat. Speed sets the flight duration.
            ball.IntendedTarget = target;
            ball.Pass(target.transform.position,
                      isChest ? chestPassSpeed : lobPassSpeed,
                      isLob: !isChest);
        }

        /// <summary>
        /// Picks the same-team teammate whose direction from the holder is
        /// most aligned with lastMoveDirection (cosine similarity).
        /// </summary>
        private PlayerZoneTracker FindPassTarget()
        {
            PlayerZoneTracker best = null;
            float bestScore = -2f;
            var team = tracker.Spawn.team;
            Vector2 origin = transform.position;

            var trackers = PlayerZoneTracker.All;
            for (int i = 0; i < trackers.Count; i++)
            {
                var t = trackers[i];
                if (t == null || t == tracker) continue;
                if (t.Spawn.team != team) continue;

                Vector2 toT = (Vector2)t.transform.position - origin;
                if (toT.sqrMagnitude < 0.0001f) continue;

                float score = Vector2.Dot(toT.normalized, lastMoveDirection);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = t;
                }
            }
            return best;
        }

        private void OnSwitchPressed(InputAction.CallbackContext _)
        {
            if (tracker.HasBall) return;
            SwitchToClosestTeammate();
        }

        // Toggle the defensive stance. No stance while carrying the ball — that's
        // offense. Update() faces the ball and PlayerMovement applies the slow-down.
        private void OnStancePressed(InputAction.CallbackContext _)
        {
            if (tracker.HasBall) return;
            inStance = !inStance;
            movement.SetStance(inStance);
        }

        private void OnReturnBallPressed(InputAction.CallbackContext _)
        {
            var ball = FindAnyObjectByType<Ball>();
            if (ball != null) ball.ForcePickup(tracker);
        }

        // Control follows possession: whenever a player on our team gains the
        // ball — catch, loose-ball pickup, retrieval, or pass — hand control to
        // them. An opponent gaining the ball leaves control where it is.
        private void OnBallAttached(PlayerZoneTracker carrier)
        {
            if (carrier == null || carrier == tracker) return;
            if (carrier.Spawn.team != tracker.Spawn.team) return;
            TransferControl(carrier.gameObject);
        }

        // Subscribe once to the ball's attach event (the ball may not exist yet
        // at Awake, so this is retried from Update until it takes).
        private void EnsureBallSubscription()
        {
            if (subscribedToBall) return;
            if (cachedBall == null) cachedBall = FindAnyObjectByType<Ball>();
            if (cachedBall == null) return;
            cachedBall.OnAttached += OnBallAttached;
            subscribedToBall = true;
        }

        // Triangle / F, empty-handed: take the nearest teammate so you can
        // step in on a play. Possession-follows-control (OnBallAttached) still
        // jumps you to a teammate who actually picks the ball up.
        private void SwitchToClosestTeammate()
        {
            PlayerZoneTracker best = null;
            float bestDistSq = float.MaxValue;
            Vector2 here = transform.position;
            var team = tracker.Spawn.team;
            var trackers = PlayerZoneTracker.All;
            for (int i = 0; i < trackers.Count; i++)
            {
                var t = trackers[i];
                if (t == null || t == tracker || t.Spawn.team != team) continue;
                if (!t.gameObject.activeInHierarchy) continue;
                float d = ((Vector2)t.transform.position - here).sqrMagnitude;
                if (d < bestDistSq) { bestDistSq = d; best = t; }
            }
            if (best != null) TransferControl(best.gameObject);
        }

        /// <summary>
        /// Move human control to <paramref name="target"/>: tears down the
        /// current input component and adds a fresh one on the target.
        /// </summary>
        public static void TransferControl(GameObject target)
        {
            if (Current != null)
            {
                Current.actions?.Disable();
                Destroy(Current);
            }
            target.AddComponent<DodgeballPlayerInput>();
        }
    }
}
