using UnityEngine;

namespace Sportland.Sports.Demoball
{
    /// <summary>
    /// The center-field cannon that fires balls into play on command from DemoballGameManager.
    ///
    /// Maintains a pool of Ball objects. On Fire(), it selects the next Dormant ball,
    /// picks a slightly randomised landing position near the cannon, and calls Ball.Launch().
    /// The random scatter ensures the sprint to first possession isn't predetermined.
    ///
    /// Setup: place at center of the circular field. Assign all Ball GameObjects to ballPool.
    /// </summary>
    public class BallCannon : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        //  CONFIGURATION
        // ──────────────────────────────────────────────

        [Tooltip("All balls managed by this cannon. Assign in Inspector. " +
                 "Balls should start Dormant and be recycled between periods.")]
        [SerializeField] private Ball[] ballPool;

        [Tooltip("Maximum random scatter radius (world units) for ball landing position. " +
                 "0 = balls always land dead centre.")]
        [SerializeField] private float landingScatterRadius = 1.8f;

        // ──────────────────────────────────────────────
        //  PUBLIC API
        // ──────────────────────────────────────────────

        /// <summary>
        /// Fires the next available Dormant ball toward a random position near centre.
        /// Returns the ball that was fired, or null if the pool is exhausted.
        /// </summary>
        public Ball Fire()
        {
            Ball ball = GetDormantBall();
            if (ball == null)
            {
                Debug.LogWarning("[BallCannon] No dormant balls available to fire.");
                return null;
            }

            Vector3 landPosition = PickLandingPosition();
            ball.Launch(transform.position, landPosition);

            // TODO: play cannon fire SFX / VFX here
            return ball;
        }

        /// <summary>Resets all balls to Dormant. Call between periods.</summary>
        public void ResetPool()
        {
            foreach (var b in ballPool)
                b.ResetToDormant();
        }

        /// <summary>How many balls in the pool are currently Dormant.</summary>
        public int DormantCount
        {
            get
            {
                int n = 0;
                foreach (var b in ballPool)
                    if (b.State == Ball.BallState.Dormant) n++;
                return n;
            }
        }

        // ──────────────────────────────────────────────
        //  HELPERS
        // ──────────────────────────────────────────────

        private Ball GetDormantBall()
        {
            foreach (var b in ballPool)
                if (b.State == Ball.BallState.Dormant) return b;
            return null;
        }

        private Vector3 PickLandingPosition()
        {
            Vector2 offset = Random.insideUnitCircle * landingScatterRadius;
            return transform.position + new Vector3(offset.x, offset.y, 0f);
        }

        // ──────────────────────────────────────────────
        //  GIZMOS
        // ──────────────────────────────────────────────

        private void OnDrawGizmos()
        {
            // Cannon body
            Gizmos.color = new Color(0.8f, 0.4f, 0f, 1f);
            Gizmos.DrawWireSphere(transform.position, 0.3f);

            // Landing scatter zone
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.25f);
            Gizmos.DrawSphere(transform.position, landingScatterRadius);
        }
    }
}
