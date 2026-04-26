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

        // ──────────────────────────────────────────────
        //  UNITY LIFECYCLE
        // ──────────────────────────────────────────────

        protected override void Update()
        {
            base.Update();
            TickStunTimer();
            TickEngagement();
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
            if (stunTimer <= 0f && CurrentState == MovementState.Stunned && NeedsTagUp)
            {
                // Stun expired — player can now move toward the tag-up zone
                SetState(MovementState.Idle);
            }
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
            if (IsEngaged) EndEngagement();
            SetState(MovementState.Idle);
        }
    }
}
