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

        [Tooltip("Within this distance of the flank target, the AI stops moving.")]
        [SerializeField] private float arriveDistance = 0.15f;

        [Tooltip("If farther than this from the flank target, the AI sprints to catch up.")]
        [SerializeField] private float sprintDistance = 4f;

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
            if (leader == null)
            {
                self.SetMoveInput(Vector2.zero);
                self.SetSprinting(false);
                return;
            }

            Vector2 leaderFacing = leader.GetFacingDirection();
            // Vector2.Perpendicular rotates 90° CCW — that's the leader's LEFT.
            // Multiplying by -side gives:  side = -1 → leader's left, side = +1 → leader's right.
            Vector2 lateral = -side * Vector2.Perpendicular(leaderFacing);

            Vector2 leaderPos = leader.transform.position;
            Vector2 target    = leaderPos + lateral * flankOffset - leaderFacing * trailDistance;

            Vector2 toTarget = target - (Vector2)transform.position;
            float dist = toTarget.magnitude;

            if (dist < arriveDistance)
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
    }
}
