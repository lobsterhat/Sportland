using Sportland.Movement;
using Sportland.World;
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

        [Header("=== TAG: BARGE ===")]

        [Tooltip("Radius in which the runner can connect a barge on an It player.")]
        [SerializeField] private float bargeRadius = 2.2f;

        [Tooltip("How long the barged It player is stunned (seconds).")]
        [SerializeField] private float bargeStunDuration = 0.8f;

        [Tooltip("Distance of the brief burst-through when the barge connects.")]
        [SerializeField] private float bargeBurstDistance = 1.5f;

        [Tooltip("Duration of the burst-through (seconds). Keep short.")]
        [SerializeField] private float bargeBurstDuration = 0.18f;

        [Tooltip("Cooldown before this runner can barge again (seconds).")]
        [SerializeField] private float bargeCooldown = 5f;

        [Tooltip("Stamina cost to barge.")]
        [SerializeField] private float bargeStaminaCost = 20f;

        [Tooltip("Minimum dot product between runner velocity and direction-to-It required to barge. " +
                 "0 = any direction, 1 = must be heading directly at them.")]
        [SerializeField] private float bargeAlignmentThreshold = 0.35f;

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

        // Barge state
        private float bargeCooldownTimer;
        private float stunTimer;          // counts down on the It player who was barged
        private bool isBargeBursting;     // true on the runner during the brief burst-through
        private float bargeBurstTimer;
        private Vector2 bargeBurstDirection;

        // ──────────────────────────────────────────────
        //  EVENTS
        // ──────────────────────────────────────────────

        public System.Action<TagMovementController> OnTagged;       // this player tagged someone
        public System.Action<TagMovementController> OnBecameIt;     // this player is now It
        public System.Action OnFuseExpired;
        public System.Action OnLungeStarted;
        public System.Action OnEvasionBurst;
        public System.Action OnEliminated;                          // this player was eliminated
        public System.Action<float> OnFuseChanged;                  // passes normalized 0-1
        public System.Action OnBargeConnected;                      // this runner connected a barge
        public System.Action OnBargeStunned;                        // this It player was stunned by a barge

        // ──────────────────────────────────────────────
        //  PUBLIC QUERIES (Tag-specific)
        // ──────────────────────────────────────────────

        /// <summary>True while this It player is stunned from a barge (and not permanently eliminated).</summary>
        public bool IsBargeStunned => !IsEliminated && stunTimer > 0f;

        /// <summary>Normalized barge cooldown remaining (0 = ready, 1 = just used).</summary>
        public float BargeCooldownNormalized => bargeCooldown > 0f ? bargeCooldownTimer / bargeCooldown : 0f;

        /// <summary>Normalized evasion burst cooldown (0 = ready, 1 = just used).</summary>
        public float EvasionCooldownNormalized => evasionBurstCooldown > 0f ? evasionCooldownTimer / evasionBurstCooldown : 0f;

        /// <summary>True if this It player can lunge right now.</summary>
        public bool LungeReady => CurrentRole == TagRole.It
            && CanAct() && !isLunging
            && CurrentStamina >= lungeStaminaCost
            && (ActiveSurface == null || ActiveSurface.allowLunge);

        /// <summary>True if this runner can barge right now.</summary>
        public bool BargeReady => CurrentRole == TagRole.Runner
            && CanAct() && !isBargeBursting
            && bargeCooldownTimer <= 0f
            && CurrentStamina >= bargeStaminaCost
            && (ActiveSurface == null || ActiveSurface.allowBarge);

        /// <summary>True if this runner can use evasion burst right now.</summary>
        public bool EvasionReady => CurrentRole == TagRole.Runner
            && CanAct() && !isEvading
            && evasionCooldownTimer <= 0f
            && CurrentStamina >= evasionBurstStaminaCost;

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
            UpdateBarge();
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

        /// <summary>
        /// Remove this player from the game. Called by the game manager
        /// when they are tagged in elimination mode.
        /// </summary>
        public void Eliminate()
        {
            if (IsEliminated) return;
            IsEliminated = true;
            SetState(MovementState.Stunned);
            rb.linearVelocity = Vector2.zero;
            currentSpeed = 0f;
            OnEliminated?.Invoke();
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
                    // Signal the tag but don't transfer It status.
                    // The game manager listens to OnTagged and handles elimination.
                    OnTagged?.Invoke(closest);
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
            if (ActiveSurface != null && !ActiveSurface.allowLunge) return false;

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
        //  BARGE (Runner offensive counter-move)
        // ──────────────────────────────────────────────

        /// <summary>
        /// Runner charges through an It player, stunning them briefly.
        /// Requires moving toward the It player within barge radius.
        /// High risk (running at the chaser), high reward (they freeze, you sprint away).
        /// </summary>
        public bool TryBarge()
        {
            if (CurrentRole != TagRole.Runner) return false;
            if (!CanAct()) return false;
            if (isBargeBursting) return false;
            if (bargeCooldownTimer > 0f) return false;
            if (CurrentStamina < bargeStaminaCost) return false;
            if (ActiveSurface != null && !ActiveSurface.allowBarge) return false;

            Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, bargeRadius, taggableLayer);

            TagMovementController target = null;
            float closestDist = float.MaxValue;

            foreach (var hit in hits)
            {
                if (hit.gameObject == gameObject) continue;

                var t = hit.GetComponent<TagMovementController>();
                if (t == null) continue;
                if (t.CurrentRole != TagRole.It) continue;
                if (t.IsEliminated) continue;
                if (t.IsBargeStunned) continue; // already stunned

                // Runner must be moving toward the target
                Vector2 toTarget = ((Vector2)t.transform.position - (Vector2)transform.position).normalized;
                Vector2 vel = rb.linearVelocity.sqrMagnitude > 0.01f
                    ? rb.linearVelocity.normalized
                    : facingDirection;

                if (Vector2.Dot(vel, toTarget) < bargeAlignmentThreshold) continue;

                float dist = Vector2.Distance(transform.position, t.transform.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    target = t;
                }
            }

            if (target == null) return false;

            // Barge connects!
            ConsumeStamina(bargeStaminaCost);
            bargeCooldownTimer = bargeCooldown;

            // Stun the It player
            target.ApplyBarge(bargeStunDuration);

            // Runner bursts through
            bargeBurstDirection = ((Vector2)target.transform.position - (Vector2)transform.position).normalized;
            isBargeBursting = true;
            bargeBurstTimer = bargeBurstDuration;

            // Brief immunity while passing through
            IsImmune = true;
            immunityTimer = bargeBurstDuration + 0.15f;

            OnBargeConnected?.Invoke();
            return true;
        }

        /// <summary>
        /// Called on the It player to apply the stun from a barge.
        /// </summary>
        public void ApplyBarge(float stunDuration)
        {
            SetState(MovementState.Stunned);
            rb.linearVelocity = Vector2.zero;
            currentSpeed = 0f;
            stunTimer = stunDuration;
            OnBargeStunned?.Invoke();
        }

        private void UpdateBarge()
        {
            // Cooldown on the runner
            if (bargeCooldownTimer > 0f)
                bargeCooldownTimer -= Time.deltaTime;

            // Burst-through: override velocity while active
            if (isBargeBursting)
            {
                bargeBurstTimer -= Time.deltaTime;
                if (bargeBurstTimer <= 0f)
                    isBargeBursting = false;
            }

            // Stun countdown on the It player (stunTimer is only nonzero on the It side)
            if (CurrentState == MovementState.Stunned && stunTimer > 0f)
            {
                stunTimer -= Time.deltaTime;
                if (stunTimer <= 0f)
                {
                    stunTimer = 0f;
                    SetState(MovementState.Idle);
                }
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
        /// Override to apply It speed boost, evasion burst, and barge burst-through.
        /// </summary>
        protected override void ApplyMovement()
        {
            // Barge burst-through: override base movement entirely for its brief duration
            if (isBargeBursting)
            {
                float burstSpeed = bargeBurstDistance / bargeBurstDuration;
                rb.linearVelocity = bargeBurstDirection * burstSpeed;
                return;
            }

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

            // Reset barge state
            bargeCooldownTimer = 0f;
            stunTimer = 0f;
            isBargeBursting = false;
            bargeBurstTimer = 0f;
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