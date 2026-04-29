using System.Collections.Generic;
using UnityEngine;

namespace Sportland.Sports.Demoball
{
    /// <summary>
    /// Pursuit AI for a CPU defender. Each frame it picks the highest-priority
    /// opponent (current ball-carrier first, otherwise the nearest opponent
    /// who is not already engaged) and chases them, calling TryTackle when
    /// in range of a carrier.
    ///
    /// Idles while engaged — DemoballMovementController.IsEngaged drives
    /// the lock; the engagement system controls movement during that window.
    /// </summary>
    [RequireComponent(typeof(DemoballMovementController))]
    public class DemoballDefenderAi : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        //  CONFIGURATION
        // ──────────────────────────────────────────────

        [Tooltip("Players this defender will pursue. Carriers among them get top priority.")]
        [SerializeField] private List<DemoballMovementController> opponents;

        [Tooltip("World-space distance at which a tackle attempt is fired against a carrier.")]
        [SerializeField] private float tackleRange = 1.4f;

        [Tooltip("If farther than this from the target, the defender sprints to close the gap.")]
        [SerializeField] private float sprintDistance = 4f;

        [Tooltip("Pickup radius — when this close to a loose ball the defender attempts to recover it.")]
        [SerializeField] private float ballPickupRadius = 1.0f;

        [Tooltip("Maximum range at which a loose ball outranks an opposing player as a target.")]
        [SerializeField] private float looseBallChaseRange = 16f;

        [Header("=== FRESH BALL EXCLUSION ===")]
        [Tooltip("During the offense's setup window, defenders must stay at least this far from the fresh ball.")]
        [SerializeField] private float freshBallExclusionRadius = 4f;

        [Tooltip("How far the defender will look for a fresh ball to back away from (default covers the arena).")]
        [SerializeField] private float freshBallSearchRange = 24f;

        [Header("=== TARGETING ===")]
        [Tooltip("Effective-distance bias for the ball-carrier. < 1 means the carrier looks closer than they really " +
                 "are, so defenders favour rushing them but will peel off to cover a clearly closer receiver.")]
        [SerializeField] private float carrierDistanceBias = 0.85f;

        [Header("=== SEPARATION ===")]
        [Tooltip("Personal-space radius. Pushes away from same-team players within this distance to prevent bunching.")]
        [SerializeField] private float separationRadius = 1.4f;

        [Tooltip("How strongly the separation vector blends into the desired chase direction.")]
        [SerializeField] private float separationWeight = 1.2f;

        [Header("=== SHOVE AI ===")]
        [Tooltip("Minimum delay between shove attempts while engaged (seconds).")]
        [SerializeField] private float shoveAttemptMin = 0.4f;

        [Tooltip("Maximum delay between shove attempts while engaged (seconds).")]
        [SerializeField] private float shoveAttemptMax = 1.2f;

        // ──────────────────────────────────────────────
        //  RUNTIME
        // ──────────────────────────────────────────────

        private DemoballMovementController self;
        private float shoveAttemptTimer;

        private void Awake()
        {
            self = GetComponent<DemoballMovementController>();
        }

        private void Update()
        {
            // Engaged: idle, but periodically attempt a shove to break the
            // hold — the engagement system suspends voluntary movement either way.
            if (self.IsEngaged)
            {
                self.SetMoveInput(Vector2.zero);
                self.SetSprinting(false);
                TickAiShove();
                return;
            }

            // Reset shove cadence so the next engagement starts with a fresh delay.
            shoveAttemptTimer = Random.Range(shoveAttemptMin, shoveAttemptMax);

            if (self.NeedsTagUp)
            {
                self.SetMoveInput(Vector2.zero);
                self.SetSprinting(false);
                return;
            }

            // Fresh-ball exclusion takes priority over everything else: while
            // the offense's setup window is active the defender must hold
            // exclusionRadius from the ball. Push away if too close, otherwise
            // idle in place (don't pursue offense yet either, that's the rule).
            Ball fresh = FindFreshBall();
            if (fresh != null) { HoldExclusion(fresh); return; }

            // Loose ball recovery takes priority when no opponent is carrying:
            // intercepting a ball before it gets picked back up is huge for the
            // defense, since a defender pickup removes the ball from play.
            if (!AnyOpponentCarrying())
            {
                Ball loose = FindClosestLooseBall();
                if (loose != null) { ChaseLooseBall(loose); return; }
            }

            var target = FindTarget();
            if (target == null)
            {
                self.SetMoveInput(Vector2.zero);
                self.SetSprinting(false);
                return;
            }

            Vector2 toTarget = (Vector2)target.transform.position - (Vector2)transform.position;
            float dist = toTarget.magnitude;

            if (target.IsCarryingBall && dist <= tackleRange)
                self.TryTackle();

            if (dist > 0.05f)
            {
                self.SetMoveInput(BlendSeparation(toTarget / dist));
                self.SetSprinting(dist > sprintDistance);
            }
            else
            {
                self.SetMoveInput(Vector2.zero);
                self.SetSprinting(false);
            }
        }

        // Adds a teammate-separation steering vector to the desired direction.
        // Same-team players within separationRadius push us away with a force
        // that scales linearly with how close they are.
        private Vector2 BlendSeparation(Vector2 desired)
        {
            Vector2 sep = ComputeTeammateSeparation();
            if (sep.sqrMagnitude < 0.0001f) return desired;
            Vector2 blended = desired + sep * separationWeight;
            return blended.sqrMagnitude > 0.0001f ? blended.normalized : desired;
        }

        private Vector2 ComputeTeammateSeparation()
        {
            if (separationRadius <= 0f) return Vector2.zero;
            var hits = Physics2D.OverlapCircleAll(transform.position, separationRadius);
            Vector2 sum = Vector2.zero;
            foreach (var hit in hits)
            {
                var t = hit.GetComponent<DemoballMovementController>();
                if (t == null || t == self) continue;
                if (!IsSameTeam(t)) continue;

                Vector2 away = (Vector2)transform.position - (Vector2)t.transform.position;
                float d = away.magnitude;
                if (d > 0.01f && d < separationRadius)
                    sum += (away / d) * (1f - d / separationRadius);
            }
            return sum;
        }

        private bool IsSameTeam(DemoballMovementController other)
        {
            // Defender vs non-defender splits the two sides of the ball.
            bool selfIsDefense  = self.Role  == DemoballRole.Defender;
            bool otherIsDefense = other.Role == DemoballRole.Defender;
            return selfIsDefense == otherIsDefense;
        }

        private void TickAiShove()
        {
            if (!self.CanShove) return;
            shoveAttemptTimer -= Time.deltaTime;
            if (shoveAttemptTimer <= 0f)
            {
                self.TryShove(out _);
                shoveAttemptTimer = Random.Range(shoveAttemptMin, shoveAttemptMax);
            }
        }

        private bool AnyOpponentCarrying()
        {
            if (opponents == null) return false;
            foreach (var o in opponents)
                if (o != null && o.IsCarryingBall) return true;
            return false;
        }

        private Ball FindClosestLooseBall()
        {
            // Single OverlapCircleAll is cheap enough at this scale and avoids
            // tracking ball references on the AI itself.
            var hits = Physics2D.OverlapCircleAll(transform.position, looseBallChaseRange);
            Ball best = null;
            float bestSqr = float.MaxValue;
            foreach (var hit in hits)
            {
                var ball = hit.GetComponent<Ball>();
                if (ball == null) continue;
                if (ball.State != Ball.BallState.Loose) continue;

                float sqr = ((Vector2)ball.transform.position - (Vector2)transform.position).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = ball; }
            }
            return best;
        }

        private Ball FindFreshBall()
        {
            var hits = Physics2D.OverlapCircleAll(transform.position, freshBallSearchRange);
            Ball best = null;
            float bestSqr = float.MaxValue;
            foreach (var hit in hits)
            {
                var ball = hit.GetComponent<Ball>();
                if (ball == null || !ball.IsFresh) continue;

                float sqr = ((Vector2)ball.transform.position - (Vector2)transform.position).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = ball; }
            }
            return best;
        }

        private void HoldExclusion(Ball ball)
        {
            Vector2 toBall = (Vector2)ball.transform.position - (Vector2)transform.position;
            float dist = toBall.magnitude;

            if (dist < freshBallExclusionRadius)
            {
                Vector2 away = dist > 0.01f ? -toBall / dist : Vector2.right;
                self.SetMoveInput(BlendSeparation(away));
                self.SetSprinting(false);
            }
            else
            {
                self.SetMoveInput(Vector2.zero);
                self.SetSprinting(false);
            }
        }

        private void ChaseLooseBall(Ball ball)
        {
            Vector2 toBall = (Vector2)ball.transform.position - (Vector2)transform.position;
            float dist = toBall.magnitude;

            if (dist <= ballPickupRadius)
            {
                self.TryPickUpBall(ball);   // defender pickup → ball goes OutOfPlay
            }

            if (dist > 0.05f)
            {
                self.SetMoveInput(BlendSeparation(toBall / dist));
                self.SetSprinting(dist > sprintDistance);
            }
            else
            {
                self.SetMoveInput(Vector2.zero);
                self.SetSprinting(false);
            }
        }

        private DemoballMovementController FindTarget()
        {
            // Pick the opponent with the lowest "effective distance" — the
            // carrier is biased to look closer than they really are, so a
            // defender naturally peels off to cover a route receiver only
            // when that receiver is meaningfully closer than the carrier.
            DemoballMovementController best = null;
            float bestEff = float.MaxValue;

            foreach (var o in opponents)
            {
                if (o == null) continue;
                if (o.IsEngaged || o.NeedsTagUp) continue;

                float d   = Vector2.Distance(transform.position, o.transform.position);
                float eff = o.IsCarryingBall ? d * carrierDistanceBias : d;
                if (eff < bestEff) { bestEff = eff; best = o; }
            }

            return best;
        }
    }
}
