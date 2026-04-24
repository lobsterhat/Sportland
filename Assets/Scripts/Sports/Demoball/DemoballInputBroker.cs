using UnityEngine;

namespace Sportland.Sports.Demoball
{
    /// <summary>
    /// Routes keyboard / gamepad input to DemoballMovementController using
    /// Unity's new Input System (see DemoballInputActions for bindings).
    ///
    /// Controls:
    ///   Left Stick / WASD / Arrows  — Move
    ///   L3 click / Left Shift       — Sprint (L3 = toggle, Shift = hold)
    ///   Circle / E                  — Pick up nearest loose ball, or pass if carrying
    ///   R1     / Q                  — Touch-down score (in scoring ring while carrying)
    ///   Square / T                  — Tackle nearest ball-carrier (Defenders only)
    ///
    /// Set playerControlled = false (or swap at runtime) to hand off to AI.
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

        // ──────────────────────────────────────────────
        //  RUNTIME
        // ──────────────────────────────────────────────

        private DemoballMovementController movement;
        private DemoballInputActions controls;
        private bool sprintToggleState;

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
            sprintToggleState = false;
        }

        private void Update()
        {
            if (!playerControlled) return;

            // ── Locomotion ──
            Vector2 moveInput = controls.Move.ReadValue<Vector2>();
            if (moveInput.sqrMagnitude > 1f) moveInput.Normalize();
            movement.SetMoveInput(moveInput);

            // ── Sprint ──
            if (controls.SprintToggle.WasPressedThisFrame())
                sprintToggleState = !sprintToggleState;
            movement.SetSprinting(controls.SprintHold.IsPressed() || sprintToggleState);

            // ── Action: Circle / E — pickup or pass ──
            if (controls.Action.WasPressedThisFrame())
            {
                if (movement.IsCarryingBall)
                    movement.TryPass();
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
                sprintToggleState = false;
                // Clear any held inputs so the controller coasts to a stop
                movement.SetMoveInput(Vector2.zero);
                movement.SetSprinting(false);
            }
        }
    }
}
