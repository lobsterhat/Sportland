using UnityEngine;

namespace Sportland.Sports.Demoball
{
    /// <summary>
    /// Simple flanking AI for a CPU Demoball player. Each frame it computes a
    /// target position to one side of a designated leader (relative to the
    /// leader's current facing) and steers the movement controller toward it.
    ///
    /// Designed to sit alongside a DemoballMovementController and
    /// DemoballInputBroker (with playerControlled = false) so the broker
    /// doesn't fight us for the move input.
    /// </summary>
    [RequireComponent(typeof(DemoballMovementController))]
    public class DemoballFlankAi : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        //  CONFIGURATION
        // ──────────────────────────────────────────────

        [Tooltip("The player to flank. Usually the human-controlled scorer.")]
        [SerializeField] private DemoballMovementController leader;

        [Tooltip("Lateral offset from the leader (world units), perpendicular to leader facing.")]
        [SerializeField] private float flankOffset = 1.8f;

        [Tooltip("How far behind the leader to sit, along leader facing. 0 = level with leader.")]
        [SerializeField] private float trailDistance = 0f;

        [Tooltip("Which side of the leader to flank. -1 = leader's left, +1 = leader's right.")]
        [SerializeField] private float side = -1f;

        [Tooltip("Tolerance (world units) around the stopping point. The AI stops commanding " +
                 "movement this much before the target so the controller's deceleration lands it cleanly.")]
        [SerializeField] private float arriveTolerance = 0.15f;

        [Tooltip("If farther than this from the flank target, the AI sprints to catch up.")]
        [SerializeField] private float sprintDistance = 4f;

        [Tooltip("Degrees per second the flank basis rotates toward the leader's facing. " +
                 "Low values make flankers orbit smoothly instead of crisscrossing when the leader spins around.")]
        [SerializeField] private float basisTurnRate = 120f;

        // ──────────────────────────────────────────────
        //  RUNTIME
        // ──────────────────────────────────────────────

        private DemoballMovementController self;
        private Vector2 flankBasis = Vector2.right;

        private void Awake()
        {
            self = GetComponent<DemoballMovementController>();
        }

        private void Start()
        {
            if (leader != null)
                flankBasis = leader.GetFacingDirection();
        }

        private void Update()
        {
            if (leader == null)
            {
                self.SetMoveInput(Vector2.zero);
                self.SetSprinting(false);
                return;
            }

            // Smooth-rotate our local basis toward the leader's facing so a sudden
            // 180° turn makes the flanker orbit around the leader instead of
            // teleporting its target to the opposite side.
            Vector2 leaderFacing = leader.GetFacingDirection();
            flankBasis = RotateToward(flankBasis, leaderFacing, basisTurnRate * Time.deltaTime);

            // Vector2.Perpendicular rotates 90° CCW — that's the leader's LEFT.
            // -side * Perpendicular(basis):  side = -1 → leader's left, side = +1 → leader's right.
            Vector2 lateral = -side * Vector2.Perpendicular(flankBasis);

            Vector2 leaderPos = leader.transform.position;
            Vector2 target    = leaderPos + lateral * flankOffset - flankBasis * trailDistance;

            Vector2 toTarget = target - (Vector2)transform.position;
            float dist = toTarget.magnitude;

            // Release the input early enough that the controller's natural
            // deceleration carries us onto the target rather than overshooting,
            // pivoting, and homing back in.
            float speed        = self.GetCurrentSpeed();
            float decel        = self.Profile != null ? self.Profile.deceleration : 20f;
            float coastDistance = (speed * speed) / (2f * Mathf.Max(decel, 0.01f));

            if (dist <= coastDistance + arriveTolerance)
            {
                self.SetMoveInput(Vector2.zero);
                self.SetSprinting(false);
            }
            else
            {
                self.SetMoveInput(toTarget / dist);
                self.SetSprinting(dist > sprintDistance);
            }
        }

        private static Vector2 RotateToward(Vector2 from, Vector2 to, float maxDegrees)
        {
            if (from.sqrMagnitude < 1e-6f) return to.normalized;
            if (to.sqrMagnitude   < 1e-6f) return from;

            float angle = Vector2.SignedAngle(from, to);
            float step  = Mathf.Clamp(angle, -maxDegrees, maxDegrees);
            return (Quaternion.Euler(0f, 0f, step) * from).normalized;
        }
    }
}
