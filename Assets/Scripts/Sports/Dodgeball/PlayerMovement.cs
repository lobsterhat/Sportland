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
        [Header("Visual layout")]
        [Tooltip("Distance from the player root down to the visual feet. " +
                 "Roughly -(sprite height / 2) for a center-pivoted sprite.")]
        [SerializeField] private float footOffset = -0.79f;

        /// <summary>Local Y offset from the root down to where the floor visually sits under this player.</summary>
        public float FootOffset => footOffset;

        [Header("Speed")]
        [Tooltip("Top speed (units/sec) when not running.")]
        [SerializeField] private float walkSpeed = 4f;
        [Tooltip("Top speed (units/sec) while running (D-pad double-tap or L2 hold).")]
        [SerializeField] private float runSpeed = 8f;
        [Tooltip("Linear velocity change rate (units/sec^2) used to ramp toward " +
                 "the target velocity. Higher = snappier, lower = more inertia.")]
        [SerializeField] private float acceleration = 40f;

        [Header("Jump")]
        [SerializeField] private float jumpHeight = 1.5f;  // peak hop height
        [SerializeField] private float jumpDuration = 0.6f;

        /// <summary>
        /// When true, ApplyMove scales by runSpeed instead of walkSpeed.
        /// The input layer flips this on each frame from D-pad double-tap latch
        /// and/or L2 (Sprint) hold state.
        /// </summary>
        public bool IsRunning { get; set; }

        private Rigidbody2D rb;
        private float jumpTimer = -1f;
        private float spriteBaseY;
        private Transform spriteChild; // visual sprite that we'll bob up/down

        public bool IsAirborne => jumpTimer >= 0f;

        /// <summary>Current vertical offset of the jump arc above the player's ground position (0 when grounded).</summary>
        public float CurrentJumpHeight { get; private set; }

        /// <summary>True on frames where ApplyMove found a non-zero gap between current and target velocity.</summary>
        public bool IsAccelerating { get; private set; }

        public System.Action<PlayerMovement> OnLanded;

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
        /// Move the player by an analog input vector. Magnitude (0..1) scales
        /// speed; vectors over length 1 (e.g. keyboard diagonals) are clamped.
        /// Current velocity ramps toward the target at the configured
        /// acceleration so changes of direction and start/stop both have weight.
        /// </summary>
        public void ApplyMove(Vector2 input)
        {
            Vector2 clamped = input.sqrMagnitude > 1f ? input.normalized : input;
            float speed = IsRunning ? runSpeed : walkSpeed;
            Vector2 target = clamped * speed;
            Vector2 current = rb.linearVelocity;
            IsAccelerating = (target - current).sqrMagnitude > 0.0001f;
            rb.linearVelocity = Vector2.MoveTowards(
                current, target, acceleration * Time.deltaTime);
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
                    CurrentJumpHeight = 0f;
                    if (spriteChild != null)
                        spriteChild.localPosition = new Vector3(0f, spriteBaseY, 0f);
                    OnLanded?.Invoke(this);
                }
                else
                {
                    // Parabolic arc: 4*t*(1-t) peaks at t=0.5 with value 1.
                    CurrentJumpHeight = 4f * t * (1f - t) * jumpHeight;
                    if (spriteChild != null)
                        spriteChild.localPosition = new Vector3(0f, spriteBaseY + CurrentJumpHeight, 0f);
                }
            }
        }
    }
}
