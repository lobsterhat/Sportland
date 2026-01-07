using UnityEngine;

namespace Sportland.Sports.Basketball.Gameplay
{
    public class Backboard : MonoBehaviour
    {
        [Header("Position")]
        [SerializeField] private Vector2 courtPosition;

        [Header("Collision Zone")]
        [SerializeField] private float minHeight = 2.5f;  // Bottom of backboard (at rim level)
        [SerializeField] private float maxHeight = 3.8f;  // Top of backboard
        [SerializeField] private float width = 1.2f;      // Width of backboard

        [Header("Physics")]
        [SerializeField] private float restitution = 0.7f;  // Bounce dampening (0-1)

        [Header("Rendering")]
        [SerializeField] private float heightVisualScale = 1.0f;  // Match ball's visual scale

        private Ball ball;
        private Vector2 previousBallCourtPosition;

        private void Start()
        {
            ball = FindAnyObjectByType<Ball>();

            // Set court position from transform if not manually set
            if (courtPosition == Vector2.zero)
            {
                courtPosition = new Vector2(transform.position.x, transform.position.y);
            }
        }

        private void Update()
        {
            if (ball == null || ball.isHeld)
            {
                previousBallCourtPosition = Vector2.zero;
                return;
            }

            CheckBallCollision();
            previousBallCourtPosition = ball.courtPosition;
        }

        private void CheckBallCollision()
        {
            // Skip first frame when we don't have a previous position
            if (previousBallCourtPosition == Vector2.zero)
            {
                previousBallCourtPosition = ball.courtPosition;
                return;
            }

            // Check if ball crossed the backboard plane in EITHER direction
            bool crossedFromFront = previousBallCourtPosition.y < courtPosition.y && ball.courtPosition.y >= courtPosition.y;
            bool crossedFromBehind = previousBallCourtPosition.y > courtPosition.y && ball.courtPosition.y <= courtPosition.y;
            bool crossedBackboard = crossedFromFront || crossedFromBehind;

            if (!crossedBackboard) return;

            // Check if ball is within backboard boundaries
            float halfWidth = width / 2f;
            bool withinWidth = ball.courtPosition.x >= (courtPosition.x - halfWidth) &&
                              ball.courtPosition.x <= (courtPosition.x + halfWidth);
            bool withinHeight = ball.height >= minHeight && ball.height <= maxHeight;

            if (withinWidth && withinHeight)
            {
                // Ball hit backboard! Bounce it back
                Debug.Log($"BACKBOARD HIT at height {ball.height:F2}!");

                // Snap ball to backboard surface
                Vector2 pos = ball.courtPosition;
                pos.y = courtPosition.y;
                ball.courtPosition = pos;

                // Reflect Y velocity (bounce in opposite direction)
                Vector2 vel = ball.courtVelocity;
                vel.y = -vel.y * restitution;
                vel.x *= 0.95f;  // Dampen X velocity slightly
                ball.courtVelocity = vel;

                // Reduce vertical velocity slightly from impact
                ball.verticalVelocity *= 0.9f;
            }
        }

        private void OnDrawGizmos()
        {
            // Use courtPosition if set, otherwise use transform position
            Vector2 pos = courtPosition != Vector2.zero ? courtPosition : new Vector2(transform.position.x, transform.position.y);
            float halfWidth = width / 2f;

            // Draw backboard collision zone
            Gizmos.color = Color.cyan;

            // Calculate corner points using same coordinate system as ball rendering
            // World Y = courtY + (height * heightVisualScale)
            float bottomWorldY = pos.y + (minHeight * heightVisualScale);
            float topWorldY = pos.y + (maxHeight * heightVisualScale);

            Vector3 bottomLeft = new Vector3(pos.x - halfWidth, bottomWorldY, 0);
            Vector3 bottomRight = new Vector3(pos.x + halfWidth, bottomWorldY, 0);
            Vector3 topLeft = new Vector3(pos.x - halfWidth, topWorldY, 0);
            Vector3 topRight = new Vector3(pos.x + halfWidth, topWorldY, 0);

            // Draw rectangle edges
            Gizmos.DrawLine(bottomLeft, bottomRight);  // Bottom
            Gizmos.DrawLine(topLeft, topRight);        // Top
            Gizmos.DrawLine(bottomLeft, topLeft);      // Left
            Gizmos.DrawLine(bottomRight, topRight);    // Right

            // Draw diagonals for better visibility
            Gizmos.color = new Color(0, 1, 1, 0.3f);  // Semi-transparent cyan
            Gizmos.DrawLine(bottomLeft, topRight);
            Gizmos.DrawLine(bottomRight, topLeft);
        }
    }
}
