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
        //  PASS CONFIG
        // ──────────────────────────────────────────────

        [Header("=== DEMOBALL: PASS ===")]
        [Tooltip("Maximum pass range (world units).")]
        [SerializeField] private float maxPassRange = 8f;

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

        // ──────────────────────────────────────────────
        //  EVENTS
        // ──────────────────────────────────────────────

        public event Action<Ball>                       OnBallPickedUp;
        public event Action<Ball>                       OnBallDropped;
        /// <summary>Fired when this player successfully touches down. bool = was in bonus zone.</summary>
        public event Action<Ball, bool>                 OnTouchDown;
        public event Action<DemoballMovementController> OnTackled;
        public event Action                             OnTaggedUp;

        // ──────────────────────────────────────────────
        //  UNITY LIFECYCLE
        // ──────────────────────────────────────────────

        protected override void Update()
        {
            base.Update();
            TickStunTimer();
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
        /// Passes the held ball to the best available teammate in range.
        /// Scorers and Blockers carrying the ball may pass; Defenders may not.
        /// Returns true if a pass was made.
        /// </summary>
        public bool TryPass()
        {
            if (!IsCarryingBall) return false;
            if (role == DemoballRole.Defender) return false;

            var target = FindPassTarget();
            if (target == null) return false;

            ExecutePass(target);
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

        private void ExecutePass(DemoballMovementController target)
        {
            Ball ball = HeldBall;
            HeldBall = null;
            OnBallDropped?.Invoke(ball);

            // TODO: animate ball arc + interception window before confirming receipt
            target.ReceivePass(ball);
        }

        /// <summary>Called on the receiver of a pass.</summary>
        public void ReceivePass(Ball ball)
        {
            ball.PickUp(this);
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
        //  SPEED OVERRIDE  (carry penalty)
        // ──────────────────────────────────────────────

        public override float GetEffectiveTopSpeed()
        {
            float speed = base.GetEffectiveTopSpeed();
            if (IsCarryingBall) speed *= carrySpeedMultiplier;
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
            SetState(MovementState.Idle);
        }
    }
}
