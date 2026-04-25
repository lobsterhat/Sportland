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
