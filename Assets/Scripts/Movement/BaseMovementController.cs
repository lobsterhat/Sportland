using UnityEngine;
using Sportland.Rendering;
using Sportland.World;

namespace Sportland.Movement
{
    /// <summary>
    /// Universal athlete movement controller. Handles all locomotion, agility,
    /// vertical movement, diving, stamina, and fatigue systems.
    /// 
    /// Sport-specific controllers inherit from this and add their own
    /// states/actions (e.g., TagMovementController adds tagging, lunging).
    /// 
    /// Expects a Rigidbody2D on the same GameObject (set to Dynamic, gravity = 0
    /// for top-down, or adjust for side-view as needed).
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class BaseMovementController : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        //  SERIALIZED FIELDS
        // ──────────────────────────────────────────────

        [Header("Profile")]
        [Tooltip("Assign a MovementProfile ScriptableObject to define this athlete's tuning.")]
        [SerializeField] protected MovementProfile profile;

        [Header("Visuals")]
        [Tooltip("Handles sprite vertical offset for jumps. Optional — assign if using top-down + side-view hybrid.")]
        [SerializeField] private SpriteVerticalOffset spriteOffset;

        // ──────────────────────────────────────────────
        //  MOVEMENT STATE
        // ──────────────────────────────────────────────

        public enum MovementState
        {
            Idle,
            Jogging,
            Sprinting,
            Shuffling,    // defensive stance, locked facing, lateral movement
            Cutting,      // mid-direction-change, speed penalty active
            Airborne,     // jumping
            Diving,       // committed dive, no cancellation
            DiveRecovery, // on the ground after a dive
            LandingRecovery, // brief delay after jump landing
            Stunned       // generic incapacitation (extensible for sports)
        }

        public MovementState CurrentState { get; protected set; } = MovementState.Idle;

        // ──────────────────────────────────────────────
        //  RUNTIME DATA
        // ──────────────────────────────────────────────

        protected Rigidbody2D rb;

        // Locomotion
        protected Vector2 moveInput;         // raw input direction (normalized)
        protected Vector2 facingDirection = Vector2.right;
        protected float currentSpeed;        // current scalar speed
        protected bool isSprinting;

        // Shuffle / Defensive Stance
        protected bool isShuffling;
        protected Vector2 shuffleFacingLock;  // direction locked when entering shuffle
        protected float shuffleBurstTimer;    // time remaining on burst bonus after exiting shuffle
        protected bool shuffleBurstActive;

        // Feint / Lean (driven by triggers while shuffling)
        protected float triggerLeanInput;     // raw input: -1 = left, 0 = center, 1 = right
        protected Vector2 leanDirection;      // current lean direction (world space, normalized)
        protected float leanIntensity;        // 0 = centered, 1 = full lean
        protected float leanCooldownTimer;    // prevents jittery direction changes
        protected bool isLeaning;             // true when actively leaning in a direction

        // Burst window (after releasing shuffle while leaning)
        protected bool burstWindowActive;
        protected float burstWindowTimer;
        protected float burstWindowDuration = 0.3f;

        // Stamina
        public float CurrentStamina { get; protected set; }
        protected float staminaRegenCooldown; // time remaining before regen kicks in

        // State timers
        protected float stateTimer;          // generic timer for timed states
        protected float cutTimer;            // time remaining in cut recovery

        // Vertical (for 2D side-view or hybrid perspective)
        protected float verticalVelocity;
        protected bool isGrounded = true;

        // Dive
        protected Vector2 diveDirection;

        // ── Surface / terrain ──
        // Keyed by the SurfaceZone that registered the surface so reference
        // equality on the definition instance is never needed for removal.
        private System.Collections.Generic.List<(SurfaceZone zone, SurfaceDefinition def)>
            activeSurfaceList = new System.Collections.Generic.List<(SurfaceZone, SurfaceDefinition)>();

        /// <summary>
        /// The highest-priority surface currently under this character.
        /// Null when no surface zones are active (baseline movement applies).
        /// </summary>
        public SurfaceDefinition ActiveSurface { get; private set; }

        // ──────────────────────────────────────────────
        //  EVENTS (for UI, VFX, audio hooks)
        // ──────────────────────────────────────────────

        public System.Action<MovementState, MovementState> OnStateChanged;
        public System.Action<float> OnStaminaChanged; // passes normalized 0-1
        public System.Action OnDiveStarted;
        public System.Action OnJumpStarted;
        public System.Action OnHardCut;
        public System.Action OnShuffleEntered;
        public System.Action OnShuffleExited;
        public System.Action OnShuffleBurst;
        public System.Action<Vector2> OnLeanStarted;   // passes lean direction
        public System.Action OnLeanCentered;            // lean returned to center

        // ──────────────────────────────────────────────
        //  UNITY LIFECYCLE
        // ──────────────────────────────────────────────

        protected virtual void Awake()
        {
            rb = GetComponent<Rigidbody2D>();

            // Auto-find SpriteVerticalOffset if not assigned in Inspector
            if (spriteOffset == null)
                spriteOffset = GetComponent<SpriteVerticalOffset>();

            if (profile == null)
            {
                Debug.LogError($"[BaseMovementController] No MovementProfile assigned on {gameObject.name}!");
            }
        }

        protected virtual void Start()
        {
            CurrentStamina = profile.maxStamina;
        }

        protected virtual void Update()
        {
            UpdateStamina();
            UpdateStateTimers();
        }

        protected virtual void FixedUpdate()
        {
            ApplyMovement();
        }

        // ──────────────────────────────────────────────
        //  INPUT INTERFACE
        //  Call these from your input handler (Player or AI).
        // ──────────────────────────────────────────────

        /// <summary>
        /// Set the desired movement direction. Pass Vector2.zero for no input.
        /// </summary>
        public virtual void SetMoveInput(Vector2 input)
        {
            moveInput = input.sqrMagnitude > 1f ? input.normalized : input;
        }

        /// <summary>
        /// Toggle sprint on/off. Sprint drains stamina and unlocks top speed.
        /// </summary>
        public virtual void SetSprinting(bool sprinting)
        {
            isSprinting = sprinting;
        }

        /// <summary>
        /// Toggle shuffle/defensive stance on/off. Hold to maintain stance.
        /// Locks facing direction, enables lateral movement, grants reactive advantages.
        /// Cannot shuffle while sprinting — shuffle overrides sprint.
        /// </summary>
        public virtual void SetShuffling(bool shuffling)
        {
            if (shuffling && !isShuffling && CanAct() && isGrounded)
            {
                // Enter shuffle
                isShuffling = true;
                shuffleFacingLock = facingDirection;
                SetState(MovementState.Shuffling);
                OnShuffleEntered?.Invoke();
            }
            else if (!shuffling && isShuffling)
            {
                // Exit shuffle
                isShuffling = false;

                // If we were leaning, open burst window
                if (isLeaning)
                {
                    burstWindowActive = true;
                    burstWindowTimer = burstWindowDuration;
                    ClearLean();
                }
                else if (moveInput.sqrMagnitude > 0.01f)
                {
                    // Not leaning but moving — normal burst
                    shuffleBurstActive = true;
                    shuffleBurstTimer = profile.shuffleBurstDuration;
                    OnShuffleBurst?.Invoke();
                }
                else
                {
                    currentSpeed = 0f;
                }

                OnShuffleExited?.Invoke();
            }
        }

        /// <summary>
        /// Set the lean input from triggers. -1 = full lean left, 0 = center, 1 = full lean right.
        /// Only meaningful while in shuffle stance.
        /// </summary>
        public virtual void SetLeanInput(float input)
        {
            triggerLeanInput = Mathf.Clamp(input, -1f, 1f);
        }

        /// <summary>
        /// Attempt to jump. Fails if not grounded, insufficient stamina, or in
        /// a non-cancellable state.
        /// </summary>
        public virtual bool TryJump()
        {
            if (!CanAct()) return false;
            if (!isGrounded) return false;
            if (CurrentStamina < profile.jumpStaminaCost) return false;
            if (ActiveSurface != null && !ActiveSurface.allowJump) return false;

            ConsumeStamina(profile.jumpStaminaCost);
            PerformJump();
            return true;
        }

        /// <summary>
        /// Attempt to dive in the current facing direction.
        /// High commitment — long recovery on failure.
        /// </summary>
        public virtual bool TryDive()
        {
            if (!CanAct()) return false;
            if (!isGrounded) return false;
            if (CurrentStamina < profile.diveStaminaCost) return false;
            if (ActiveSurface != null && !ActiveSurface.allowDive) return false;

            ConsumeStamina(profile.diveStaminaCost);
            PerformDive();
            return true;
        }

        // ──────────────────────────────────────────────
        //  STATE MANAGEMENT
        // ──────────────────────────────────────────────

        protected virtual void SetState(MovementState newState)
        {
            if (CurrentState == newState) return;

            MovementState oldState = CurrentState;
            CurrentState = newState;
            stateTimer = 0f;
            OnStateChanged?.Invoke(oldState, newState);
        }

        /// <summary>
        /// Returns true if the athlete can perform voluntary actions
        /// (not mid-dive, not stunned, not in recovery).
        /// </summary>
        public virtual bool CanAct()
        {
            return CurrentState != MovementState.Diving
                && CurrentState != MovementState.DiveRecovery
                && CurrentState != MovementState.LandingRecovery
                && CurrentState != MovementState.Stunned;
        }

        // ──────────────────────────────────────────────
        //  CORE MOVEMENT
        // ──────────────────────────────────────────────

        protected virtual void ApplyMovement()
        {
            // Update shuffle burst timer
            if (shuffleBurstActive)
            {
                shuffleBurstTimer -= Time.fixedDeltaTime;
                if (shuffleBurstTimer <= 0f)
                {
                    shuffleBurstActive = false;
                }
            }

            switch (CurrentState)
            {
                case MovementState.Diving:
                    ApplyDiveMovement();
                    return; // dive overrides all other movement

                case MovementState.DiveRecovery:
                case MovementState.LandingRecovery:
                case MovementState.Stunned:
                    ApplyDeceleration();
                    rb.linearVelocity = facingDirection * currentSpeed;
                    return; // no voluntary movement during recovery/stun

                case MovementState.Cutting:
                    if (!isInCutRecovery)
                    {
                        // Plant phase — completely stopped, waiting to push off
                        currentSpeed = 0f;
                        rb.linearVelocity = Vector2.zero;
                        return;
                    }
                    // Recovery phase — fall through to normal movement with cut recovery acceleration
                    break;

                case MovementState.Airborne:
                    // Allow horizontal movement while airborne but don't change state
                    break;

                case MovementState.Shuffling:
                    ApplyShuffleMovement();
                    return;
            }

            if (moveInput.sqrMagnitude < 0.01f)
            {
                // No input — decelerate to stop
                ApplyDeceleration();

                if (currentSpeed < 0.1f)
                {
                    currentSpeed = 0f;
                    if (CurrentState != MovementState.Airborne)
                        SetState(MovementState.Idle);
                }
            }
            else
            {
                // Check for hard cut
                float angleDelta = Vector2.Angle(facingDirection, moveInput);
                if (angleDelta >= profile.hardCutAngleThreshold
                    && currentSpeed > GetEffectiveTopSpeed() * 0.3f
                    && CurrentState != MovementState.Airborne)
                {
                    PerformHardCut(angleDelta);
                }

                // Rotate facing toward input — traction scales turn rate
                float effectiveTurnSpeed = profile.turnSpeed
                    * GetFatigueMultiplier(profile.fatigueAgilityPenalty)
                    * (ActiveSurface?.tractionMultiplier ?? 1f);
                facingDirection = RotateToward(facingDirection, moveInput, effectiveTurnSpeed * Time.fixedDeltaTime);

                // Sprint is blocked on surfaces that disallow it (preserve the input field)
                bool effectiveSprint = isSprinting
                    && (ActiveSurface == null || ActiveSurface.allowSprint);

                // Accelerate
                float targetSpeed = effectiveSprint ? GetEffectiveTopSpeed() : GetEffectiveJogSpeed();

                float effectiveAcceleration = (CurrentState == MovementState.Cutting)
                    ? profile.cutRecoveryAcceleration
                    : profile.acceleration;
                effectiveAcceleration *= GetFatigueMultiplier(profile.fatigueAccelerationPenalty);
                effectiveAcceleration *= ActiveSurface?.accelerationMultiplier ?? 1f;

                // Shuffle burst bonus — brief acceleration boost for explosive first step
                if (shuffleBurstActive)
                {
                    effectiveAcceleration *= profile.shuffleBurstMultiplier;
                }

                currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, effectiveAcceleration * Time.fixedDeltaTime);

                // Update state (don't override Airborne or Shuffling)
                if (CurrentState != MovementState.Airborne)
                {
                    if (effectiveSprint && currentSpeed > GetEffectiveJogSpeed())
                        SetState(MovementState.Sprinting);
                    else if (currentSpeed > 0.1f)
                        SetState(MovementState.Jogging);
                }
            }

            rb.linearVelocity = facingDirection * currentSpeed;
        }

        protected virtual void ApplyDeceleration()
        {
            float decel = profile.deceleration * (ActiveSurface?.decelerationMultiplier ?? 1f);
            currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, decel * Time.fixedDeltaTime);
        }

        // ──────────────────────────────────────────────
        //  SHUFFLE / DEFENSIVE STANCE
        // ──────────────────────────────────────────────

        /// <summary>
        /// Movement while in shuffle stance. Facing is locked,
        /// movement is lateral relative to facing direction.
        /// Lateral stick input creates leans (feints) instead of full movement.
        /// Forward/backward input creates shuffle movement.
        /// </summary>
        protected virtual void ApplyShuffleMovement()
        {
            // If shuffle was released, check burst window
            if (!isShuffling)
            {
                if (burstWindowActive)
                {
                    ApplyBurstWindow();
                    return;
                }
                currentSpeed = 0f;
                rb.linearVelocity = Vector2.zero;
                ClearLean();
                SetState(MovementState.Idle);
                return;
            }

            // Drain stamina
            if (profile.shuffleStaminaDrain > 0f)
            {
                ConsumeStamina(profile.shuffleStaminaDrain * Time.fixedDeltaTime);

                if (CurrentStamina <= 0f)
                {
                    SetShuffling(false);
                    return;
                }
            }

            // Facing stays locked
            facingDirection = shuffleFacingLock;

            // Update lean cooldown
            if (leanCooldownTimer > 0f)
                leanCooldownTimer -= Time.fixedDeltaTime;

            // ── LEAN (from triggers L2/R2) ──
            Vector2 right = Vector2.Perpendicular(facingDirection) * -1f;

            if (Mathf.Abs(triggerLeanInput) > 0.15f)
            {
                // Determine lean direction from trigger input
                Vector2 newLeanDir = right * Mathf.Sign(triggerLeanInput);

                if (!isLeaning || Vector2.Dot(newLeanDir, leanDirection) < 0.5f)
                {
                    if (leanCooldownTimer <= 0f)
                    {
                        leanDirection = newLeanDir;
                        isLeaning = true;
                        leanCooldownTimer = profile.leanCooldown;
                        ConsumeStamina(profile.leanStaminaCost);
                        OnLeanStarted?.Invoke(leanDirection);
                    }
                }

                // Lean intensity driven by trigger pressure (analog)
                float targetIntensity = Mathf.Abs(triggerLeanInput);
                leanIntensity = Mathf.MoveTowards(leanIntensity, targetIntensity,
                    profile.leanSpeed * Time.fixedDeltaTime);

                // Slight drift in lean direction to sell the fake
                float leanMoveSpeed = profile.topSpeed * profile.shuffleSpeedRatio * 0.25f
                    * leanIntensity;
                currentSpeed = Mathf.MoveTowards(currentSpeed, leanMoveSpeed,
                    profile.shuffleAcceleration * Time.fixedDeltaTime);
                rb.linearVelocity = leanDirection * currentSpeed;
            }
            else
            {
                // No trigger input — return lean to center
                ReturnLeanToCenter();

                // ── SHUFFLE MOVEMENT (from stick) ──
                if (moveInput.sqrMagnitude > 0.1f)
                {
                    float shuffleTopSpeed = profile.topSpeed * profile.shuffleSpeedRatio
                        * GetFatigueMultiplier(profile.fatigueSpeedPenalty);
                    float shuffleAccel = profile.shuffleAcceleration
                        * GetFatigueMultiplier(profile.fatigueAccelerationPenalty);

                    currentSpeed = Mathf.MoveTowards(currentSpeed, shuffleTopSpeed,
                        shuffleAccel * Time.fixedDeltaTime);
                    rb.linearVelocity = moveInput.normalized * currentSpeed;
                }
                else
                {
                    currentSpeed = 0f;
                    rb.linearVelocity = Vector2.zero;
                }
            }
        }

        /// <summary>
        /// Burst window: brief period after releasing shuffle while leaning.
        /// Player holds stick in desired burst direction.
        /// When window closes, character explodes that way.
        /// </summary>
        protected virtual void ApplyBurstWindow()
        {
            burstWindowTimer -= Time.fixedDeltaTime;

            // Hold still during the window — player is choosing direction
            currentSpeed = 0f;
            rb.linearVelocity = Vector2.zero;

            if (burstWindowTimer <= 0f)
            {
                burstWindowActive = false;

                // Burst in whatever direction the stick is pointing
                if (moveInput.sqrMagnitude > 0.1f)
                {
                    facingDirection = moveInput.normalized;
                    shuffleBurstActive = true;
                    shuffleBurstTimer = profile.shuffleBurstDuration;
                    OnShuffleBurst?.Invoke();
                    SetState(MovementState.Idle);
                }
                else
                {
                    // No direction chosen — snap back, no burst
                    SetState(MovementState.Idle);
                }
            }
        }

        // ──────────────────────────────────────────────
        //  LEAN MANAGEMENT
        // ──────────────────────────────────────────────

        private void ReturnLeanToCenter()
        {
            if (leanIntensity > 0f)
            {
                leanIntensity = Mathf.MoveTowards(leanIntensity, 0f,
                    profile.leanReturnSpeed * Time.fixedDeltaTime);

                if (leanIntensity <= 0.01f)
                {
                    ClearLean();
                }
            }
        }

        private void ClearLean()
        {
            if (isLeaning)
            {
                OnLeanCentered?.Invoke();
            }
            leanDirection = Vector2.zero;
            leanIntensity = 0f;
            isLeaning = false;
        }

        /// <summary>
        /// Get the current lean offset for sprite rendering.
        /// Returns a world-space offset vector the sprite should shift by.
        /// </summary>
        public Vector2 GetLeanOffset()
        {
            return leanDirection * leanIntensity * profile.leanDistance;
        }

        /// <summary>
        /// Get the current lean direction and intensity for AI reading.
        /// AI treats this as apparent movement intent, filtered by
        /// the leaner's deception rating.
        /// </summary>
        public Vector2 GetApparentMovementDirection()
        {
            if (isLeaning && leanIntensity > 0.3f)
            {
                return leanDirection * leanIntensity;
            }
            return moveInput;
        }

        /// <summary>
        /// Is this character currently leaning/feinting?
        /// </summary>
        public bool IsLeaning => isLeaning;

        /// <summary>
        /// Current lean intensity (0-1). Used by sprite system and AI.
        /// </summary>
        public float LeanIntensity => leanIntensity;

        /// <summary>
        /// Current lean direction. Zero if not leaning.
        /// </summary>
        public Vector2 LeanDirection => leanDirection;

        // ──────────────────────────────────────────────
        //  AWARENESS
        // ──────────────────────────────────────────────

        /// <summary>
        /// Get the current awareness multiplier based on movement state.
        /// Used by AI and gameplay systems to determine feint resistance,
        /// reaction speed, etc.
        /// </summary>
        public virtual float GetAwarenessMultiplier()
        {
            if (CurrentState == MovementState.Shuffling)
                return profile.shuffleAwarenessMultiplier;
            if (CurrentState == MovementState.Sprinting)
                return 0.7f; // sprinting reduces awareness
            return 1f;
        }

        // ──────────────────────────────────────────────
        //  HARD CUT (plant-and-go)
        // ──────────────────────────────────────────────

        // Cut phases: Plant (stopped, facing snaps) → Recovery (accelerating in new direction)
        protected bool isInCutRecovery;

        protected virtual void PerformHardCut(float angleDelta)
        {
            if (CurrentState == MovementState.Cutting) return; // already cutting

            // Snap facing to the new direction immediately — no smooth rotation
            facingDirection = moveInput.normalized;

            // Kill speed — plant foot in the ground
            currentSpeed = 0f;
            rb.linearVelocity = Vector2.zero;

            // Consume stamina
            ConsumeStamina(profile.cutStaminaCost);

            // Enter plant phase — less traction = longer plant (harder to change direction)
            isInCutRecovery = false;
            float traction = ActiveSurface?.tractionMultiplier ?? 1f;
            float effectivePlantTime = profile.cutPlantDuration
                * (2f - GetFatigueMultiplier(profile.fatigueAgilityPenalty)) // fatigue penalty
                * (2f - Mathf.Clamp(traction, 0.1f, 2f));                    // traction penalty
            cutTimer = effectivePlantTime;
            SetState(MovementState.Cutting);

            OnHardCut?.Invoke();
        }

        // ──────────────────────────────────────────────
        //  JUMPING
        // ──────────────────────────────────────────────

        protected virtual void PerformJump()
        {
            isGrounded = false;
            SetState(MovementState.Airborne);

            // Calculate initial vertical velocity from desired height and time to apex.
            // v0 = 2 * height / timeToApex (derived from kinematic equations)
            float effectiveHeight = profile.jumpHeight * GetFatigueMultiplier(profile.fatigueJumpPenalty);
            verticalVelocity = (2f * effectiveHeight) / profile.timeToJumpApex;

            OnJumpStarted?.Invoke();
        }

        /// <summary>
        /// Call each frame to update vertical position. 
        /// Override for sport-specific aerial behavior.
        /// For top-down games, this can drive a "shadow + sprite offset" visual.
        /// </summary>
        protected virtual void UpdateVertical()
        {
            if (isGrounded) return;

            // Gravity derived from jump parameters: g = 2 * height / timeToApex²
            float gravity = (2f * profile.jumpHeight) / (profile.timeToJumpApex * profile.timeToJumpApex);
            verticalVelocity -= gravity * Time.deltaTime;

            float currentOffset = GetVerticalOffset();
            float verticalPosition = currentOffset + verticalVelocity * Time.deltaTime;

            if (verticalPosition <= 0f)
            {
                // Landed
                verticalPosition = 0f;
                verticalVelocity = 0f;
                isGrounded = true;
                SetState(MovementState.LandingRecovery);
                stateTimer = profile.landingRecoveryTime;
            }

            SetVerticalOffset(verticalPosition);
        }

        /// <summary>
        /// Get the current vertical offset (height above ground).
        /// </summary>
        protected virtual float GetVerticalOffset()
        {
            return spriteOffset != null ? spriteOffset.GetOffset() : 0f;
        }

        /// <summary>
        /// Set the vertical offset for the sprite.
        /// </summary>
        protected virtual void SetVerticalOffset(float offset)
        {
            if (spriteOffset != null)
            {
                spriteOffset.SetOffset(offset);
            }
        }

        // ──────────────────────────────────────────────
        //  DIVING
        // ──────────────────────────────────────────────

        protected virtual void PerformDive()
        {
            diveDirection = (moveInput.sqrMagnitude > 0.01f) ? moveInput.normalized : facingDirection;
            SetState(MovementState.Diving);
            stateTimer = profile.diveDuration;

            OnDiveStarted?.Invoke();
        }

        protected virtual void ApplyDiveMovement()
        {
            // Constant velocity during dive (distance / duration)
            float diveSpeed = profile.diveDistance / profile.diveDuration;
            rb.linearVelocity = diveDirection * diveSpeed;
        }

        // ──────────────────────────────────────────────
        //  STAMINA
        // ──────────────────────────────────────────────

        protected virtual void UpdateStamina()
        {
            // Drain — surface multiplier makes certain terrain more tiring
            float surfaceDrainMult = ActiveSurface?.staminaDrainMultiplier ?? 1f;
            if (CurrentState == MovementState.Sprinting)
            {
                ConsumeStamina(profile.sprintStaminaDrain * Time.deltaTime * surfaceDrainMult);
            }
            else if (CurrentState == MovementState.Jogging)
            {
                ConsumeStamina(profile.jogStaminaDrain * Time.deltaTime * surfaceDrainMult);
            }

            // Regen cooldown
            if (staminaRegenCooldown > 0f)
            {
                staminaRegenCooldown -= Time.deltaTime;
            }
            else if (CurrentState == MovementState.Idle
                  || CurrentState == MovementState.Jogging)
            {
                // Regenerate
                CurrentStamina = Mathf.Min(
                    CurrentStamina + profile.staminaRegenRate * Time.deltaTime,
                    profile.maxStamina
                );
                OnStaminaChanged?.Invoke(CurrentStamina / profile.maxStamina);
            }
        }

        protected void ConsumeStamina(float amount)
        {
            if (amount <= 0f) return;
            CurrentStamina = Mathf.Max(0f, CurrentStamina - amount);
            staminaRegenCooldown = profile.staminaRegenDelay;
            OnStaminaChanged?.Invoke(CurrentStamina / profile.maxStamina);
        }

        // ──────────────────────────────────────────────
        //  FATIGUE
        // ──────────────────────────────────────────────

        /// <summary>
        /// Returns a 0-1 multiplier representing how much of a given stat is
        /// available based on current stamina.
        /// 
        /// Uses an exponential curve so degradation is mild at first and 
        /// accelerates as stamina drops — you barely notice going from 100% 
        /// to 70%, but 30% to 0% is brutal.
        /// </summary>
        protected float GetFatigueMultiplier(float maxPenalty)
        {
            float staminaRatio = CurrentStamina / profile.maxStamina;

            // Exponential curve: 1 - penalty * (1 - ratio²)
            // At full stamina (ratio=1): returns 1.0 (no penalty)
            // At zero stamina (ratio=0): returns 1.0 - maxPenalty
            float fatigueFactor = 1f - maxPenalty * (1f - staminaRatio * staminaRatio);
            return fatigueFactor;
        }

        // ──────────────────────────────────────────────
        //  EFFECTIVE STATS (after fatigue)
        // ──────────────────────────────────────────────

        public virtual float GetEffectiveTopSpeed()
        {
            float speed = profile.topSpeed * GetFatigueMultiplier(profile.fatigueSpeedPenalty);

            if (ActiveSurface != null)
            {
                speed *= ActiveSurface.speedMultiplier;

                // Slope: directional speed delta based on current movement input
                if (ActiveSurface.surfaceType == SurfaceType.Slope
                    && moveInput.sqrMagnitude > 0.01f)
                {
                    speed *= ActiveSurface.GetSlopeSpeedMultiplier(moveInput);
                }
            }

            return speed;
        }

        public float GetEffectiveJogSpeed()
        {
            return GetEffectiveTopSpeed() * profile.jogSpeedRatio;
        }

        public float GetEffectiveDiveRecovery()
        {
            return profile.diveRecoveryTime + profile.fatigueDiveRecoveryPenalty * (1f - GetFatigueMultiplier(1f));
        }

        // ──────────────────────────────────────────────
        //  STATE TIMERS
        // ──────────────────────────────────────────────

        protected virtual void UpdateStateTimers()
        {
            // Jump vertical update
            if (CurrentState == MovementState.Airborne)
            {
                UpdateVertical();
            }

            // Dive duration
            if (CurrentState == MovementState.Diving)
            {
                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0f)
                {
                    SetState(MovementState.DiveRecovery);
                    stateTimer = GetEffectiveDiveRecovery();
                    rb.linearVelocity = Vector2.zero;
                    currentSpeed = 0f;
                }
            }

            // Dive recovery
            if (CurrentState == MovementState.DiveRecovery)
            {
                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0f)
                {
                    SetState(MovementState.Idle);
                }
            }

            // Landing recovery
            if (CurrentState == MovementState.LandingRecovery)
            {
                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0f)
                {
                    SetState(MovementState.Idle);
                }
            }

            // Cut phases: Plant → Recovery → Normal
            if (CurrentState == MovementState.Cutting)
            {
                cutTimer -= Time.deltaTime;
                if (cutTimer <= 0f)
                {
                    if (!isInCutRecovery)
                    {
                        // Plant phase finished — enter recovery (can move again)
                        isInCutRecovery = true;
                        cutTimer = 0.2f; // brief recovery window with cut acceleration
                    }
                    else
                    {
                        // Recovery finished — back to normal
                        isInCutRecovery = false;
                        SetState(moveInput.sqrMagnitude > 0.01f ? MovementState.Jogging : MovementState.Idle);
                    }
                }
            }
        }

        // ──────────────────────────────────────────────
        //  UTILITY
        // ──────────────────────────────────────────────

        /// <summary>
        /// Smoothly rotates 'from' toward 'to' by up to 'maxDegrees'.
        /// </summary>
        protected static Vector2 RotateToward(Vector2 from, Vector2 to, float maxDegrees)
        {
            float angle = Vector2.SignedAngle(from, to);
            float clampedAngle = Mathf.Clamp(angle, -maxDegrees, maxDegrees);
            return (Quaternion.Euler(0, 0, clampedAngle) * from).normalized;
        }

        // ──────────────────────────────────────────────
        //  PUBLIC QUERIES
        // ──────────────────────────────────────────────

        public float GetNormalizedStamina() => CurrentStamina / profile.maxStamina;
        public float GetNormalizedSpeed() => currentSpeed / GetEffectiveTopSpeed();
        public Vector2 GetFacingDirection() => facingDirection;
        public float GetCurrentSpeed() => currentSpeed;
        public bool IsGrounded() => isGrounded;
        public MovementProfile Profile => profile;

        // ──────────────────────────────────────────────
        //  SURFACE / TERRAIN API
        // ──────────────────────────────────────────────

        /// <summary>
        /// Called by SurfaceZone when this character enters its trigger.
        /// </summary>
        public void EnterSurface(SurfaceZone zone, SurfaceDefinition def)
        {
            // Prevent duplicate entries (e.g. fast re-entry edge case)
            for (int i = 0; i < activeSurfaceList.Count; i++)
            {
                if (activeSurfaceList[i].zone == zone) return;
            }
            activeSurfaceList.Add((zone, def));
            RefreshActiveSurface();
        }

        /// <summary>
        /// Called by SurfaceZone when this character exits its trigger.
        /// </summary>
        public void ExitSurface(SurfaceZone zone)
        {
            for (int i = 0; i < activeSurfaceList.Count; i++)
            {
                if (activeSurfaceList[i].zone == zone)
                {
                    activeSurfaceList.RemoveAt(i);
                    break;
                }
            }
            RefreshActiveSurface();
        }

        private void RefreshActiveSurface()
        {
            ActiveSurface = null;
            for (int i = 0; i < activeSurfaceList.Count; i++)
            {
                var def = activeSurfaceList[i].def;
                if (ActiveSurface == null || def.priority > ActiveSurface.priority)
                    ActiveSurface = def;
            }
        }

        // ──────────────────────────────────────────────
        //  RESET
        // ──────────────────────────────────────────────

        /// <summary>
        /// Reset all movement state to defaults. Used by game reset.
        /// </summary>
        public virtual void ResetMovementState()
        {
            CurrentStamina = profile.maxStamina;
            currentSpeed = 0f;
            verticalVelocity = 0f;
            isGrounded = true;
            isSprinting = false;
            isShuffling = false;
            shuffleBurstActive = false;
            shuffleBurstTimer = 0f;
            leanDirection = Vector2.zero;
            leanIntensity = 0f;
            leanCooldownTimer = 0f;
            isLeaning = false;
            triggerLeanInput = 0f;
            burstWindowActive = false;
            burstWindowTimer = 0f;
            moveInput = Vector2.zero;
            facingDirection = Vector2.right;
            stateTimer = 0f;
            cutTimer = 0f;
            isInCutRecovery = false;
            staminaRegenCooldown = 0f;
            SetState(MovementState.Idle);
            SetVerticalOffset(0f);

            OnStaminaChanged?.Invoke(1f);
        }
    }
}