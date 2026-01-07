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

            // Check if ball crossed the backboard plane (X axis) in EITHER direction
            bool crossedFromFront = previousBallCourtPosition.x < courtPosition.x && ball.courtPosition.x >= courtPosition.x;
            bool crossedFromBehind = previousBallCourtPosition.x > courtPosition.x && ball.courtPosition.x <= courtPosition.x;
            bool crossedBackboard = crossedFromFront || crossedFromBehind;

            if (crossedBackboard)
            {
                Debug.Log($"Ball crossed backboard plane! Ball X: {ball.courtPosition.x:F2}, Backboard X: {courtPosition.x:F2}, Ball Y: {ball.courtPosition.y:F2}, Ball Height: {ball.height:F2}");
            }

            if (!crossedBackboard) return;

            // Check if ball is within backboard boundaries
            float halfWidth = width / 2f;
            bool withinWidth = ball.courtPosition.y >= (courtPosition.y - halfWidth) &&
                              ball.courtPosition.y <= (courtPosition.y + halfWidth);
            bool withinHeight = ball.height >= minHeight && ball.height <= maxHeight;

            Debug.Log($"Backboard bounds check - Y range: [{courtPosition.y - halfWidth:F2}, {courtPosition.y + halfWidth:F2}], Height range: [{minHeight:F2}, {maxHeight:F2}]");
            Debug.Log($"Within width: {withinWidth}, Within height: {withinHeight}");

            if (withinWidth && withinHeight)
            {
                // Ball hit backboard! Bounce it back
                Debug.Log($"BACKBOARD HIT at height {ball.height:F2}!");
                Debug.Log($"Before collision - Ball velocity: ({ball.courtVelocity.x:F2}, {ball.courtVelocity.y:F2}), Vertical: {ball.verticalVelocity:F2}");

                // Snap ball to backboard surface
                Vector2 pos = ball.courtPosition;
                pos.x = courtPosition.x;
                ball.courtPosition = pos;

                // Reflect X velocity (bounce back toward court)
                Vector2 vel = ball.courtVelocity;
                vel.x = -vel.x * restitution;
                vel.y *= 0.95f;  // Dampen Y velocity slightly
                ball.courtVelocity = vel;

                // Reduce vertical velocity slightly from impact
                ball.verticalVelocity *= 0.9f;

                Debug.Log($"After collision - Ball velocity: ({ball.courtVelocity.x:F2}, {ball.courtVelocity.y:F2}), Vertical: {ball.verticalVelocity:F2}");
            }
        }

        private void OnDrawGizmos()
        {
            // Determine the court position to use
            Vector2 courtPos = courtPosition != Vector2.zero ? courtPosition : new Vector2(transform.position.x, transform.position.y);
            float halfWidth = width / 2f;

            // Draw backboard collision zone
            Gizmos.color = Color.cyan;

            // Get heightVisualScale from ball (if available) to ensure gizmo matches ball rendering
            float heightScale = 1.0f;
            Ball ballRef = ball;
            if (ballRef == null)
            {
                ballRef = FindAnyObjectByType<Ball>();
            }
            if (ballRef != null)
            {
                heightScale = ballRef.heightVisualScale;
            }

            // Calculate visual positions - these are where ball sprites at these heights would render
            // World Y = courtY + (height * heightScale)
            float bottomWorldY = courtPos.y + (minHeight * heightScale);
            float topWorldY = courtPos.y + (maxHeight * heightScale);

            Vector3 bottomLeft = new Vector3(courtPos.x - halfWidth, bottomWorldY, 0);
            Vector3 bottomRight = new Vector3(courtPos.x + halfWidth, bottomWorldY, 0);
            Vector3 topLeft = new Vector3(courtPos.x - halfWidth, topWorldY, 0);
            Vector3 topRight = new Vector3(courtPos.x + halfWidth, topWorldY, 0);

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
