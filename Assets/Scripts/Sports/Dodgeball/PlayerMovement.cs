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
        [SerializeField] private float runSpeed = 6f;
        [Tooltip("Linear velocity change rate (units/sec^2) used to ramp toward " +
                 "the target velocity. Higher = snappier, lower = more inertia.")]
        [SerializeField] private float acceleration = 40f;

        [Header("Jump")]
        [SerializeField] private float jumpHeight = 1.5f;  // peak hop height
        [SerializeField] private float jumpDuration = 0.6f;
        [Tooltip("Brief 'gather your feet' pause (s) after landing during which movement input is ignored. Prevents instant re-aim mid-stride; lets natural damping bleed off the jump's lateral momentum.")]
        [SerializeField] private float jumpRecoverDuration = 0.15f;

        [Header("Dash (sidestep evade)")]
        [Tooltip("Speed (u/s) of the evade dash burst.")]
        [SerializeField] private float dashSpeed = 14f;
        [Tooltip("How long the dash burst lasts (seconds). Distance ≈ dashSpeed × this.")]
        [SerializeField] private float dashDuration = 0.15f;
        [Tooltip("Minimum time between dashes (seconds).")]
        [SerializeField] private float dashCooldown = 0.35f;

        [Header("Dive (lunge catch)")]
        [Tooltip("Launch speed (u/s) of a diving lunge; it eases to a stop over the dive so it reads as a real dive, not a constant-speed teleport.")]
        [SerializeField] private float diveSpeed = 8f;
        [Tooltip("How long the dive lunge lasts (seconds). Distance ≈ diveSpeed × this ÷ 2 (it decelerates).")]
        [SerializeField] private float diveDuration = 0.5f;
        [Tooltip("Prone recovery (seconds) after a dive lands, before the player can act again.")]
        [SerializeField] private float diveRecovery = 0.6f;
        [Tooltip("Extra catch radius (u) while diving — arms extended toward the ball.")]
        [SerializeField] private float diveReach = 0.7f;

        [Header("Defensive stance")]
        [Tooltip("Movement speed multiplier while set in a defensive stance (slower, but defensively ready).")]
        [SerializeField] private float stanceSpeedMultiplier = 0.8f;
        [Tooltip("Dash speed multiplier when NOT in stance — a flat-footed sidestep is weaker.")]
        [SerializeField] private float dashOutOfStanceScale = 0.5f;

        [Header("Body / duck")]
        [Tooltip("Height (above feet) of the top of the body while standing — the hit ceiling.")]
        [SerializeField] private float standBodyTop = 1.6f;
        [Tooltip("Body top while ducking — balls above this pass over a ducker.")]
        [SerializeField] private float duckBodyTop = 0.8f;
        [Tooltip("How long a duck holds after the last Duck() call (seconds).")]
        [SerializeField] private float duckDuration = 0.5f;
        [Tooltip("Vertical sprite squash while ducking (visual only).")]
        [SerializeField] private float duckSquash = 0.6f;

        /// <summary>
        /// When true, ApplyMove scales by runSpeed instead of walkSpeed.
        /// The input layer flips this on each frame from D-pad double-tap latch
        /// and/or L2 (Sprint) hold state.
        /// </summary>
        public bool IsRunning { get; set; }

        private Rigidbody2D rb;
        private float jumpTimer = -1f;
        private float jumpRecoverTimer = -1f;
        private float dampingBeforeJump = -1f;   // sentinel; >=0 means a jump is in progress and we've stashed the rb's linearDamping
        public bool IsJumpRecovering => jumpRecoverTimer >= 0f;
        private float duckTimer = -1f;
        private float dashTimer = -1f;
        private float dashCooldownTimer = -1f;
        private Vector2 dashDir = Vector2.right;
        private float dashActiveSpeed;
        private float diveTimer = -1f;
        private float recoverTimer = -1f;
        private Vector2 diveDir = Vector2.right;
        private bool facingOverriddenThisFrame;
        private float spriteBaseY;
        private Vector3 spriteBaseScale = Vector3.one;
        private Transform spriteChild; // visual sprite that we'll bob up/down

        public bool IsAirborne => jumpTimer >= 0f;
        public bool IsDucking => duckTimer >= 0f;
        public bool IsDashing => dashTimer >= 0f;
        /// <summary>Lunging for a diving catch (drives velocity, extended catch reach).</summary>
        public bool IsDiving => diveTimer >= 0f;
        /// <summary>Prone after a dive — can't move or act until back on feet.</summary>
        public bool IsRecovering => recoverTimer >= 0f;
        /// <summary>
        /// Feet on the floor: not mid-jump and not mid-dive (a dive is a low
        /// airborne lunge). Prone recovery counts as grounded — you've landed.
        /// Used to authorize line-crossing: a dive over a boundary is legal as
        /// long as you don't actually touch down out of your zone.
        /// </summary>
        public bool IsGrounded => !IsAirborne && !IsDiving;
        /// <summary>Extra catch radius while diving (arms out); read by Ball.</summary>
        public float DiveReach => diveReach;

        /// <summary>Set/ready defensive posture: slower movement, but full catch/evade (the AI sets this while defending; the human toggles it).</summary>
        public bool InDefensiveStance { get; private set; }
        public void SetStance(bool on) => InDefensiveStance = on;

        /// <summary>Current vertical offset of the jump arc above the player's ground position (0 when grounded).</summary>
        public float CurrentJumpHeight { get; private set; }

        /// <summary>Lower edge of the body above the floor (rises while jumping). Balls below this pass under.</summary>
        public float BodyBottom => CurrentJumpHeight;

        /// <summary>Upper edge of the body above the floor (drops while ducking, rises while jumping). Balls above this pass over.</summary>
        public float BodyTop => (IsDucking ? duckBodyTop : standBodyTop) + CurrentJumpHeight;

        /// <summary>Time from jump start to the apex (peak height).</summary>
        public float JumpApexTime => jumpDuration * 0.5f;

        /// <summary>True on frames where ApplyMove found a non-zero gap between current and target velocity.</summary>
        public bool IsAccelerating { get; private set; }

        /// <summary>Unit vector of the last non-zero movement direction (the way the player faces).</summary>
        public Vector2 Facing { get; private set; } = Vector2.right;

        /// <summary>Current lateral velocity (Rigidbody2D). Used by the visual to point the yellow movement arrow.</summary>
        public Vector2 Velocity => rb != null ? rb.linearVelocity : Vector2.zero;

        /// <summary>Orient the player without moving (e.g. AI facing the ball while set).</summary>
        public void SetFacing(Vector2 dir)
        {
            if (dir.sqrMagnitude > 0.0001f)
            {
                Facing = dir.normalized;
                facingOverriddenThisFrame = true;   // keep ApplyMove from snapping facing back to the move dir
            }
        }

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
                spriteBaseScale = spriteChild.localScale;
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
            if (IsDashing || IsDiving || IsRecovering) return;   // dash/dive drive velocity directly; recovery is prone
            // Airborne preserves its launch velocity — no input mid-flight.
            // Jump-recovery is a brief landing pause so you can't instantly
            // pivot the moment your feet touch down.
            if (IsAirborne || IsJumpRecovering) return;
            Vector2 clamped = input.sqrMagnitude > 1f ? input.normalized : input;
            if (!facingOverriddenThisFrame && clamped.sqrMagnitude > 0.04f) Facing = clamped.normalized;
            float speed = (IsRunning ? runSpeed : walkSpeed) * (InDefensiveStance ? stanceSpeedMultiplier : 1f);
            Vector2 target = clamped * speed;
            Vector2 current = rb.linearVelocity;
            IsAccelerating = (target - current).sqrMagnitude > 0.0001f;
            rb.linearVelocity = Vector2.MoveTowards(
                current, target, acceleration * Time.deltaTime);
        }

        public void TryJump()
        {
            if (!IsAirborne && !IsDucking && !IsDashing && !IsDiving && !IsRecovering)
            {
                jumpTimer = 0f;
                // Zero the rigidbody's damping for the duration of the jump
                // so the launch velocity persists end-to-end. Restored on
                // landing. Sentinel -1 means "no jump in progress."
                dampingBeforeJump = rb.linearDamping;
                rb.linearDamping = 0f;
            }
        }

        /// <summary>Start/refresh a duck. Holds for duckDuration after the last call. Ignored while airborne or dashing.</summary>
        public void Duck()
        {
            if (!IsAirborne && !IsDashing && !IsDiving && !IsRecovering) duckTimer = 0f;
        }

        /// <summary>
        /// Start a quick lateral dash in <paramref name="dir"/> (the evade
        /// sidestep). Ignored while airborne, ducking, already dashing, or on
        /// cooldown. Drives velocity directly for dashDuration, then cools down.
        /// </summary>
        public void Dash(Vector2 dir)
        {
            if (IsAirborne || IsDucking || IsDashing || IsDiving || IsRecovering || dashCooldownTimer >= 0f) return;
            if (dir.sqrMagnitude < 0.0001f) return;
            dashDir = dir.normalized;
            dashActiveSpeed = dashSpeed * (InDefensiveStance ? 1f : dashOutOfStanceScale);   // flat-footed dodge is weaker
            Facing = dashDir;
            dashTimer = 0f;
        }

        /// <summary>
        /// Lunge toward <paramref name="dir"/> for a diving catch — drives velocity
        /// for diveDuration, then a prone recovery (diveRecovery) before the player
        /// can act again. The catch reach is extended (DiveReach) while diving.
        /// Ignored mid jump/duck/dash/dive/recovery.
        /// </summary>
        public void Dive(Vector2 dir)
        {
            if (IsAirborne || IsDucking || IsDashing || IsDiving || IsRecovering) return;
            if (dir.sqrMagnitude < 0.0001f) return;
            diveDir = dir.normalized;
            Facing = diveDir;
            diveTimer = 0f;
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
                    // Restore the rb's normal damping that we zeroed at TryJump.
                    if (dampingBeforeJump >= 0f) { rb.linearDamping = dampingBeforeJump; dampingBeforeJump = -1f; }
                    // Brief landing pause before input is accepted again.
                    if (jumpRecoverDuration > 0f) jumpRecoverTimer = 0f;
                    OnLanded?.Invoke(this);
                }
                else
                {
                    // Parabolic arc: 4*t*(1-t) peaks at t=0.5 with value 1.
                    CurrentJumpHeight = 4f * t * (1f - t) * jumpHeight;
                }
            }
            else if (IsJumpRecovering)
            {
                jumpRecoverTimer += Time.deltaTime;
                if (jumpRecoverTimer >= jumpRecoverDuration) jumpRecoverTimer = -1f;
            }

            if (IsDucking)
            {
                duckTimer += Time.deltaTime;
                if (duckTimer >= duckDuration) duckTimer = -1f;
            }

            if (IsDashing)
            {
                dashTimer += Time.deltaTime;
                if (dashTimer >= dashDuration)
                {
                    dashTimer = -1f;
                    dashCooldownTimer = 0f;   // begin cooldown
                }
                else
                {
                    rb.linearVelocity = dashDir * dashActiveSpeed;
                }
            }
            else if (dashCooldownTimer >= 0f)
            {
                dashCooldownTimer += Time.deltaTime;
                if (dashCooldownTimer >= dashCooldown) dashCooldownTimer = -1f;
            }

            if (IsDiving)
            {
                diveTimer += Time.deltaTime;
                if (diveTimer >= diveDuration) { diveTimer = -1f; recoverTimer = 0f; }  // landed → prone recovery
                else
                {
                    // Launch fast, ease to a stop — a real lunge, not a flat-speed lurch.
                    float k = 1f - diveTimer / diveDuration;
                    rb.linearVelocity = diveDir * (diveSpeed * k);
                }
            }
            else if (IsRecovering)
            {
                rb.linearVelocity = Vector2.zero;   // prone — no movement until back on feet
                recoverTimer += Time.deltaTime;
                if (recoverTimer >= diveRecovery) recoverTimer = -1f;
            }

            // Sprite: bob up with the jump arc, squash down while ducking.
            if (spriteChild != null)
            {
                spriteChild.localPosition = new Vector3(0f, spriteBaseY + CurrentJumpHeight, 0f);
                float yScale = IsDucking ? duckSquash : 1f;
                spriteChild.localScale = new Vector3(
                    spriteBaseScale.x, spriteBaseScale.y * yScale, spriteBaseScale.z);
            }
        }

        private void LateUpdate()
        {
            // Cleared after every controller's Update has run, so next frame
            // ApplyMove resumes steering-based facing unless SetFacing is called again.
            facingOverriddenThisFrame = false;
        }
    }
}
