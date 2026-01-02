using UnityEngine;

namespace Sportland.Basketball
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
                    courtPosition = player.courtPosition;
                    height = 1.0f;
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
                shadowSprite.transform.position = new Vector3(
                    courtPosition.x,
                    courtPosition.y + (height * heightVisualScale),
                    0
                );

                float shadowScale = Mathf.Lerp(0.3f, 0.15f, height / 5f);
                shadowSprite.transform.localScale = Vector3.one * shadowScale;
            }

            if (ballSprite != null)
            {
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
    }
}