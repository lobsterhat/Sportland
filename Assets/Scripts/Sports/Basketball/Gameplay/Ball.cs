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
        public float radius = 0.12f; // Basketball radius in units (~4.7 inches)

        [Header("State")]
        public bool isHeld = true;
        public Transform holder;
        public float catchSmoothTime = 0.1f; // Time to smooth ball to holder position
        public float heldBallHeight = 1.2f; // Height of ball when held (chest level)
        private float catchTransitionTimer = 0f;

        private void Awake()
        {
            if (courtPosition == Vector2.zero)
            {
                courtPosition = new Vector2(transform.position.x, transform.position.y);
            }
        }

        private void Update()
        {
            if (isHeld && holder != null)
            {
                if (holder.TryGetComponent<BasketballPlayer>(out var player))
                {
                    // Smooth transition when first caught
                    if (catchTransitionTimer < catchSmoothTime)
                    {
                        catchTransitionTimer += Time.deltaTime;
                        float t = catchTransitionTimer / catchSmoothTime;

                        // Smoothly move ball to player position
                        courtPosition = Vector2.Lerp(courtPosition, player.courtPosition, t);
                        height = Mathf.Lerp(height, heldBallHeight, t);
                    }
                    else
                    {
                        // After smooth transition, snap to player position
                        courtPosition = player.courtPosition;
                        height = heldBallHeight;
                    }

                    verticalVelocity = 0f;  // Reset velocity when held
                    courtVelocity = Vector2.zero;  // Reset court velocity too
                }
            }
            else
            {
                // Ball in flight
                courtPosition += courtVelocity * Time.deltaTime;

                verticalVelocity += gravity * Time.deltaTime;
                height += verticalVelocity * Time.deltaTime;

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
            catchTransitionTimer = 0f; // Reset transition timer for smooth catch
        }

        public void SetHolderImmediate(Transform newHolder)
        {
            isHeld = true;
            holder = newHolder;
            courtVelocity = Vector2.zero;
            verticalVelocity = 0f;
            catchTransitionTimer = catchSmoothTime; // Skip smooth transition, snap immediately

            // Immediately set position
            if (newHolder.TryGetComponent<BasketballPlayer>(out var player))
            {
                courtPosition = player.courtPosition;
                height = heldBallHeight;
            }
        }

        public void CaptureAtHoop(Vector2 hoopCourtPosition, float rimHeight)
        {
            // Ball has gone through hoop - let it continue naturally
            // Don't snap position or zero velocities - just ensure it's falling

            // If ball isn't falling yet, give it a slight downward push
            if (verticalVelocity > -1f)
            {
                verticalVelocity = -1f;
            }

            // Let horizontal velocity continue naturally - ball will drift as it falls
            // This creates a more natural "dropping through net" effect

            //Debug.Log("Ball passed through hoop, continuing to fall naturally");
        }
    }
}