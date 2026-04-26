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

        // ──────────────────────────────────────────────
        //  RUNTIME
        // ──────────────────────────────────────────────

        private DemoballMovementController self;

        private void Awake()
        {
            self = GetComponent<DemoballMovementController>();
        }

        private void Update()
        {
            // While engaged or recovering from a tackle, hold still — the
            // engagement / tag-up systems own the character's state.
            if (self.IsEngaged || self.NeedsTagUp)
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
                self.SetMoveInput(toTarget / dist);
                self.SetSprinting(dist > sprintDistance);
            }
            else
            {
                self.SetMoveInput(Vector2.zero);
                self.SetSprinting(false);
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
                self.SetMoveInput(away);
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
                self.SetMoveInput(toBall / dist);
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
            DemoballMovementController carrier = null;
            DemoballMovementController nearest = null;
            float nearestDist = float.MaxValue;

            foreach (var o in opponents)
            {
                if (o == null) continue;
                if (o.IsEngaged || o.NeedsTagUp) continue;

                if (o.IsCarryingBall && carrier == null) carrier = o;

                float d = Vector2.Distance(transform.position, o.transform.position);
                if (d < nearestDist) { nearestDist = d; nearest = o; }
            }

            return carrier != null ? carrier : nearest;
        }
    }
}
