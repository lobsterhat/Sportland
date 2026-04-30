using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sportland.Sports.Demoball
{
    /// <summary>
    /// Routes keyboard / gamepad input to DemoballMovementController using
    /// Unity's new Input System (see DemoballInputActions for bindings).
    ///
    /// Controls:
    ///   Left Stick / WASD / Arrows  — Move
    ///   Right Stick                 — Aim a pass while carrying (selects target)
    ///   L2 / Left Shift             — Sprint (hold)
    ///   R1     / E                  — Pick up nearest loose ball, or pass if carrying
    ///   Circle / Q                  — Touch-down score (in scoring ring while carrying)
    ///   Square / T                  — Tackle nearest ball-carrier (Defenders only)
    ///   Triangle / G                — Shove engaged opponent (Blockers and Defenders, when engaged)
    ///
    /// On a successful pass, control is transferred to the receiver's broker
    /// automatically.
    /// </summary>
    [RequireComponent(typeof(DemoballMovementController))]
    public class DemoballInputBroker : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        //  CONFIGURATION
        // ──────────────────────────────────────────────

        [Tooltip("When true this instance reads keyboard/gamepad input. " +
                 "Set false for AI-controlled players.")]
        [SerializeField] private bool playerControlled = true;

        [Tooltip("Radius (world units) within which the player can pick up a loose ball.")]
        [SerializeField] private float pickupRadius = 1.2f;

        [Header("=== PASSING ===")]
        [Tooltip("All offensive teammates this player can pass to. Self may be included; it is filtered at runtime.")]
        [SerializeField] private List<DemoballMovementController> teammates;

        [Tooltip("Right-stick magnitude below this is treated as no aim (no target highlighted).")]
        [SerializeField] private float aimDeadzone = 0.3f;

        [Tooltip("Hold time below this counts as a tap (pitch). Above this counts as a hold (football).")]
        [SerializeField] private float passTapThreshold = 0.18f;

        [Tooltip("Hold time at or above this caps the pass at maximum (football) power.")]
        [SerializeField] private float passMaxChargeTime = 0.6f;

        [Header("=== PLAYS ===")]
        [Tooltip("Forward depth of the lead blocker on a release play (along the carrier's facing).")]
        [SerializeField] private float leadBlockerForward = 2f;

        [Tooltip("Lateral offset of the lead blocker on a release play (toward the chosen side).")]
        [SerializeField] private float leadBlockerLateral = 3f;

        [Tooltip("Forward depth of the primary receiver on a release play.")]
        [SerializeField] private float receiverForward = 6f;

        [Tooltip("Lateral offset of the primary receiver on a release play.")]
        [SerializeField] private float receiverLateral = 4.5f;

        [Tooltip("How long routes stay active before blockers revert to flanking.")]
        [SerializeField] private float playDuration = 6f;

        // ──────────────────────────────────────────────
        //  PUBLIC ACCESSORS
        // ──────────────────────────────────────────────

        /// <summary>True if this broker is currently driven by player input.</summary>
        public bool IsPlayerControlled => playerControlled;

        // ──────────────────────────────────────────────
        //  RUNTIME
        // ──────────────────────────────────────────────

        private DemoballMovementController movement;
        private DemoballInputActions controls;
        private DemoballMovementController currentPassTarget;
        private float passPressTime;     // Time.time when Action was pressed while carrying
        private bool passCharging;

        // ──────────────────────────────────────────────
        //  LIFECYCLE
        // ──────────────────────────────────────────────

        /// <summary>Active brokers in the scene. Visuals iterate this to find the player-controlled teammate.</summary>
        public static readonly List<DemoballInputBroker> All = new List<DemoballInputBroker>();

        private void Awake()
        {
            movement = GetComponent<DemoballMovementController>();
            controls = new DemoballInputActions();
        }

        private void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
            if (playerControlled) controls.Enable();
        }

        private void OnDisable()
        {
            All.Remove(this);
            controls.Disable();
            ClearPassTarget();
        }

        private void Update()
        {
            if (!playerControlled) return;

            // ── Locomotion ──
            Vector2 moveInput = controls.Move.ReadValue<Vector2>();
            if (moveInput.sqrMagnitude > 1f) moveInput.Normalize();
            movement.SetMoveInput(moveInput);

            // ── Sprint ──
            movement.SetSprinting(controls.Sprint.IsPressed());

            // ── Pass target selection (right stick, only while carrying) ──
            UpdatePassTarget();

            // ── Action: R1 / E — context-sensitive ──
            // Carrying: tap = pitch, hold = football pass. Pass fires on release.
            // Not carrying: instant pickup attempt on press.
            if (controls.Action.WasPressedThisFrame())
            {
                if (movement.IsCarryingBall)
                {
                    passPressTime = Time.time;
                    passCharging  = true;
                }
                else
                {
                    TryPickUpNearest();
                }
            }

            if (passCharging && controls.Action.WasReleasedThisFrame())
            {
                AttemptPass(Time.time - passPressTime);
                passCharging = false;
            }
            // Drop the charge if we lose the ball mid-hold (tackled, etc.)
            else if (passCharging && !movement.IsCarryingBall)
            {
                passCharging = false;
            }

            // ── Touch-down: R1 / Q ──
            if (controls.TouchDown.WasPressedThisFrame())
                movement.TryTouchDown();

            // ── Tackle: Square / T ──
            if (controls.Tackle.WasPressedThisFrame())
                movement.TryTackle();

            // ── Shove: Triangle / G ── (CanShove gates role + engagement + cooldown)
            if (controls.Shove.WasPressedThisFrame() && movement.CanShove)
                movement.TryShove(out _);

            // ── Plays (carrier only) ──
            if (movement.IsCarryingBall)
            {
                if      (controls.BlockPlay.WasPressedThisFrame())        CallBlockPlay();
                else if (controls.ReleaseLeftPlay.WasPressedThisFrame())  CallReleasePlay( 1f);
                else if (controls.ReleaseRightPlay.WasPressedThisFrame()) CallReleasePlay(-1f);
            }

            // ── Debug: D-pad Up / R reloads the scene ──
            if (controls.DebugReset.WasPressedThisFrame())
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);

            // ── Debug: D-pad Down / B fires another ball ──
            if (controls.DebugSpawnBall.WasPressedThisFrame())
                FireExtraBall();
        }

        // ──────────────────────────────────────────────
        //  PLAY CALLING
        // ──────────────────────────────────────────────

        /// <summary>
        /// Block play — cancel any active routes so all blockers revert to
        /// flank-and-block on the carrier.
        /// </summary>
        private void CallBlockPlay()
        {
            if (teammates == null) return;
            foreach (var t in teammates)
            {
                if (t == null || t == movement) continue;
                var ai = t.GetComponent<DemoballFlankAi>();
                if (ai != null) ai.EndRoute();
            }
        }

        /// <summary>
        /// Release play — both blockers release to one side of the carrier.
        /// `lateralSign` is +1 for the carrier's left, -1 for the carrier's
        /// right (perpendicular to facing direction).
        ///
        /// The blocker most aligned with the right-stick aim direction (from
        /// the carrier's perspective) is the lead blocker and gets the
        /// shallower route; the other becomes the primary receiver and gets
        /// the deeper route. With no aim input we default to the carrier's
        /// facing so the player closest to "in front" leads.
        /// </summary>
        private void CallReleasePlay(float lateralSign)
        {
            if (teammates == null) return;

            Vector2 origin = transform.position;
            Vector2 facing = movement.GetFacingDirection();
            if (facing.sqrMagnitude < 0.001f) facing = Vector2.right;
            Vector2 lateral = Vector2.Perpendicular(facing) * lateralSign; // +1 = left, -1 = right

            // Snapshot aim — fall back to facing if right-stick is centred.
            Vector2 aim = controls.Aim.ReadValue<Vector2>();
            if (aim.sqrMagnitude < aimDeadzone * aimDeadzone) aim = facing;
            aim.Normalize();

            // Collect eligible blockers.
            DemoballMovementController bestLead = null;
            DemoballMovementController other    = null;
            float bestAlign = float.NegativeInfinity;

            foreach (var t in teammates)
            {
                if (t == null || t == movement) continue;
                if (t.NeedsTagUp || t.IsCarryingBall) continue;
                if (t.GetComponent<DemoballFlankAi>() == null) continue;

                Vector2 toBlocker = (Vector2)t.transform.position - origin;
                if (toBlocker.sqrMagnitude < 0.0001f) continue;
                float align = Vector2.Dot(toBlocker.normalized, aim);

                if (align > bestAlign)
                {
                    if (bestLead != null) other = bestLead; // demoted to receiver
                    bestLead = t;
                    bestAlign = align;
                }
                else if (other == null)
                {
                    other = t;
                }
            }

            if (bestLead == null) return;

            Vector2 leadDest     = origin + facing * leadBlockerForward + lateral * leadBlockerLateral;
            Vector2 receiverDest = origin + facing * receiverForward    + lateral * receiverLateral;

            bestLead.GetComponent<DemoballFlankAi>().RunRoute(leadDest, playDuration);
            if (other != null)
                other.GetComponent<DemoballFlankAi>().RunRoute(receiverDest, playDuration);
        }

        // ──────────────────────────────────────────────
        //  DEBUG SHORTCUTS
        // ──────────────────────────────────────────────

        private BallCannon cachedCannon;

        private void FireExtraBall()
        {
            if (cachedCannon == null)
                cachedCannon = Object.FindFirstObjectByType<BallCannon>();
            if (cachedCannon != null) cachedCannon.Fire();
        }

        // ──────────────────────────────────────────────
        //  PASS TARGETING
        // ──────────────────────────────────────────────

        private void UpdatePassTarget()
        {
            DemoballMovementController next = null;

            if (movement.IsCarryingBall && teammates != null)
            {
                Vector2 aim = controls.Aim.ReadValue<Vector2>();
                if (aim.sqrMagnitude >= aimDeadzone * aimDeadzone)
                    next = FindTargetAlongAim(aim.normalized);
            }

            if (next != currentPassTarget)
            {
                if (currentPassTarget != null) currentPassTarget.IsPassTarget = false;
                currentPassTarget = next;
                if (currentPassTarget != null) currentPassTarget.IsPassTarget = true;
            }
        }

        private DemoballMovementController FindTargetAlongAim(Vector2 aimDir)
        {
            DemoballMovementController best = null;
            float bestScore = float.NegativeInfinity;
            Vector2 selfPos = transform.position;

            foreach (var t in teammates)
            {
                if (t == null || t == movement) continue;
                if (t.Role == DemoballRole.Defender) continue;
                if (t.NeedsTagUp || t.IsCarryingBall) continue;

                Vector2 toTeam = (Vector2)t.transform.position - selfPos;
                if (toTeam.sqrMagnitude < 0.0001f) continue;

                // Score = directional alignment, gently weighted by closeness.
                // Pure dot product picks the most "in line" teammate; the
                // 1/(1+dist) factor breaks ties in favour of the nearer one.
                float dot = Vector2.Dot(aimDir, toTeam.normalized);
                if (dot <= 0f) continue;  // ignore players behind the aim direction

                float score = dot / (1f + toTeam.magnitude * 0.1f);
                if (score > bestScore) { bestScore = score; best = t; }
            }
            return best;
        }

        private void ClearPassTarget()
        {
            if (currentPassTarget != null)
            {
                currentPassTarget.IsPassTarget = false;
                currentPassTarget = null;
            }
        }

        // ──────────────────────────────────────────────
        //  PASS EXECUTION + CONTROL HANDOFF
        // ──────────────────────────────────────────────

        private void AttemptPass(float holdSeconds)
        {
            // Snapshot target then clear visual immediately — TryPass may swap
            // control, after which we'd otherwise lose the chance to clear it.
            var target = currentPassTarget ?? FindNearestTeammate();
            ClearPassTarget();
            if (target == null) return;

            // Tap → pitch (power 0). Past the tap threshold the power ramps
            // linearly toward 1 over passMaxChargeTime - passTapThreshold.
            float power;
            if (holdSeconds < passTapThreshold)
            {
                power = 0f;
            }
            else
            {
                float chargeRange = Mathf.Max(0.001f, passMaxChargeTime - passTapThreshold);
                power = Mathf.Clamp01((holdSeconds - passTapThreshold) / chargeRange);
            }

            if (!movement.TryPass(target, power)) return;

            // Transfer control to the receiver immediately on pass-fire so the
            // user can steer them toward the catch (or chase a missed pass).
            var receiverBroker = target.GetComponent<DemoballInputBroker>();
            if (receiverBroker != null && receiverBroker != this)
            {
                SetPlayerControlled(false);
                receiverBroker.SetPlayerControlled(true);
            }
        }

        private DemoballMovementController FindNearestTeammate()
        {
            if (teammates == null) return null;
            DemoballMovementController best = null;
            float bestSqr = float.MaxValue;
            Vector2 selfPos = transform.position;

            foreach (var t in teammates)
            {
                if (t == null || t == movement) continue;
                if (t.Role == DemoballRole.Defender) continue;
                if (t.NeedsTagUp || t.IsCarryingBall) continue;

                float sqr = ((Vector2)t.transform.position - selfPos).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = t; }
            }
            return best;
        }

        // ──────────────────────────────────────────────
        //  PICKUP HELPERS
        // ──────────────────────────────────────────────

        private void TryPickUpNearest()
        {
            // Physics2D.queriesHitTriggers is true by default — this picks up
            // the Ball's trigger CircleCollider2D.
            var hits = Physics2D.OverlapCircleAll(transform.position, pickupRadius);

            Ball nearest     = null;
            float nearestDist = float.MaxValue;

            foreach (var hit in hits)
            {
                var ball = hit.GetComponent<Ball>();
                if (ball == null || !ball.CanBePickedUpBy(movement)) continue;

                float d = Vector2.Distance(transform.position, hit.transform.position);
                if (d < nearestDist) { nearestDist = d; nearest = ball; }
            }

            if (nearest != null)
                movement.TryPickUpBall(nearest);
        }

        // ──────────────────────────────────────────────
        //  PUBLIC API  (call from game manager / AI)
        // ──────────────────────────────────────────────

        public void SetPlayerControlled(bool controlled)
        {
            playerControlled = controlled;
            if (controlled)
            {
                controls.Enable();
            }
            else
            {
                controls.Disable();
                ClearPassTarget();
                // Clear any held inputs so the controller coasts to a stop
                movement.SetMoveInput(Vector2.zero);
                movement.SetSprinting(false);
            }
        }
    }
}
