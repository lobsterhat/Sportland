using System.Collections.Generic;
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

        [Tooltip("Degrees per second the flank basis rotates toward the leader's facing for gentle turns. " +
                 "For turns larger than sideSwapAngle the flanker just swaps sides instead.")]
        [SerializeField] private float basisTurnRate = 120f;

        [Tooltip("When the angle between the flanker's basis and the leader's facing exceeds this, " +
                 "the flanker swaps sides (snap to new basis) rather than orbiting around. " +
                 "90° is the break-even point where swapping is shorter than orbiting.")]
        [SerializeField] private float sideSwapAngle = 90f;

        [Header("=== BLOCKING ===")]
        [Tooltip("Defenders this blocker should intercept when they threaten the leader.")]
        [SerializeField] private List<DemoballMovementController> opponents;

        [Tooltip("Maximum distance (from the leader) at which a defender is treated as a threat to intercept.")]
        [SerializeField] private float threatRange = 6f;

        [Tooltip("Where the blocker plants relative to the leader along the threat line — " +
                 "world units from the leader, between leader and the threat.")]
        [SerializeField] private float blockStandoff = 1.0f;

        [Tooltip("Distance to the threat at which the blocker initiates an engagement.")]
        [SerializeField] private float engageDistance = 0.7f;

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
            // Engagement system owns movement while we're locked up.
            if (self.IsEngaged) return;

            if (leader == null)
            {
                self.SetMoveInput(Vector2.zero);
                self.SetSprinting(false);
                return;
            }

            // For gentle turns, rotate our local basis toward the leader so the
            // flanker orbits to stay on their side. For a large turn it's
            // shorter for the flanker to swap sides than to orbit, so snap the
            // basis and flip the side flag.
            Vector2 leaderFacing = leader.GetFacingDirection();
            if (Vector2.Angle(flankBasis, leaderFacing) > sideSwapAngle)
            {
                side = -side;
                flankBasis = leaderFacing;
            }
            else
            {
                flankBasis = RotateToward(flankBasis, leaderFacing, basisTurnRate * Time.deltaTime);
            }

            Vector2 leaderPos = leader.transform.position;
            Vector2 target;

            // If a defender is threatening the leader, abandon the flank slot
            // and step in to block. Otherwise hold the flank position.
            DemoballMovementController threat = FindThreat(leaderPos);
            if (threat != null)
            {
                Vector2 threatPos = threat.transform.position;

                // If we're close enough to the threat, lock into an engagement.
                if (Vector2.Distance(transform.position, threatPos) <= engageDistance
                    && self.TryStartEngagement(threat))
                {
                    self.SetMoveInput(Vector2.zero);
                    self.SetSprinting(false);
                    return;
                }

                // Plant on the line between leader and threat, blockStandoff
                // units from the leader.
                Vector2 toThreat = (threatPos - leaderPos);
                if (toThreat.sqrMagnitude > 0.0001f)
                    target = leaderPos + toThreat.normalized * blockStandoff;
                else
                    target = leaderPos;
            }
            else
            {
                // Vector2.Perpendicular rotates 90° CCW — that's the leader's LEFT.
                // -side * Perpendicular(basis):  side = -1 → leader's left, side = +1 → leader's right.
                Vector2 lateral = -side * Vector2.Perpendicular(flankBasis);
                target = leaderPos + lateral * flankOffset - flankBasis * trailDistance;
            }

            Vector2 toTarget = target - (Vector2)transform.position;
            float dist = toTarget.magnitude;

            // Release the input early enough that the controller's natural
            // deceleration carries us onto the target rather than overshooting,
            // pivoting, and homing back in.
            float speed         = self.GetCurrentSpeed();
            float decel         = self.Profile != null ? self.Profile.deceleration : 20f;
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

        private DemoballMovementController FindThreat(Vector2 leaderPos)
        {
            if (opponents == null) return null;

            DemoballMovementController best = null;
            float bestDist = threatRange;

            foreach (var o in opponents)
            {
                if (o == null) continue;
                if (o.IsEngaged || o.NeedsTagUp) continue;

                float d = Vector2.Distance(o.transform.position, leaderPos);
                if (d < bestDist) { bestDist = d; best = o; }
            }
            return best;
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
