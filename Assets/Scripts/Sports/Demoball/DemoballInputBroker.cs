using System.Collections.Generic;
using UnityEngine;

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

        // ──────────────────────────────────────────────
        //  LIFECYCLE
        // ──────────────────────────────────────────────

        private void Awake()
        {
            movement = GetComponent<DemoballMovementController>();
            controls = new DemoballInputActions();
        }

        private void OnEnable()
        {
            if (playerControlled) controls.Enable();
        }

        private void OnDisable()
        {
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

            // ── Action: Circle / E — pickup or pass ──
            if (controls.Action.WasPressedThisFrame())
            {
                if (movement.IsCarryingBall)
                    AttemptPass();
                else
                    TryPickUpNearest();
            }

            // ── Touch-down: R1 / Q ──
            if (controls.TouchDown.WasPressedThisFrame())
                movement.TryTouchDown();

            // ── Tackle: Square / T ──
            if (controls.Tackle.WasPressedThisFrame())
                movement.TryTackle();
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

        private void AttemptPass()
        {
            // Snapshot target then clear visual immediately — TryPass may swap
            // control, after which we'd otherwise lose the chance to clear it.
            var target = currentPassTarget;
            ClearPassTarget();

            bool passed = target != null
                ? movement.TryPass(target)
                : movement.TryPass();          // fallback: nearest teammate

            if (!passed) return;

            // Hand control to whichever broker now holds the ball.
            DemoballInputBroker receiver =
                  (target != null && target.IsCarryingBall) ? target.GetComponent<DemoballInputBroker>()
                : FindCarrierBroker();

            if (receiver != null && receiver != this)
            {
                SetPlayerControlled(false);
                receiver.SetPlayerControlled(true);
            }
        }

        private DemoballInputBroker FindCarrierBroker()
        {
            if (teammates == null) return null;
            foreach (var t in teammates)
            {
                if (t == null || t == movement) continue;
                if (t.IsCarryingBall) return t.GetComponent<DemoballInputBroker>();
            }
            return null;
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
