using UnityEngine;

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

    // ──────────────────────────────────────────────
    //  EVENTS (for UI, VFX, audio hooks)
    // ──────────────────────────────────────────────

    public System.Action<MovementState, MovementState> OnStateChanged;
    public System.Action<float> OnStaminaChanged; // passes normalized 0-1
    public System.Action OnDiveStarted;
    public System.Action OnJumpStarted;
    public System.Action OnHardCut;

    // ──────────────────────────────────────────────
    //  UNITY LIFECYCLE
    // ──────────────────────────────────────────────

    protected virtual void Awake()
    {
        rb = GetComponent<Rigidbody2D>();

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
    /// Attempt to jump. Fails if not grounded, insufficient stamina, or in
    /// a non-cancellable state.
    /// </summary>
    public virtual bool TryJump()
    {
        if (!CanAct()) return false;
        if (!isGrounded) return false;
        if (CurrentStamina < profile.jumpStaminaCost) return false;

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
        }

        if (moveInput.sqrMagnitude < 0.01f)
        {
            // No input — decelerate to stop
            ApplyDeceleration();

            if (currentSpeed < 0.1f)
            {
                currentSpeed = 0f;
                SetState(MovementState.Idle);
            }
        }
        else
        {
            // Check for hard cut
            float angleDelta = Vector2.Angle(facingDirection, moveInput);
            if (angleDelta >= profile.hardCutAngleThreshold && currentSpeed > GetEffectiveTopSpeed() * 0.3f)
            {
                PerformHardCut(angleDelta);
            }

            // Rotate facing toward input
            float effectiveTurnSpeed = profile.turnSpeed * GetFatigueMultiplier(profile.fatigueAgilityPenalty);
            facingDirection = RotateToward(facingDirection, moveInput, effectiveTurnSpeed * Time.fixedDeltaTime);

            // Accelerate
            float targetSpeed = isSprinting ? GetEffectiveTopSpeed() : GetEffectiveJogSpeed();
            float effectiveAcceleration = (CurrentState == MovementState.Cutting)
                ? profile.cutRecoveryAcceleration
                : profile.acceleration;
            effectiveAcceleration *= GetFatigueMultiplier(profile.fatigueAccelerationPenalty);

            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, effectiveAcceleration * Time.fixedDeltaTime);

            // Update state
            if (isSprinting && currentSpeed > GetEffectiveJogSpeed())
                SetState(MovementState.Sprinting);
            else if (currentSpeed > 0.1f)
                SetState(MovementState.Jogging);
        }

        rb.linearVelocity = facingDirection * currentSpeed;
    }

    protected virtual void ApplyDeceleration()
    {
        currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, profile.deceleration * Time.fixedDeltaTime);
    }

    // ──────────────────────────────────────────────
    //  HARD CUT
    // ──────────────────────────────────────────────

    protected virtual void PerformHardCut(float angleDelta)
    {
        if (CurrentState == MovementState.Cutting) return; // already cutting

        // Speed penalty scales with cut angle: 90° = full penalty, threshold° = none
        float cutSeverity = Mathf.InverseLerp(profile.hardCutAngleThreshold, 180f, angleDelta);
        float retention = Mathf.Lerp(1f, profile.cutSpeedRetention, cutSeverity);
        retention *= GetFatigueMultiplier(profile.fatigueAgilityPenalty); // fatigue makes cuts worse

        currentSpeed *= retention;
        ConsumeStamina(profile.cutStaminaCost);
        SetState(MovementState.Cutting);
        cutTimer = 0.15f; // brief "cutting" state before returning to normal

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

        // Apply vertical offset (could be transform.position.y or a sprite offset)
        // Subclasses should implement based on their perspective system.
        float verticalPosition = GetVerticalOffset() + verticalVelocity * Time.deltaTime;

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
        // Drain
        if (CurrentState == MovementState.Sprinting)
        {
            ConsumeStamina(profile.sprintStaminaDrain * Time.deltaTime);
        }
        else if (CurrentState == MovementState.Jogging)
        {
            ConsumeStamina(profile.jogStaminaDrain * Time.deltaTime);
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

    public float GetEffectiveTopSpeed()
    {
        return profile.topSpeed * GetFatigueMultiplier(profile.fatigueSpeedPenalty);
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

        // Cut recovery
        if (CurrentState == MovementState.Cutting)
        {
            cutTimer -= Time.deltaTime;
            if (cutTimer <= 0f)
            {
                SetState(moveInput.sqrMagnitude > 0.01f ? MovementState.Jogging : MovementState.Idle);
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
}
