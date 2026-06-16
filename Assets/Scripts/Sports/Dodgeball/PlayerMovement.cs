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
        [Tooltip("Hang-time multiplier for ATTACK jumps (jump / run-jump throws): extends the airtime so the spike floats longer. Defensive evade hops stay at 1.")]
        [SerializeField] private float attackJumpHangMul = 1.4f;
        [Tooltip("Brief 'gather your feet' pause (s) after landing during which movement input is ignored. Prevents instant re-aim mid-stride; lets natural damping bleed off the jump's lateral momentum.")]
        public float jumpRecoverDuration = 0.15f;

        [Header("Crow hop (skill-timed running attack)")]
        [Tooltip("Length (s) of the crow-hop skip-step. The player keeps running through it — it's a hop, not a full jump.")]
        [SerializeField] private float crowHopDuration = 0.4f;
        [Tooltip("Peak height (u) of the skip — small, well below a jump.")]
        [SerializeField] private float crowHopHeight = 0.45f;
        [Tooltip("Normalized time (0..1) of the 'plant' — the moment to release the throw for the timing bonus.")]
        [Range(0.3f, 1f)] [SerializeField] private float crowHopPlantT = 0.72f;
        [Tooltip("Half-width (s) of the timing window around the plant. A throw released within this of the plant earns up to the full crow-hop bonus; outside, none.")]
        [SerializeField] private float crowHopWindow = 0.12f;

        [Header("Pivot delay (sharp direction change)")]
        [Tooltip("When the input direction differs from current velocity by more than 90° AND the player is moving at >pivotMinSpeed, this delay (s) kicks in before the new direction is accepted. During the pivot, velocity actively tapers to zero (linear) — the player visibly brakes, then commits to the new direction. Scales down with GeneralAttributes.ChangeOfDirection01 (faster pivot for higher-stat players, with a floor at 0.4× the base).")]
        public float pivotDuration = 0.18f;
        [Tooltip("Minimum current lateral speed (u/s) for the pivot delay to trigger. Below this, players can turn freely at walking pace.")]
        public float pivotMinSpeed = 4.5f;

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
        [Tooltip("Catch settle (frozen — can't move or act) after an EASY catch: high catch chance, a routine grab loaded for a fast counter.")]
        [SerializeField] private float catchRecoverMin = 0.3f;
        [Tooltip("Catch settle after a HARD catch (barely made it): securing a tough grab takes longer. Difficulty = 1 - catch chance, so it already accounts for the catcher's skill.")]
        [SerializeField] private float catchRecoverMax = 0.9f;
        [Tooltip("Extra window AFTER the freeze during which a throw is still 'unsettled' (weaker / wilder), fading to full as the ball is secured. Scales longer with catch difficulty. A rushed counter off a fresh catch pays for it.")]
        [SerializeField] private float catchSettleDuration = 0.6f;
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
        private float currentJumpDurationMul = 1f;   // 1 normal hop, >1 for attack jumps (more hang)
        private float crowHopTimer = -1f;            // >=0 during a crow-hop skip-step
        private float jumpRecoverTimer = -1f;
        private float dampingBeforeJump = -1f;   // sentinel; >=0 means a jump is in progress and we've stashed the rb's linearDamping
        private Vector2 landingVelocity = Vector2.zero;   // captured at land; linearly tapered to zero over jumpRecoverDuration
        private float pivotTimer = -1f;
        private Vector2 pivotStartVelocity = Vector2.zero;   // captured at pivot start; linearly tapered to zero over pivotDuration
        private GeneralAttributes generalCache;
        public bool IsJumpRecovering => jumpRecoverTimer >= 0f;
        public bool IsPivoting => pivotTimer >= 0f;
        private float duckTimer = -1f;
        private float dashTimer = -1f;
        private float dashCooldownTimer = -1f;
        private Vector2 dashDir = Vector2.right;
        private float dashActiveSpeed;
        private float diveTimer = -1f;
        private float recoverTimer = -1f;
        private float catchRecoverTimer = -1f;
        private float catchRecoverDur = 0.5f;   // freeze duration, computed per catch from difficulty
        private float caughtAt = -999f;          // time of last catch (for the throw-settle ramp)
        private float catchDifficulty01;         // 0 easy .. 1 barely made it
        private Vector2 diveDir = Vector2.right;
        private bool facingOverriddenThisFrame;
        private float spriteBaseY;
        private Vector3 spriteBaseScale = Vector3.one;
        private Transform spriteChild; // visual sprite that we'll bob up/down

        public bool IsAirborne => jumpTimer >= 0f;
        /// <summary>Mid crow-hop skip-step (a running attack — the player keeps moving).</summary>
        public bool IsCrowHopping => crowHopTimer >= 0f;
        /// <summary>Absolute time offset (s) from the hop's start to the plant — the AI targets this for its release.</summary>
        public float CrowHopPlantTime => crowHopDuration * crowHopPlantT;
        /// <summary>
        /// How well-timed a throw released RIGHT NOW is vs the crow-hop plant:
        /// 1 = dead on the plant, falling to 0 at the edge of the window, 0 when
        /// not crow-hopping. Drives the power + accuracy bonus.
        /// </summary>
        public float CrowHopTiming01
        {
            get
            {
                if (crowHopTimer < 0f) return 0f;
                float off = Mathf.Abs(crowHopTimer - CrowHopPlantTime);
                return Mathf.Clamp01(1f - off / Mathf.Max(0.01f, crowHopWindow));
            }
        }
        /// <summary>Signed timing vs the plant right now: negative = early, positive = late (s); 0 when not hopping.</summary>
        public float CrowHopSignedOffset => IsCrowHopping ? crowHopTimer - CrowHopPlantTime : 0f;
        public bool IsDucking => duckTimer >= 0f;
        public bool IsDashing => dashTimer >= 0f;
        /// <summary>Lunging for a diving catch (drives velocity, extended catch reach).</summary>
        public bool IsDiving => diveTimer >= 0f;
        /// <summary>Prone after a dive — can't move or act until back on feet.</summary>
        public bool IsRecovering => recoverTimer >= 0f;
        /// <summary>Frozen settle right after catching — can't move or act. Duration scales with catch difficulty.</summary>
        public bool IsCatchRecovering => catchRecoverTimer >= 0f;
        /// <summary>
        /// 0 right after a catch, ramping to 1 as the ball is fully secured (over
        /// the freeze + the extra settle window). A throw fired while this is
        /// below 1 is weaker / wilder — a rushed counter. Harder catches secure
        /// slower. 1 when there's no recent catch.
        /// </summary>
        public float CatchSettle01
        {
            get
            {
                float window = catchRecoverDur + catchSettleDuration * (0.5f + 0.5f * catchDifficulty01);
                return window <= 0.0001f ? 1f : Mathf.Clamp01((Time.time - caughtAt) / window);
            }
        }
        /// <summary>
        /// Begin the post-catch settle (called by Ball on a successful catch).
        /// difficulty01 (0 easy .. 1 barely caught) lengthens both the freeze and
        /// the throw-settle ramp.
        /// </summary>
        public void BeginCatchRecovery(float difficulty01)
        {
            catchDifficulty01 = Mathf.Clamp01(difficulty01);
            catchRecoverDur = Mathf.Lerp(catchRecoverMin, catchRecoverMax, catchDifficulty01);
            catchRecoverTimer = 0f;
            caughtAt = Time.time;
        }
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
        private float EffectiveJumpDuration => jumpDuration * currentJumpDurationMul;
        public float JumpApexTime => EffectiveJumpDuration * 0.5f;

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
            // Pivot delay: while turning sharply at speed, ignore input —
            // damping bleeds off momentum until we're slow enough to commit
            // to the new direction.
            if (IsPivoting) return;
            // Ducking commits to a crouch and the post-catch settle is a brief
            // hold — no movement during either (a ducker shouldn't run at full
            // speed; a fresh catcher shouldn't instantly counter-attack).
            if (IsDucking || IsCatchRecovering) return;

            Vector2 clamped = input.sqrMagnitude > 1f ? input.normalized : input;

            // Sharp-direction-change detection: input flips >90° relative to
            // current velocity AND we're moving at running speed. Starts the
            // pivot delay; ignored on the same frame so damping kicks in.
            Vector2 currentForCheck = rb.linearVelocity;
            if (currentForCheck.sqrMagnitude > pivotMinSpeed * pivotMinSpeed
                && clamped.sqrMagnitude > 0.04f
                && Vector2.Dot(currentForCheck.normalized, clamped.normalized) < 0f)
            {
                pivotTimer = 0f;
                pivotStartVelocity = currentForCheck;   // taper from here to zero across pivotDuration
                return;
            }

            if (!facingOverriddenThisFrame && clamped.sqrMagnitude > 0.04f) Facing = clamped.normalized;
            float speed = (IsRunning ? runSpeed : walkSpeed) * (InDefensiveStance ? stanceSpeedMultiplier : 1f);
            Vector2 target = clamped * speed;
            Vector2 current = rb.linearVelocity;
            IsAccelerating = (target - current).sqrMagnitude > 0.0001f;
            rb.linearVelocity = Vector2.MoveTowards(
                current, target, acceleration * Time.deltaTime);
        }

        // Pivot duration shrinks with the player's ChangeOfDirection rating;
        // floor at 0.4× the base so even elite players have some momentum cost.
        private float EffectivePivotDuration()
        {
            if (generalCache == null) generalCache = GetComponent<GeneralAttributes>();
            if (generalCache == null) return pivotDuration;
            return pivotDuration * Mathf.Lerp(1.0f, 0.4f, generalCache.ChangeOfDirection01);
        }

        /// <summary>Start a crow-hop skip-step (a small hop that does NOT stop the run).
        /// Time a throw to the plant (CrowHopTiming01) for a power + accuracy bonus.</summary>
        public void CrowHop()
        {
            if (!IsAirborne && !IsCrowHopping && !IsDucking && !IsDashing && !IsDiving && !IsRecovering)
                crowHopTimer = 0f;
        }

        public void TryJump(bool attackJump = false)
        {
            if (!IsAirborne && !IsDucking && !IsDashing && !IsDiving && !IsRecovering)
            {
                jumpTimer = 0f;
                currentJumpDurationMul = attackJump ? Mathf.Max(1f, attackJumpHangMul) : 1f;
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
                float t = jumpTimer / EffectiveJumpDuration;
                if (t >= 1f)
                {
                    jumpTimer = -1f;
                    CurrentJumpHeight = 0f;
                    // Restore the rb's normal damping that we zeroed at TryJump.
                    if (dampingBeforeJump >= 0f) { rb.linearDamping = dampingBeforeJump; dampingBeforeJump = -1f; }
                    // Brief landing pause + linear velocity taper. Capture the
                    // velocity now so we can bleed it to zero in-place over the
                    // recovery window — "lands on their feet and slows down"
                    // rather than coasting off-screen on the launch momentum.
                    landingVelocity = rb.linearVelocity;
                    if (jumpRecoverDuration > 0f) jumpRecoverTimer = 0f;
                    else { rb.linearVelocity = Vector2.zero; landingVelocity = Vector2.zero; }
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
                // Linear taper from landingVelocity → 0 over jumpRecoverDuration.
                // Driven directly (overrides damping) so the player decelerates
                // predictably regardless of the rb's damping setting.
                float u = Mathf.Clamp01(jumpRecoverTimer / jumpRecoverDuration);
                rb.linearVelocity = landingVelocity * (1f - u);
                if (jumpRecoverTimer >= jumpRecoverDuration)
                {
                    jumpRecoverTimer = -1f;
                    rb.linearVelocity = Vector2.zero;
                    landingVelocity = Vector2.zero;
                }
            }

            // Crow-hop skip-step: a small visual bob that runs alongside the
            // run (no airborne state, no movement freeze). Only drives the
            // sprite height when not in a full jump.
            if (IsCrowHopping)
            {
                crowHopTimer += Time.deltaTime;
                float ct = crowHopTimer / crowHopDuration;
                if (ct >= 1f) { crowHopTimer = -1f; if (!IsAirborne) CurrentJumpHeight = 0f; }
                else if (!IsAirborne) CurrentJumpHeight = 4f * ct * (1f - ct) * crowHopHeight;
            }

            if (IsPivoting)
            {
                pivotTimer += Time.deltaTime;
                float effDur = EffectivePivotDuration();
                // Active brake: linearly drive velocity from pivotStartVelocity
                // to zero across the pivot window. Player visibly decelerates,
                // then commits to the new direction once input is accepted again.
                float u = effDur > 0f ? Mathf.Clamp01(pivotTimer / effDur) : 1f;
                rb.linearVelocity = pivotStartVelocity * (1f - u);
                if (pivotTimer >= effDur)
                {
                    pivotTimer = -1f;
                    pivotStartVelocity = Vector2.zero;
                }
            }

            if (IsDucking)
            {
                duckTimer += Time.deltaTime;
                if (duckTimer >= duckDuration) duckTimer = -1f;
            }

            if (IsCatchRecovering)
            {
                catchRecoverTimer += Time.deltaTime;
                if (catchRecoverTimer >= catchRecoverDur) catchRecoverTimer = -1f;
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

            // Hard play-area clamp. Runs after every other system has moved
            // the player this frame — catches anything that would otherwise
            // slide off-screen (run-jump momentum, dash overshoot, etc.).
            // Zeroes velocity along the clamped axis so we don't keep slamming.
            var p = transform.position;
            const float maxX = CourtSetup.PlayAreaHalfWidth;
            const float maxY = CourtSetup.PlayAreaHalfHeight;
            bool clampedX = false, clampedY = false;
            if (p.x < -maxX) { p.x = -maxX; clampedX = true; }
            else if (p.x > maxX) { p.x = maxX; clampedX = true; }
            if (p.y < -maxY) { p.y = -maxY; clampedY = true; }
            else if (p.y > maxY) { p.y = maxY; clampedY = true; }
            if (clampedX || clampedY)
            {
                transform.position = p;
                var v = rb.linearVelocity;
                if (clampedX) v.x = 0f;
                if (clampedY) v.y = 0f;
                rb.linearVelocity = v;
            }
        }
    }
}
