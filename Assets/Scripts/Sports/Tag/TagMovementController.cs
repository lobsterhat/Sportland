using Sportland.Movement;
using UnityEngine;
using static Sportland.Movement.BaseMovementController;
using static UnityEngine.RuleTile.TilingRuleOutput;


namespace Sportland.Sports.Tag
{
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

        [Header("=== TAG: TAG-BACK COOLDOWN ===")]

        [Tooltip("Duration after being tagged before you can tag back the player who tagged you (seconds).")]
        [SerializeField] private float tagBackCooldown = 3f;

        [Header("=== TAG: DEBUG ===")]

        [Tooltip("When enabled, this character always stays It — tags are registered but IT status never transfers away. Useful for testing chaser AI.")]
        [SerializeField] private bool alwaysIt = false;

        // ──────────────────────────────────────────────
        //  TAG STATE
        // ──────────────────────────────────────────────

        public enum TagRole { Runner, It }

        public TagRole CurrentRole { get; private set; } = TagRole.Runner;
        public float FuseTimeRemaining { get; private set; }
        public bool IsImmune { get; set; }
        public bool IsEliminated { get; private set; }
        public bool InSafeZone { get; set; }

        // Tag-back tracking
        private TagMovementController taggedByPlayer;  // who tagged me last
        private float tagBackTimer;                     // time remaining before I can tag them back

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
            if (alwaysIt)
                CurrentRole = TagRole.It;
        }

        // ──────────────────────────────────────────────
        //  UPDATE LOOP
        // ──────────────────────────────────────────────

        protected override void Update()
        {
            base.Update();

            if (IsEliminated) return;

            UpdateImmunity();
            UpdateTagBackCooldown();
            //UpdateFuse();
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
        /// If alwaysIt is set, this is a no-op — the character never gives up It status.
        /// </summary>
        public void BecomeRunner()
        {
            if (alwaysIt) return;
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
                if (target.InSafeZone) continue;
                if (target.IsEliminated) continue;
                if (target.CurrentRole == TagRole.It) continue;

                // Tag-back cooldown: can't tag the player who just tagged me
                if (target == taggedByPlayer && tagBackTimer > 0f) continue;

                float dist = Vector2.Distance(transform.position, target.transform.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = target;
                }
            }

            if (closest != null)
            {
                if (alwaysIt)
                {
                    // Acknowledge the tag (fire event, give runner brief immunity)
                    // but keep It status here — useful for AI testing.
                    OnTagged?.Invoke(closest);
                    closest.BecomeRunner(); // grants immunity; they're already a runner so no role change
                }
                else
                {
                    TransferTag(closest);
                }
            }

            return closest;
        }

        private void TransferTag(TagMovementController target)
        {
            OnTagged?.Invoke(target);

            // This player becomes a runner
            BecomeRunner();

            // Target becomes It — record who tagged them for tag-back cooldown
            target.taggedByPlayer = this;
            target.tagBackTimer = target.tagBackCooldown;
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

            // Only count down if there's an active timer (from tag transfer).
            // External immunity (safe zones) manages IsImmune directly
            // and doesn't use the timer.
            if (immunityTimer > 0f)
            {
                immunityTimer -= Time.deltaTime;
                if (immunityTimer <= 0f)
                {
                    immunityTimer = 0f;
                    IsImmune = false;
                }
            }
        }

        // ──────────────────────────────────────────────
        //  TAG-BACK COOLDOWN
        // ──────────────────────────────────────────────

        private void UpdateTagBackCooldown()
        {
            if (tagBackTimer <= 0f) return;

            tagBackTimer -= Time.deltaTime;
            if (tagBackTimer <= 0f)
            {
                tagBackTimer = 0f;
                taggedByPlayer = null;
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

        /// <summary>
        /// Reset all Tag-specific state in addition to base movement state.
        /// </summary>
        public override void ResetMovementState()
        {
            base.ResetMovementState();

            // Reset Tag state
            IsEliminated = false;
            IsImmune = false;
            InSafeZone = false;
            FuseTimeRemaining = totalFuseTime;

            // Reset tag-back
            taggedByPlayer = null;
            tagBackTimer = 0f;

            // Reset timers
            immunityTimer = 0f;
            lungeTimer = 0f;
            evasionTimer = 0f;
            evasionCooldownTimer = 0f;
            isLunging = false;
            isEvading = false;
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
}