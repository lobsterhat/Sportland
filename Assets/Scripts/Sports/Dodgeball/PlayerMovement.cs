using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Side-view sprite movement on a top-down court.
    /// X/Y are court coordinates; Z is treated as "vertical hop" for the jump.
    ///
    /// The jump is a pure presentation/state thing — it does NOT affect zone
    /// containment (you're still considered to be at your X,Y position when
    /// airborne). Its role is to authorize the "throw across a line" exception.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerMovement : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float moveSpeed = 6f;     // units/sec
        [SerializeField] private float jumpHeight = 1.5f;  // peak hop height
        [SerializeField] private float jumpDuration = 0.6f;

        private Rigidbody2D rb;
        private float jumpTimer = -1f;
        private float spriteBaseY;
        private Transform spriteChild; // visual sprite that we'll bob up/down

        public bool IsAirborne => jumpTimer >= 0f;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            rb.gravityScale = 0f;            // top-down — no gravity on world Y
            rb.freezeRotation = true;

            // Convention: child[0] is the sprite renderer we offset for the hop.
            if (transform.childCount > 0)
            {
                spriteChild = transform.GetChild(0);
                spriteBaseY = spriteChild.localPosition.y;
            }
        }

        /// <summary>
        /// Move the player by a normalized input vector. Call from your input handler.
        /// </summary>
        public void ApplyMove(Vector2 input)
        {
            Vector2 vel = input.normalized * moveSpeed;
            rb.linearVelocity = vel;
        }

        public void TryJump()
        {
            if (!IsAirborne) jumpTimer = 0f;
        }

        private void Update()
        {
            if (IsAirborne)
            {
                jumpTimer += Time.deltaTime;
                float t = jumpTimer / jumpDuration;
                if (t >= 1f)
                {
                    jumpTimer = -1f;
                    if (spriteChild != null)
                        spriteChild.localPosition = new Vector3(0f, spriteBaseY, 0f);
                }
                else if (spriteChild != null)
                {
                    // Parabolic arc: 4*t*(1-t) peaks at t=0.5 with value 1.
                    float h = 4f * t * (1f - t) * jumpHeight;
                    spriteChild.localPosition = new Vector3(0f, spriteBaseY + h, 0f);
                }
            }
        }
    }
}
