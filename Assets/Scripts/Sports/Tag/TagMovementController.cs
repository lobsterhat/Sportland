using UnityEngine;

/// <summary>
/// Tag-specific extension of BaseMovementController.
/// Adds: It/Runner state, tag detection, lunge mechanic, 
/// evasion burst (dodge), and the Burning Fuse timer.
/// </summary>
public class TagMovementController : BaseMovementController
{
    // ──────────────────────────────────────────────
    //  TAG-SPECIFIC CONFIGURATION
    // ──────────────────────────────────────────────

    [Header("=== TAG: IT MECHANICS ===")]

    [Tooltip("Speed boost multiplier when 'It' (slight advantage to keep chases viable). " +
             "1.0 = no boost, 1.1 = 10% faster.")]
    [SerializeField] private float itSpeedMultiplier = 1.05f;

    [Tooltip("Tag reach radius — how close the It player must be to tag someone.")]
    [SerializeField] private float tagReachRadius = 1.2f;

    [Tooltip("Layer mask for taggable players.")]
    [SerializeField] private LayerMask taggableLayer;

    [Tooltip("Brief immunity duration after being tagged (prevents instant tag-backs).")]
    [SerializeField] private float tagImmunityDuration = 1.5f;

    [Header("=== TAG: LUNGE ===")]

    [Tooltip("Lunge distance — a short, fast burst specifically for tagging. " +
             "Shorter than a dive but faster recovery.")]
    [SerializeField] private float lungeDistance = 1.5f;

    [Tooltip("Lunge duration (seconds). Quick and committal.")]
    [SerializeField] private float lungeDuration = 0.2f;

    [Tooltip("Recovery time after a missed lunge.")]
    [SerializeField] private float lungeRecoveryTime = 0.4f;

    [Tooltip("Stamina cost for a lunge.")]
    [SerializeField] private float lungeStaminaCost = 8f;

    [Header("=== TAG: EVASION BURST ===")]

    [Tooltip("Short speed boost when dodging (multiplier on current speed).")]
    [SerializeField] private float evasionBurstMultiplier = 1.4f;

    [Tooltip("Duration of the evasion burst (seconds).")]
    [SerializeField] private float evasionBurstDuration = 0.25f;

    [Tooltip("Cooldown between evasion bursts (seconds).")]
    [SerializeField] private float evasionBurstCooldown = 3f;

    [Tooltip("Stamina cost for an evasion burst.")]
    [SerializeField] private float evasionBurstStaminaCost = 10f;

    [Header("=== TAG: BURNING FUSE ===")]

    [Tooltip("Total fuse time each player has (seconds). " +
             "Only drains while It. Eliminated at zero.")]
    [SerializeField] private float totalFuseTime = 15f;

    // ──────────────────────────────────────────────
    //  TAG STATE
    // ──────────────────────────────────────────────

    public enum TagRole { Runner, It }

    public TagRole CurrentRole { get; private set; } = TagRole.Runner;
    public float FuseTimeRemaining { get; private set; }
    public bool IsImmune { get; private set; }
    public bool IsEliminated { get; private set; }

    // Internal timers
    private float immunityTimer;
    private float lungeTimer;
    private float evasionTimer;
    private float evasionCooldownTimer;
    private Vector2 lungeDirection;
    private bool isLunging;
    private bool isEvading;

    // ──────────────────────────────────────────────
    //  EVENTS
    // ──────────────────────────────────────────────

    public System.Action<TagMovementController> OnTagged;       // this player tagged someone
    public System.Action<TagMovementController> OnBecameIt;     // this player is now It
    public System.Action OnFuseExpired;
    public System.Action OnLungeStarted;
    public System.Action OnEvasionBurst;
    public System.Action<float> OnFuseChanged;                  // passes normalized 0-1

    // ──────────────────────────────────────────────
    //  INITIALIZATION
    // ──────────────────────────────────────────────

    protected override void Start()
    {
        base.Start();
        FuseTimeRemaining = totalFuseTime;
    }

    // ──────────────────────────────────────────────
    //  UPDATE LOOP
    // ──────────────────────────────────────────────

    protected override void Update()
    {
        base.Update();

        if (IsEliminated) return;

        UpdateImmunity();
        UpdateFuse();
        UpdateLunge();
        UpdateEvasion();
    }

    // ──────────────────────────────────────────────
    //  ROLE MANAGEMENT
    // ──────────────────────────────────────────────

    /// <summary>
    /// Make this player It. Called by game manager or by tag transfer.
    /// </summary>
    public void BecomeIt()
    {
        CurrentRole = TagRole.It;
        IsImmune = false;
        OnBecameIt?.Invoke(this);
    }

    /// <summary>
    /// Make this player a Runner (after successfully tagging someone).
    /// Grants brief immunity to prevent tag-backs.
    /// </summary>
    public void BecomeRunner()
    {
        CurrentRole = TagRole.Runner;
        IsImmune = true;
        immunityTimer = tagImmunityDuration;
    }

    // ──────────────────────────────────────────────
    //  TAG ACTION
    // ──────────────────────────────────────────────

    /// <summary>
    /// Attempt to tag the nearest taggable player within reach.
    /// Can be called during normal movement or at the apex of a lunge/dive.
    /// Returns the tagged player, or null if nobody in range.
    /// </summary>
    public TagMovementController TryTag()
    {
        if (CurrentRole != TagRole.It) return null;

        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, tagReachRadius, taggableLayer);

        TagMovementController closest = null;
        float closestDist = float.MaxValue;

        foreach (var hit in hits)
        {
            if (hit.gameObject == gameObject) continue;

            var target = hit.GetComponent<TagMovementController>();
            if (target == null) continue;
            if (target.IsImmune) continue;
            if (target.IsEliminated) continue;
            if (target.CurrentRole == TagRole.It) continue;

            float dist = Vector2.Distance(transform.position, target.transform.position);
            if (dist < closestDist)
            {
                closestDist = dist;
                closest = target;
            }
        }

        if (closest != null)
        {
            // Transfer tag
            TransferTag(closest);
        }

        return closest;
    }

    private void TransferTag(TagMovementController target)
    {
        OnTagged?.Invoke(target);

        // This player becomes a runner
        BecomeRunner();

        // Target becomes It
        target.BecomeIt();
    }

    // ──────────────────────────────────────────────
    //  LUNGE (It-only offensive move)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Short, fast burst toward a target. Only available when It.
    /// Shorter range than a dive, faster recovery, and auto-attempts
    /// a tag at the end.
    /// </summary>
    public bool TryLunge()
    {
        if (CurrentRole != TagRole.It) return false;
        if (!CanAct()) return false;
        if (isLunging) return false;
        if (CurrentStamina < lungeStaminaCost) return false;

        ConsumeStamina(lungeStaminaCost);
        PerformLunge();
        return true;
    }

    private void PerformLunge()
    {
        lungeDirection = (moveInput.sqrMagnitude > 0.01f) ? moveInput.normalized : facingDirection;
        isLunging = true;
        lungeTimer = lungeDuration;
        SetState(MovementState.Diving); // reuse Diving state for movement lockout

        OnLungeStarted?.Invoke();
    }

    private void UpdateLunge()
    {
        if (!isLunging) return;

        lungeTimer -= Time.deltaTime;

        if (lungeTimer <= 0f)
        {
            // Lunge complete — try to tag at the endpoint
            TryTag();

            isLunging = false;
            SetState(MovementState.DiveRecovery);
            stateTimer = lungeRecoveryTime;
            rb.linearVelocity = Vector2.zero;
            currentSpeed = 0f;
        }
        else
        {
            // Lunge movement
            float lungeSpeed = lungeDistance / lungeDuration;
            rb.linearVelocity = lungeDirection * lungeSpeed;
        }
    }

    // ──────────────────────────────────────────────
    //  EVASION BURST (Runner-only defensive move)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Quick speed burst to dodge a tag attempt. Runner only.
    /// On cooldown to prevent spam.
    /// </summary>
    public bool TryEvasionBurst()
    {
        if (CurrentRole != TagRole.Runner) return false;
        if (!CanAct()) return false;
        if (isEvading) return false;
        if (evasionCooldownTimer > 0f) return false;
        if (CurrentStamina < evasionBurstStaminaCost) return false;

        ConsumeStamina(evasionBurstStaminaCost);
        isEvading = true;
        evasionTimer = evasionBurstDuration;

        OnEvasionBurst?.Invoke();
        return true;
    }

    private void UpdateEvasion()
    {
        // Cooldown tick
        if (evasionCooldownTimer > 0f)
        {
            evasionCooldownTimer -= Time.deltaTime;
        }

        // Active evasion
        if (!isEvading) return;

        evasionTimer -= Time.deltaTime;
        if (evasionTimer <= 0f)
        {
            isEvading = false;
            evasionCooldownTimer = evasionBurstCooldown;
        }
    }

    // ──────────────────────────────────────────────
    //  BURNING FUSE
    // ──────────────────────────────────────────────

    private void UpdateFuse()
    {
        if (CurrentRole != TagRole.It) return;

        FuseTimeRemaining -= Time.deltaTime;
        OnFuseChanged?.Invoke(FuseTimeRemaining / totalFuseTime);

        if (FuseTimeRemaining <= 0f)
        {
            FuseTimeRemaining = 0f;
            IsEliminated = true;
            SetState(MovementState.Stunned);
            rb.linearVelocity = Vector2.zero;
            currentSpeed = 0f;
            OnFuseExpired?.Invoke();
        }
    }

    // ──────────────────────────────────────────────
    //  IMMUNITY
    // ──────────────────────────────────────────────

    private void UpdateImmunity()
    {
        if (!IsImmune) return;

        immunityTimer -= Time.deltaTime;
        if (immunityTimer <= 0f)
        {
            IsImmune = false;
        }
    }

    // ──────────────────────────────────────────────
    //  OVERRIDES
    // ──────────────────────────────────────────────

    /// <summary>
    /// Override to apply It speed boost and evasion burst multiplier.
    /// </summary>
    protected override void ApplyMovement()
    {
        // Let base handle all core movement
        base.ApplyMovement();

        // Apply It speed boost (post-process velocity)
        if (CurrentRole == TagRole.It && CanAct())
        {
            rb.linearVelocity *= itSpeedMultiplier;
        }

        // Apply evasion burst (post-process velocity)
        if (isEvading && CanAct())
        {
            rb.linearVelocity *= evasionBurstMultiplier;
        }
    }

    /// <summary>
    /// Override CanAct to block actions while eliminated.
    /// </summary>
    public override bool CanAct()
    {
        if (IsEliminated) return false;
        return base.CanAct();
    }

    // ──────────────────────────────────────────────
    //  DEBUG
    // ──────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        // Visualize tag reach
        Gizmos.color = CurrentRole == TagRole.It ? Color.red : Color.green;
        Gizmos.DrawWireSphere(transform.position, tagReachRadius);
    }
}
