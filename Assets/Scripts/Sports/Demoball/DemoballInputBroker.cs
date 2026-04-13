using UnityEngine;

namespace Sportland.Sports.Demoball
{
    /// <summary>
    /// Routes keyboard / gamepad input to DemoballMovementController.
    ///
    /// Controls (keyboard):
    ///   WASD / Arrow Keys   — Move
    ///   Left Shift          — Sprint (hold)
    ///   E                   — Pick up nearest loose ball (if not carrying)
    ///                         Pass to nearest teammate (if carrying)
    ///   Q                   — Touch-down score (must be in scoring ring while carrying)
    ///   T                   — Tackle nearest ball-carrier (Defenders only)
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

        // ──────────────────────────────────────────────
        //  LIFECYCLE
        // ──────────────────────────────────────────────

        private void Awake()
        {
            movement = GetComponent<DemoballMovementController>();
        }

        private void Update()
        {
            if (!playerControlled) return;

            // ── Locomotion ──
            movement.SetMoveInput(new Vector2(
                Input.GetAxisRaw("Horizontal"),
                Input.GetAxisRaw("Vertical")));

            movement.SetSprinting(Input.GetKey(KeyCode.LeftShift));

            // ── Ball interaction: E ──
            if (Input.GetKeyDown(KeyCode.E))
            {
                if (movement.IsCarryingBall)
                    movement.TryPass();
                else
                    TryPickUpNearest();
            }

            // ── Touch-down: Q ──
            if (Input.GetKeyDown(KeyCode.Q))
                movement.TryTouchDown();

            // ── Tackle: T ──
            if (Input.GetKeyDown(KeyCode.T))
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
            if (!controlled)
            {
                // Clear any held inputs so the controller coasts to a stop
                movement.SetMoveInput(Vector2.zero);
                movement.SetSprinting(false);
            }
        }
    }
}
