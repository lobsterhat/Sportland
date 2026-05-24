using UnityEngine;
using UnityEngine.InputSystem;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Routes PS4 / keyboard input into a single human-controlled Dodgeball
    /// player. Move uses analog magnitude; L2 holds sprint. Square throws,
    /// Triangle passes (tap = lob, hold = chest), Circle catches. Cross is the
    /// smart-evade button: holding the ball it jumps (cross a line to throw);
    /// empty-handed, a held direction dashes off the ball's line while a neutral
    /// stick ducks a high throw or jumps a low one. L1 force-returns the ball
    /// for testing.
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
        [Tooltip("Release speed (u/s) at throwSpeed rating 0 and 100; the thrower's rating lerps between them.")]
        [SerializeField] private float minThrowSpeed = 12f;
        [SerializeField] private float maxThrowSpeed = 36f;
        [SerializeField] private float lobPassSpeed = 6f;
        [SerializeField] private float chestPassSpeed = 12f;

        [Header("Throw accuracy")]
        [Tooltip("At accuracy 0, the aim point is offset by up to this many units PER unit of throw distance; " +
                 "scales to 0 at accuracy 100.")]
        [SerializeField] private float accuracyErrorPerUnit = 0.15f;
        [Tooltip("At accuracy 0, an untargeted throw's direction can be off by up to this many degrees.")]
        [SerializeField] private float maxInaccuracyAngleDeg = 25f;

        [Header("Pass timing")]
        [Tooltip("Hold longer than this (seconds) for chest pass; release sooner for lob.")]
        [SerializeField] private float passTapThreshold = 0.18f;

        [Header("Evade")]
        [Tooltip("Neutral-stick evade auto-picks a verb from the incoming throw's predicted " +
                 "arrival height: at/above this, duck under it; below, jump over it.")]
        [SerializeField] private float evadeDuckHeight = 0.75f;

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

        private bool isRunning;
        private float idleTime;

        // Direction memory for second-tap-while-moving run detection. The
        // previous D-pad press direction is held until movement has been
        // idle for longer than directionMemorySeconds; pressing that same
        // direction inside the window engages run.
        private int previousDpadDirIndex = -1;
        private float lastMovementActiveTime = -10f;

        // Pending pass we issued: the catch handler watches for this ball to
        // arrive at intendedPassTarget so it can hand control off.
        private Ball passedBall;
        private PlayerZoneTracker intendedPassTarget;

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            tracker = GetComponent<PlayerZoneTracker>();

            // Human input overrides the CPU brain while this component lives.
            var ai = GetComponent<DodgeballAI>();
            if (ai != null) ai.enabled = false;

            actions = new DodgeballInputActions();
            actions.Evade.performed      += OnEvadePressed;
            actions.Throw.performed      += OnThrowPressed;
            actions.Pass.started         += OnPassStarted;
            actions.Pass.canceled        += OnPassCanceled;
            actions.Catch.performed      += OnCatchPressed;
            actions.ReturnBall.performed += OnReturnBallPressed;
            actions.DpadUp.started       += OnDpadUpPressed;
            actions.DpadDown.started     += OnDpadDownPressed;
            actions.DpadLeft.started     += OnDpadLeftPressed;
            actions.DpadRight.started    += OnDpadRightPressed;

            Current = this;
        }

        private void OnEnable()  => actions?.Enable();
        private void OnDisable() => actions?.Disable();

        private void OnDestroy()
        {
            if (actions != null)
            {
                actions.Evade.performed      -= OnEvadePressed;
                actions.Throw.performed      -= OnThrowPressed;
                actions.Pass.started         -= OnPassStarted;
                actions.Pass.canceled        -= OnPassCanceled;
                actions.Catch.performed      -= OnCatchPressed;
                actions.ReturnBall.performed -= OnReturnBallPressed;
                actions.DpadUp.started       -= OnDpadUpPressed;
                actions.DpadDown.started     -= OnDpadDownPressed;
                actions.DpadLeft.started     -= OnDpadLeftPressed;
                actions.DpadRight.started    -= OnDpadRightPressed;
                actions.Disable();
            }
            if (passedBall != null) passedBall.OnAttached -= OnPassedBallCaught;
            if (Current == this) Current = null;

            // Hand the player back to its CPU brain when control leaves.
            var ai = GetComponent<DodgeballAI>();
            if (ai != null) ai.enabled = true;
        }

        private void Update()
        {
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

            movement.IsRunning = isRunning || actions.Sprint.IsPressed();
            movement.ApplyMove(input);
        }

        // Cross is the smart-evade button. Holding the ball it's the offensive
        // jump (cross a line and throw before landing). Empty-handed it's a
        // context evade: a held direction dashes off the ball's line; a neutral
        // stick reads the incoming throw and ducks a high ball / jumps a low one.
        private void OnEvadePressed(InputAction.CallbackContext _)
        {
            if (tracker.HasBall) { movement.TryJump(); return; }

            Vector2 dir = actions.Move.ReadValue<Vector2>();
            if (dir.sqrMagnitude > 0.04f) { movement.Dash(dir); return; }

            if (TryGetIncomingThrowHeight(out float predictedHeight))
            {
                if (predictedHeight >= evadeDuckHeight) movement.Duck();
                else                                    movement.TryJump();
            }
            else
            {
                movement.Duck();   // nothing incoming — brief crouch
            }
        }

        // Predicted arrival height of an in-flight thrown ball at our position,
        // used to auto-pick duck vs jump for a neutral-stick evade.
        private bool TryGetIncomingThrowHeight(out float predictedHeight)
        {
            predictedHeight = 0f;
            var ball = FindAnyObjectByType<Ball>();
            if (ball == null || ball.CurrentState != Ball.State.Thrown) return false;
            float dist = Vector2.Distance(transform.position, ball.transform.position);
            predictedHeight = ball.PredictHeightAfter(dist);
            return true;
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

        private void OnThrowPressed(InputAction.CallbackContext _)
        {
            var ball = tracker.HeldBall;
            if (ball == null) return;

            float power = ThrowReleaseSpeed();
            var target = FindThrowTargetInCone(lastMoveDirection);
            if (target != null)
            {
                // Anticipation leads the target; accuracy then scatters the aim.
                Vector2 lead = ball.LeadAim(transform.position, target.transform.position,
                                            TargetVelocity(target), power, OwnAnticipation01());
                Vector2 aim = ApplyAccuracyToAim(lead);
                ball.ThrowAt(aim, power);
            }
            else
            {
                Vector2 dir = lastMoveDirection.sqrMagnitude > 0.0001f
                    ? lastMoveDirection.normalized
                    : Vector2.right;
                ball.Throw(ApplyAccuracyToDirection(dir), power);
            }
        }

        // Release speed comes straight from the thrower's throwSpeed rating.
        private float ThrowReleaseSpeed()
        {
            var attr = GetComponent<DodgeballAttributes>();
            float s01 = attr != null ? attr.ThrowSpeed01 : 0.6f;
            return Mathf.Lerp(minThrowSpeed, maxThrowSpeed, s01);
        }

        private float OwnAnticipation01()
        {
            var attr = GetComponent<DodgeballAttributes>();
            return attr != null ? attr.Anticipation01 : 0f;
        }

        private static Vector2 TargetVelocity(PlayerZoneTracker t)
        {
            var rb = t.GetComponent<Rigidbody2D>();
            return rb != null ? rb.linearVelocity : Vector2.zero;
        }

        // Accuracy scatters the aim point; the miss grows with distance and with
        // how far below 100 the thrower's accuracy is.
        private Vector2 ApplyAccuracyToAim(Vector2 aimPoint)
        {
            var attr = GetComponent<DodgeballAttributes>();
            float acc01 = attr != null ? attr.ThrowAccuracy01 : 0.6f;
            float dist = Vector2.Distance(transform.position, aimPoint);
            float maxError = (1f - acc01) * accuracyErrorPerUnit * dist;
            return aimPoint + Random.insideUnitCircle * maxError;
        }

        // Untargeted throws scatter on angle instead of a point.
        private Vector2 ApplyAccuracyToDirection(Vector2 dir)
        {
            var attr = GetComponent<DodgeballAttributes>();
            float acc01 = attr != null ? attr.ThrowAccuracy01 : 0.6f;
            float maxAngle = (1f - acc01) * maxInaccuracyAngleDeg;
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

        private void OnPassStarted(InputAction.CallbackContext _)
        {
            passPressTime = Time.unscaledTime;
        }

        private void OnPassCanceled(InputAction.CallbackContext _)
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
            var target = FindPassTarget();
            if (target == null) return;

            // Clear any stale subscription before recording the new pass.
            if (passedBall != null) passedBall.OnAttached -= OnPassedBallCaught;
            passedBall = ball;
            intendedPassTarget = target;
            passedBall.OnAttached += OnPassedBallCaught;

            // Parametric drive — ball lerps to target's current position; lob
            // adds an arc, chest stays flat. Speed sets the flight duration.
            ball.Pass(target.transform.position,
                      isChest ? chestPassSpeed : lobPassSpeed,
                      isLob: !isChest);
        }

        private void OnPassedBallCaught(PlayerZoneTracker catcher)
        {
            if (passedBall == null) return;
            passedBall.OnAttached -= OnPassedBallCaught;

            var target = intendedPassTarget;
            passedBall = null;
            intendedPassTarget = null;

            // Only the intended teammate's catch triggers a control handoff;
            // interceptions leave control where it was.
            if (catcher == target)
            {
                TransferControl(catcher.gameObject);
            }
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

        private void OnCatchPressed(InputAction.CallbackContext _)
        {
            // Press-window reaction: arming a catch lets the Ball resolve a
            // skill-checked catch when it arrives within reach. (A slow loose
            // ball at the player's feet is still a free walk-over pickup.)
            if (tracker.HasBall) return;
            tracker.ArmCatch();
        }

        private void OnReturnBallPressed(InputAction.CallbackContext _)
        {
            var ball = FindAnyObjectByType<Ball>();
            if (ball != null) ball.ForcePickup(tracker);
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
