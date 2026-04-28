using System;
using Sportland.Movement;
using UnityEngine;
using static Sportland.Movement.BaseMovementController;

namespace Sportland.Sports.Demoball
{
    /// <summary>
    /// Demoball extension of BaseMovementController.
    ///
    /// Adds:
    ///   - Role assignment (Scorer / Blocker / Defender) per period
    ///   - Ball carry state with a speed penalty
    ///   - Pass mechanic (scorer and blocker ball-carriers may pass)
    ///   - Touch-down action (while in scoring ring, carrying ball)
    ///   - Tackle mechanic (Defenders only)
    ///   - Tag-up requirement after being tackled (must reach TagUpZone)
    ///   - Scoring ring awareness (tracked by ScoringRing trigger callbacks)
    /// </summary>
    public class DemoballMovementController : BaseMovementController
    {
        // ──────────────────────────────────────────────
        //  ROLE
        // ──────────────────────────────────────────────

        [Header("=== DEMOBALL: ROLE ===")]
        [Tooltip("Starting role. Overwritten by DemoballGameManager at each period start.")]
        [SerializeField] private DemoballRole role = DemoballRole.Defender;

        public DemoballRole Role => role;

        // ──────────────────────────────────────────────
        //  CARRY CONFIG
        // ──────────────────────────────────────────────

        [Header("=== DEMOBALL: CARRY ===")]
        [Tooltip("Top-speed multiplier while carrying a ball. < 1 = slower.")]
        [SerializeField] private float carrySpeedMultiplier = 0.85f;

        [Tooltip("Debug speed scalar applied to every Demoball player so the action stays " +
                 "readable while we tune AI. Set to 1.0 for production speed.")]
        [SerializeField] private float debugSpeedMultiplier = 0.7f;

        // ──────────────────────────────────────────────
        //  TACKLE CONFIG
        // ──────────────────────────────────────────────

        [Header("=== DEMOBALL: TACKLE ===")]
        [Tooltip("Reach radius for a tackle attempt (world units).")]
        [SerializeField] private float tackleReach = 1.4f;

        [Tooltip("Layer mask containing all Demoball players.")]
        [SerializeField] private LayerMask playerLayer;

        [Tooltip("How long the tackled player is in Stunned state before they can move to tag-up.")]
        [SerializeField] private float tackleStunsFor = 0.5f;

        // ──────────────────────────────────────────────
        //  ENGAGEMENT CONFIG
        // ──────────────────────────────────────────────

        [Header("=== DEMOBALL: ENGAGEMENT ===")]
        [Tooltip("Default engagement duration (seconds) when no specific value is supplied. " +
                 "Future per-character stats will replace this fixed timer.")]
        [SerializeField] private float defaultEngagementDuration = 2f;

        // ──────────────────────────────────────────────
        //  SHOVE CONFIG
        // ──────────────────────────────────────────────

        [Header("=== DEMOBALL: SHOVE ===")]
        [Tooltip("Cooldown (seconds) between shove attempts. Prevents spam-shoving while engaged.")]
        [SerializeField] private float shoveCooldown = 0.4f;

        [Tooltip("Loser is held prone (extended stun) for this duration on a Knockdown outcome.")]
        [SerializeField] private float knockdownProneDuration = 1.5f;

        [Tooltip("Brief mutual stun applied to both players after a Collide outcome.")]
        [SerializeField] private float collideStunDuration = 0.4f;

        [Tooltip("Initial speed of the knockback impulse on a PushBack.")]
        [SerializeField] private float pushBackSpeed = 6f;

        [Tooltip("Duration of the PushBack knockback before velocity decays out (seconds).")]
        [SerializeField] private float pushBackDuration = 0.35f;

        [Tooltip("Initial speed of the bump impulse applied to the defender on a ChipBlock.")]
        [SerializeField] private float chipBlockBumpSpeed = 3f;

        [Tooltip("Duration of the ChipBlock bump (seconds).")]
        [SerializeField] private float chipBlockBumpDuration = 0.2f;

        [Tooltip("Distance the defender ends up past the blocker on a SwimThrough (world units).")]
        [SerializeField] private float swimThroughDistance = 1.2f;

        [Tooltip("Distance the defender slides off-axis on a SpinOff (world units).")]
        [SerializeField] private float spinOffDistance = 1.5f;

        [Tooltip("Search radius used to locate the ball carrier when resolving SpinOff / SwimThrough geometry.")]
        [SerializeField] private float carrierSearchRadius = 30f;

        [Header("=== SHOVE WEIGHTS: DEFENDER ===")]
        [SerializeField] private float defWeightHold        = 35f;
        [SerializeField] private float defWeightCollide     = 10f;
        [SerializeField] private float defWeightKnockdown   = 5f;
        [SerializeField] private float defWeightPushBack    = 25f;
        [SerializeField] private float defWeightSpinOff     = 15f;
        [SerializeField] private float defWeightSwimThrough = 10f;

        [Header("=== SHOVE WEIGHTS: BLOCKER ===")]
        [SerializeField] private float blkWeightHold      = 40f;
        [SerializeField] private float blkWeightCollide   = 10f;
        [SerializeField] private float blkWeightKnockdown = 5f;
        [SerializeField] private float blkWeightPushBack  = 25f;
        [SerializeField] private float blkWeightChipBlock = 20f;

        // ──────────────────────────────────────────────
        //  PASS CONFIG
        // ──────────────────────────────────────────────

        [Header("=== DEMOBALL: PASS ===")]
        [Tooltip("Maximum pass range (world units).")]
        [SerializeField] private float maxPassRange = 8f;

        [Header("=== DEMOBALL: PASS ARC ===")]
        [Tooltip("Peak height of a tap (pitch) pass — world units above ground.")]
        [SerializeField] private float pitchPeakHeight = 0.4f;

        [Tooltip("Peak height of a held (football) pass — world units above ground.")]
        [SerializeField] private float footballPeakHeight = 1.6f;

        [Tooltip("Travel duration of a tap (pitch) pass — seconds.")]
        [SerializeField] private float pitchDuration = 0.35f;

        [Tooltip("Travel duration of a held (football) pass — seconds.")]
        [SerializeField] private float footballDuration = 0.7f;

        // ──────────────────────────────────────────────
        //  RUNTIME STATE
        // ──────────────────────────────────────────────

        /// <summary>The ball this player is currently carrying, or null.</summary>
        public Ball HeldBall { get; private set; }

        public bool IsCarryingBall => HeldBall != null;

        /// <summary>
        /// True after being tackled. Player cannot pick up balls or score until they
        /// physically enter a TagUpZone. Movement is still allowed (to reach the zone).
        /// </summary>
        public bool NeedsTagUp { get; private set; }

        public bool IsInScoringRing { get; private set; }
        private ScoringRing currentScoringRing;

        private float stunTimer;

        /// <summary>
        /// Visual flag set by the carrier's input layer to mark this player as the
        /// currently selected pass target. Drives the green target ring on
        /// DemoballPlayerVisual.
        /// </summary>
        public bool IsPassTarget { get; set; }

        // Engagement state — set when this player is locked into a block / grapple.
        // Voluntary movement is suspended for both participants for the duration.
        public bool IsEngaged => engagedWith != null;
        public DemoballMovementController EngagedWith => engagedWith;
        public float EngagementTimeRemaining => engagementTimer;
        private DemoballMovementController engagedWith;
        private float engagementTimer;

        // Shove state — cooldown between attempts, prone window after a knockdown,
        // and a knockback impulse that overrides locomotion velocity for a moment.
        public bool IsProne => proneTimer > 0f;
        public bool CanShove =>
            IsEngaged
            && !IsCarryingBall
            && !NeedsTagUp
            && shoveCooldownTimer <= 0f
            && (role == DemoballRole.Defender || role == DemoballRole.Blocker);
        private float shoveCooldownTimer;
        private float proneTimer;
        private float knockbackTimer;
        private Vector2 knockbackVelocity;
        private float knockbackDecel;

        // ──────────────────────────────────────────────
        //  EVENTS
        // ──────────────────────────────────────────────

        public event Action<Ball>                       OnBallPickedUp;
        public event Action<Ball>                       OnBallDropped;
        /// <summary>Fired when this player successfully touches down. bool = was in bonus zone.</summary>
        public event Action<Ball, bool>                 OnTouchDown;
        public event Action<DemoballMovementController> OnTackled;
        public event Action                             OnTaggedUp;
        /// <summary>Fired when this player locks into an engagement. Argument = the other participant.</summary>
        public event Action<DemoballMovementController> OnEngagementStarted;
        /// <summary>Fired when this player's engagement ends. Argument = the (now ex-)opponent.</summary>
        public event Action<DemoballMovementController> OnEngagementEnded;
        /// <summary>Fired on the initiator after a shove resolves. Argument = the rolled outcome.</summary>
        public event Action<ShoveOutcome> OnShoveResolved;

        // ──────────────────────────────────────────────
        //  UNITY LIFECYCLE
        // ──────────────────────────────────────────────

        protected override void Update()
        {
            base.Update();
            TickStunTimer();
            TickEngagement();
            TickProneTimer();
            TickShoveCooldown();
        }

        protected override void FixedUpdate()
        {
            base.FixedUpdate();

            // Knockback impulse overrides whatever velocity the base movement
            // step just wrote. Decays linearly to zero over knockbackDuration.
            if (knockbackTimer > 0f)
            {
                knockbackTimer -= Time.fixedDeltaTime;
                rb.linearVelocity = knockbackVelocity;
                knockbackVelocity = Vector2.MoveTowards(
                    knockbackVelocity, Vector2.zero, knockbackDecel * Time.fixedDeltaTime);

                if (knockbackTimer <= 0f)
                    knockbackVelocity = Vector2.zero;
            }
        }

        // ──────────────────────────────────────────────
        //  ROLE ASSIGNMENT
        // ──────────────────────────────────────────────

        /// <summary>Called by DemoballGameManager at the start of each period.</summary>
        public void AssignRole(DemoballRole newRole)
        {
            role = newRole;
        }

        // ──────────────────────────────────────────────
        //  BALL PICKUP
        // ──────────────────────────────────────────────

        /// <summary>
        /// Attempts to pick up the ball. Enforces role-based pickup rules.
        /// Returns true if pickup succeeded (or ball was removed from play by a defender).
        /// </summary>
        public bool TryPickUpBall(Ball ball)
        {
            if (NeedsTagUp) return false;
            if (IsCarryingBall) return false;

            bool result = ball.PickUp(this);
            if (result && ball.State == Ball.BallState.Carried)
            {
                HeldBall = ball;
                OnBallPickedUp?.Invoke(ball);
            }
            return result;
        }

        // ──────────────────────────────────────────────
        //  PASS
        // ──────────────────────────────────────────────

        /// <summary>
        /// Passes the held ball to the best available teammate in range using a
        /// pitch-style arc. Used as a fallback when no explicit target was
        /// selected by the input layer.
        /// </summary>
        public bool TryPass()
        {
            if (!IsCarryingBall) return false;
            if (role == DemoballRole.Defender) return false;

            var target = FindPassTarget();
            if (target == null) return false;

            ExecutePass(target, 0f);
            return true;
        }

        /// <summary>
        /// Passes the held ball to a specific target (selected by the input layer
        /// via aim direction). `power` blends pitch (0) → football (1): a tap
        /// produces a low fast arc, a held button produces a high slower arc.
        /// </summary>
        public bool TryPass(DemoballMovementController target, float power = 0f)
        {
            if (!IsCarryingBall)              return false;
            if (role == DemoballRole.Defender) return false;
            if (target == null || target == this) return false;
            if (target.Role == DemoballRole.Defender) return false;
            if (target.NeedsTagUp || target.IsCarryingBall) return false;

            ExecutePass(target, Mathf.Clamp01(power));
            return true;
        }

        private DemoballMovementController FindPassTarget()
        {
            // TODO: improve to prefer targets in facing direction / open space
            var hits = Physics2D.OverlapCircleAll(transform.position, maxPassRange, playerLayer);
            DemoballMovementController best  = null;
            float                      bestD = float.MaxValue;

            foreach (var hit in hits)
            {
                var candidate = hit.GetComponent<DemoballMovementController>();
                if (candidate == null || candidate == this) continue;
                if (candidate.Role == DemoballRole.Defender) continue;
                if (candidate.NeedsTagUp) continue;
                if (candidate.IsCarryingBall) continue;

                float d = Vector2.Distance(transform.position, hit.transform.position);
                if (d < bestD) { bestD = d; best = candidate; }
            }
            return best;
        }

        private void ExecutePass(DemoballMovementController target, float power)
        {
            Ball ball = HeldBall;
            HeldBall = null;
            OnBallDropped?.Invoke(ball);

            float peakHeight = Mathf.Lerp(pitchPeakHeight, footballPeakHeight, power);
            float duration   = Mathf.Lerp(pitchDuration,   footballDuration,   power);

            // The ball owns the flight: it animates the arc and resolves the
            // catch on arrival (see Ball.ResolvePassArrival).
            ball.Pass(transform.position, target, peakHeight, duration);
        }

        /// <summary>Called on the receiver of a pass.</summary>
        public void ReceivePass(Ball ball)
        {
            ball.TransferTo(this);
            HeldBall = ball;
            OnBallPickedUp?.Invoke(ball);
        }

        // ──────────────────────────────────────────────
        //  TOUCH DOWN
        // ──────────────────────────────────────────────

        /// <summary>
        /// Attempts to touch the ball down for a score. Only valid when:
        ///   - carrying a ball
        ///   - physically inside the scoring ring
        ///   - not waiting to tag up
        /// Returns true if the touch-down was registered. Points are calculated by
        /// DemoballGameManager, which subscribes to the OnTouchDown event.
        /// </summary>
        public bool TryTouchDown()
        {
            if (!IsCarryingBall)   return false;
            if (!IsInScoringRing)  return false;
            if (NeedsTagUp)        return false;

            bool inBonus = currentScoringRing.IsInBonusZone(transform.position);
            Ball ball    = HeldBall;
            HeldBall     = null;

            ball.Score(inBonus);
            OnTouchDown?.Invoke(ball, inBonus);
            return true;
        }

        // ──────────────────────────────────────────────
        //  TACKLE
        // ──────────────────────────────────────────────

        /// <summary>
        /// Defender attempts to tackle the nearest ball-carrier in reach.
        /// Only Defenders may tackle. Returns true if a tackle connected.
        /// </summary>
        public bool TryTackle()
        {
            if (role != DemoballRole.Defender) return false;

            var hits = Physics2D.OverlapCircleAll(transform.position, tackleReach, playerLayer);
            foreach (var hit in hits)
            {
                var target = hit.GetComponent<DemoballMovementController>();
                if (target == null || target == this) continue;
                if (!target.IsCarryingBall) continue;
                if (target.Role == DemoballRole.Defender) continue;

                target.ReceiveTackle();
                return true;
            }
            return false;
        }

        /// <summary>Called on the player who was tackled. Drops the ball and sets the tag-up flag.</summary>
        public void ReceiveTackle()
        {
            if (IsCarryingBall)
            {
                Ball ball = HeldBall;
                HeldBall  = null;
                ball.Drop(transform.position);
                OnBallDropped?.Invoke(ball);
            }

            NeedsTagUp = true;
            stunTimer  = tackleStunsFor;
            SetState(MovementState.Stunned);
            OnTackled?.Invoke(this);
        }

        // ──────────────────────────────────────────────
        //  SCORING RING CALLBACKS  (called by ScoringRing trigger)
        // ──────────────────────────────────────────────

        public void EnterScoringRing(ScoringRing ring)
        {
            IsInScoringRing    = true;
            currentScoringRing = ring;
        }

        public void ExitScoringRing()
        {
            IsInScoringRing    = false;
            currentScoringRing = null;
        }

        // ──────────────────────────────────────────────
        //  TAG-UP  (called by TagUpZone trigger)
        // ──────────────────────────────────────────────

        /// <summary>Clears the NeedsTagUp flag and returns the player to active play.</summary>
        public void TagUp()
        {
            if (!NeedsTagUp) return;
            NeedsTagUp = false;
            SetState(MovementState.Idle);
            OnTaggedUp?.Invoke();
        }

        // ──────────────────────────────────────────────
        //  STUN TIMER
        // ──────────────────────────────────────────────

        private void TickStunTimer()
        {
            if (stunTimer <= 0f) return;
            stunTimer -= Time.deltaTime;
            if (stunTimer <= 0f && CurrentState == MovementState.Stunned && !IsEngaged)
            {
                // Stun expired — return to Idle. NeedsTagUp persists if set;
                // the player can now move toward the tag-up zone.
                SetState(MovementState.Idle);
            }
        }

        private void TickProneTimer()
        {
            if (proneTimer > 0f) proneTimer -= Time.deltaTime;
        }

        private void TickShoveCooldown()
        {
            if (shoveCooldownTimer > 0f) shoveCooldownTimer -= Time.deltaTime;
        }

        // ──────────────────────────────────────────────
        //  ENGAGEMENT  (blocker × defender lock)
        // ──────────────────────────────────────────────

        /// <summary>
        /// Attempts to lock this player and `other` into an engagement (e.g. a
        /// blocker grappling a defender). Both characters' voluntary movement
        /// is suspended via the Stunned state for `duration` seconds; pass a
        /// negative value to use the configured default.
        ///
        /// Future stat-driven logic will plug into the OnEngagement* events to
        /// determine actual duration, push direction, and knockdown outcomes.
        /// </summary>
        public bool TryStartEngagement(DemoballMovementController other, float duration = -1f)
        {
            if (other == null || other == this) return false;
            if (IsEngaged || other.IsEngaged)   return false;
            if (NeedsTagUp || other.NeedsTagUp) return false;

            float d = duration < 0f ? defaultEngagementDuration : duration;
            BeginEngagement(other, d);
            other.BeginEngagement(this, d);
            return true;
        }

        private void BeginEngagement(DemoballMovementController other, float duration)
        {
            engagedWith     = other;
            engagementTimer = duration;
            SetState(MovementState.Stunned);
            OnEngagementStarted?.Invoke(other);
        }

        /// <summary>
        /// Ends this engagement immediately. Safe to call on either participant —
        /// the partner is released as well. Called automatically when the timer
        /// expires; future code can also trigger early breakouts.
        /// </summary>
        public void EndEngagement()
        {
            if (!IsEngaged) return;

            var partner = engagedWith;
            engagedWith     = null;
            engagementTimer = 0f;

            // Don't override Stunned if the player is also recovering from a tackle
            if (CurrentState == MovementState.Stunned && !NeedsTagUp)
                SetState(MovementState.Idle);

            OnEngagementEnded?.Invoke(partner);

            if (partner != null && partner.IsEngaged)
                partner.EndEngagement();
        }

        private void TickEngagement()
        {
            if (!IsEngaged) return;
            engagementTimer -= Time.deltaTime;
            if (engagementTimer <= 0f)
                EndEngagement();
        }

        // ──────────────────────────────────────────────
        //  SHOVE  (engagement breakouts and counters)
        // ──────────────────────────────────────────────

        /// <summary>
        /// Attempts a shove against the currently-engaged partner. Rolls a
        /// role-specific outcome from the configured weight tables and applies
        /// its effects to both participants. Returns true if a shove was
        /// resolved; outcome receives the rolled result. Returns false if the
        /// player is not eligible to shove (not engaged, on cooldown, carrying,
        /// recovering from a tackle, or wrong role).
        /// </summary>
        public bool TryShove(out ShoveOutcome outcome)
        {
            outcome = ShoveOutcome.Hold;
            if (!CanShove) return false;

            var partner = engagedWith;
            if (partner == null) return false;

            outcome = RollShoveOutcome();
            ApplyShoveOutcome(outcome, partner);
            shoveCooldownTimer = shoveCooldown;
            OnShoveResolved?.Invoke(outcome);
            return true;
        }

        private ShoveOutcome RollShoveOutcome()
        {
            if (role == DemoballRole.Defender)
            {
                float total = defWeightHold + defWeightCollide + defWeightKnockdown
                            + defWeightPushBack + defWeightSpinOff + defWeightSwimThrough;
                float r = UnityEngine.Random.value * Mathf.Max(0.0001f, total);
                if ((r -= defWeightHold)        < 0f) return ShoveOutcome.Hold;
                if ((r -= defWeightCollide)     < 0f) return ShoveOutcome.Collide;
                if ((r -= defWeightKnockdown)   < 0f) return ShoveOutcome.Knockdown;
                if ((r -= defWeightPushBack)    < 0f) return ShoveOutcome.PushBack;
                if ((r -= defWeightSpinOff)     < 0f) return ShoveOutcome.SpinOff;
                return ShoveOutcome.SwimThrough;
            }
            else // Blocker
            {
                float total = blkWeightHold + blkWeightCollide + blkWeightKnockdown
                            + blkWeightPushBack + blkWeightChipBlock;
                float r = UnityEngine.Random.value * Mathf.Max(0.0001f, total);
                if ((r -= blkWeightHold)      < 0f) return ShoveOutcome.Hold;
                if ((r -= blkWeightCollide)   < 0f) return ShoveOutcome.Collide;
                if ((r -= blkWeightKnockdown) < 0f) return ShoveOutcome.Knockdown;
                if ((r -= blkWeightPushBack)  < 0f) return ShoveOutcome.PushBack;
                return ShoveOutcome.ChipBlock;
            }
        }

        private void ApplyShoveOutcome(ShoveOutcome outcome, DemoballMovementController partner)
        {
            Vector2 selfPos    = transform.position;
            Vector2 partnerPos = partner.transform.position;
            // Axis points away from the initiator (toward the partner). Used as
            // the push direction for impulses and the line for SwimThrough.
            Vector2 axis = (partnerPos - selfPos);
            if (axis.sqrMagnitude < 0.0001f) axis = GetFacingDirection();
            axis.Normalize();

            switch (outcome)
            {
                case ShoveOutcome.Hold:
                    // Failed shove. Engagement continues, cooldown applied.
                    break;

                case ShoveOutcome.Collide:
                    EndEngagement();
                    ApplyStun(collideStunDuration);
                    partner.ApplyStun(collideStunDuration);
                    break;

                case ShoveOutcome.Knockdown:
                    EndEngagement();
                    partner.ApplyProne(knockdownProneDuration);
                    break;

                case ShoveOutcome.PushBack:
                    EndEngagement();
                    partner.ApplyKnockback(axis, pushBackSpeed, pushBackDuration);
                    partner.ApplyStun(pushBackDuration);
                    break;

                case ShoveOutcome.SpinOff:
                {
                    Vector2 carrierPos = FindCarrierPosition();
                    int side = ChooseSideTowardCarrier(selfPos, axis, carrierPos);
                    Vector2 perp = Vector2.Perpendicular(axis) * side;
                    EndEngagement();
                    transform.position = selfPos + perp * spinOffDistance;
                    break;
                }

                case ShoveOutcome.SwimThrough:
                {
                    Vector2 carrierPos = FindCarrierPosition();
                    Vector2 toCarrier  = carrierPos - partnerPos;
                    Vector2 swimDir    = toCarrier.sqrMagnitude > 0.01f
                        ? toCarrier.normalized
                        : axis;
                    EndEngagement();
                    transform.position = partnerPos + swimDir * swimThroughDistance;
                    break;
                }

                case ShoveOutcome.ChipBlock:
                    EndEngagement();
                    partner.ApplyKnockback(axis, chipBlockBumpSpeed, chipBlockBumpDuration);
                    partner.ApplyStun(chipBlockBumpDuration);
                    break;
            }
        }

        /// <summary>Brief stun. Sets the Stunned state until stunTimer expires.</summary>
        public void ApplyStun(float duration)
        {
            if (duration <= 0f) return;
            stunTimer = Mathf.Max(stunTimer, duration);
            if (CurrentState != MovementState.Stunned)
                SetState(MovementState.Stunned);
        }

        /// <summary>Extended stun (knockdown). Sets IsProne for the duration.</summary>
        public void ApplyProne(float duration)
        {
            if (duration <= 0f) return;
            proneTimer = Mathf.Max(proneTimer, duration);
            ApplyStun(duration);
        }

        /// <summary>
        /// Applies a velocity impulse in `direction` that decays linearly to
        /// zero over `duration`. Used for PushBack and ChipBlock outcomes.
        /// Overrides locomotion velocity each FixedUpdate while active.
        /// </summary>
        public void ApplyKnockback(Vector2 direction, float speed, float duration)
        {
            if (direction.sqrMagnitude < 0.0001f || speed <= 0f || duration <= 0f) return;
            knockbackVelocity = direction.normalized * speed;
            knockbackTimer    = duration;
            knockbackDecel    = speed / duration;
        }

        private Vector2 FindCarrierPosition()
        {
            var hits = Physics2D.OverlapCircleAll(transform.position, carrierSearchRadius, playerLayer);
            foreach (var hit in hits)
            {
                var p = hit.GetComponent<DemoballMovementController>();
                if (p != null && p != this && p.IsCarryingBall) return p.transform.position;
            }
            // No carrier found — fall back to the partner's position so SpinOff /
            // SwimThrough degrade gracefully (axis-aligned movement).
            return engagedWith != null ? (Vector2)engagedWith.transform.position : (Vector2)transform.position;
        }

        // Returns +1 or -1: the perpendicular sign that lands the defender
        // closer to the ball carrier (relative to the engagement axis).
        private int ChooseSideTowardCarrier(Vector2 selfPos, Vector2 axis, Vector2 carrierPos)
        {
            Vector2 perp = Vector2.Perpendicular(axis);
            Vector2 destA = selfPos + perp * spinOffDistance;
            Vector2 destB = selfPos - perp * spinOffDistance;
            return (destA - carrierPos).sqrMagnitude < (destB - carrierPos).sqrMagnitude ? 1 : -1;
        }

        // ──────────────────────────────────────────────
        //  SPEED OVERRIDE  (carry penalty)
        // ──────────────────────────────────────────────

        public override float GetEffectiveTopSpeed()
        {
            float speed = base.GetEffectiveTopSpeed();
            if (IsCarryingBall) speed *= carrySpeedMultiplier;
            speed *= debugSpeedMultiplier;
            return speed;
        }

        // ──────────────────────────────────────────────
        //  PERIOD RESET
        // ──────────────────────────────────────────────

        /// <summary>Called by DemoballGameManager between periods to clear transient state.</summary>
        public void ResetForNewPeriod()
        {
            if (IsCarryingBall)
            {
                HeldBall.Drop(transform.position);
                HeldBall = null;
            }
            NeedsTagUp         = false;
            IsInScoringRing    = false;
            currentScoringRing = null;
            stunTimer          = 0f;
            proneTimer         = 0f;
            shoveCooldownTimer = 0f;
            knockbackTimer     = 0f;
            knockbackVelocity  = Vector2.zero;
            if (IsEngaged) EndEngagement();
            SetState(MovementState.Idle);
        }
    }
}
