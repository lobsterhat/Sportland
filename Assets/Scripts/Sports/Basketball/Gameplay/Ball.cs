using UnityEngine;

namespace Sportland.Sports.Basketball.Gameplay
{
    public class Ball : MonoBehaviour
    {
        [Header("Court Position")]
        public Vector2 courtPosition;
        public float height = 0f;

        [Header("Rendering")]
        public SpriteRenderer ballSprite;
        public SpriteRenderer shadowSprite;
        public float heightVisualScale = 1.0f;

        [Header("Physics")]
        public Vector2 courtVelocity;
        public float verticalVelocity;
        public float gravity = -9.8f;

        [Header("State")]
        public bool isHeld = true;
        public Transform holder;

        [Header("Backboard Collision")]
        public float backboardY = 0.5f;  // How far behind hoop (positive = away from shooter)
        public float backboardMinHeight = 2.5f;  // Bottom of backboard (at rim level)
        public float backboardMaxHeight = 3.8f;  // Top of backboard
        public float backboardWidth = 1.2f;  // Width of backboard
        public float backboardRestitution = 0.7f;  // Bounce dampening (0-1)

        private Hoop targetHoop;  // Reference to the hoop we're shooting at


        private void Awake()
        {
            if (courtPosition == Vector2.zero)
            {
                courtPosition = new Vector2(transform.position.x, transform.position.y);
            }

            // Find the hoop for backboard position reference
            targetHoop = FindAnyObjectByType<Hoop>();
        }

        private void Update()
        {
            if (isHeld && holder != null)
            {
                if (holder.TryGetComponent<BasketballPlayer>(out var player))
                {
                    courtPosition = player.courtPosition;
                    height = 1.0f;
                    verticalVelocity = 0f;  // Reset velocity when held
                    courtVelocity = Vector2.zero;  // Reset court velocity too
                }
            }
            else
            {
                // Ball in flight
                Vector2 previousPosition = courtPosition;
                float previousHeight = height;

                courtPosition += courtVelocity * Time.deltaTime;

                verticalVelocity += gravity * Time.deltaTime;
                height += verticalVelocity * Time.deltaTime;

                // Check backboard collision
                CheckBackboardCollision(previousPosition, previousHeight);

                // Bounce on ground
                if (height <= 0f)
                {
                    height = 0f;
                    verticalVelocity *= -0.5f;
                    courtVelocity *= 0.8f;

                    if (Mathf.Abs(verticalVelocity) < 0.5f)
                    {
                        verticalVelocity = 0f;
                        courtVelocity = Vector2.zero;
                    }
                }
            }

            UpdateRendering();
        }

        private void UpdateRendering()
        {
            if (shadowSprite != null)
            {
                // Shadow stays at ground level
                shadowSprite.transform.position = new Vector3(
                    courtPosition.x,
                    courtPosition.y,  // No height offset - shadow on ground
                    0
                );

                // Shadow gets smaller as ball goes higher
                float shadowScale = Mathf.Lerp(0.3f, 0.15f, height / 5f);
                shadowSprite.transform.localScale = Vector3.one * shadowScale;
            }

            if (ballSprite != null)
            {
                // Ball sprite elevated by height
                ballSprite.transform.position = new Vector3(
                    courtPosition.x,
                    courtPosition.y + (height * heightVisualScale),
                    0
                );

                ballSprite.sortingOrder = 1000 - (int)(courtPosition.y * 100);
            }
        }

        private void CheckBackboardCollision(Vector2 previousPosition, float previousHeight)
        {
            if (targetHoop == null) return;

            // Calculate backboard Y position (behind the hoop)
            float backboardYPos = targetHoop.CourtPosition.y + backboardY;

            // Check if ball crossed the backboard plane in EITHER direction
            bool crossedFromFront = previousPosition.y < backboardYPos && courtPosition.y >= backboardYPos;
            bool crossedFromBehind = previousPosition.y > backboardYPos && courtPosition.y <= backboardYPos;
            bool crossedBackboard = crossedFromFront || crossedFromBehind;

            if (!crossedBackboard) return;

            // Check if ball is within backboard boundaries
            float hoopX = targetHoop.CourtPosition.x;
            float halfWidth = backboardWidth / 2f;

            bool withinWidth = courtPosition.x >= (hoopX - halfWidth) && courtPosition.x <= (hoopX + halfWidth);
            bool withinHeight = height >= backboardMinHeight && height <= backboardMaxHeight;

            if (withinWidth && withinHeight)
            {
                // Ball hit backboard! Reflect it back
                Debug.Log($"BACKBOARD HIT at height {height:F2}!");

                // Snap ball to backboard surface
                courtPosition.y = backboardYPos;

                // Reflect Y velocity (bounce in opposite direction)
                courtVelocity.y = -courtVelocity.y * backboardRestitution;

                // Dampen X velocity slightly
                courtVelocity.x *= 0.95f;

                // Reduce vertical velocity slightly from impact
                verticalVelocity *= 0.9f;
            }
        }

        public void Launch(Vector2 startCourtPos, float startHeight, Vector2 courtVel, float vertVel)
        {
            isHeld = false;
            holder = null;

            courtPosition = startCourtPos;
            height = startHeight;
            courtVelocity = courtVel;
            verticalVelocity = vertVel;

           // Debug.Log($"Ball launched from {courtPosition} at height {height}");
        }

        public void SetHolder(Transform newHolder)
        {
            isHeld = true;
            holder = newHolder;
            courtVelocity = Vector2.zero;
            verticalVelocity = 0f;
            height = 1.0f; // Reset height when caught
        }

        public void CaptureAtHoop(Vector2 hoopCourtPosition, float rimHeight)
        {
            // Ball has gone through hoop - now drop straight down

            // Snap to hoop position
            courtPosition = hoopCourtPosition;
            courtVelocity = Vector2.zero;

            // Keep falling (or increase downward speed)
            // Ball is already falling, so we can just let gravity continue
            // Or force it to fall faster:
            //if (verticalVelocity > -2f)
            //{
            //    verticalVelocity = -2f; // Ensure it's falling at least this fast
            //}

            verticalVelocity = 0;

            //Debug.Log("Ball captured at hoop, dropping straight down");
            //Debug.Log($"Ball rendering at height={height}, courtPos={courtPosition}");
        }

        private void OnDrawGizmos()
        {
            // Find hoop if not set (for edit mode)
            Hoop hoop = targetHoop;
            if (hoop == null)
            {
                hoop = FindAnyObjectByType<Hoop>();
            }

            if (hoop == null) return;

            // Draw backboard collision zone
            Gizmos.color = Color.cyan;

            float backboardCourtY = hoop.CourtPosition.y + backboardY;
            float hoopX = hoop.CourtPosition.x;
            float halfWidth = backboardWidth / 2f;

            // Calculate corner points using same coordinate system as ball rendering
            // World Y = courtY + (height * heightVisualScale)
            float bottomWorldY = backboardCourtY + (backboardMinHeight * heightVisualScale);
            float topWorldY = backboardCourtY + (backboardMaxHeight * heightVisualScale);

            Vector3 bottomLeft = new Vector3(hoopX - halfWidth, bottomWorldY, 0);
            Vector3 bottomRight = new Vector3(hoopX + halfWidth, bottomWorldY, 0);
            Vector3 topLeft = new Vector3(hoopX - halfWidth, topWorldY, 0);
            Vector3 topRight = new Vector3(hoopX + halfWidth, topWorldY, 0);

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