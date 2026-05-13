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

        [Tooltip("Possible leaders. Each frame the flanker follows whichever teammate is " +
                 "currently player-controlled, so a pass and control swap retargets the " +
                 "flank automatically. Self may be included; it is filtered at runtime.")]
        [SerializeField] private List<DemoballMovementController> teammates;

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

        [Header("=== SEPARATION ===")]
        [Tooltip("Personal-space radius. Pushes away from same-team players within this distance to prevent bunching.")]
        [SerializeField] private float separationRadius = 1.4f;

        [Tooltip("How strongly the separation vector blends into the desired direction.")]
        [SerializeField] private float separationWeight = 1.2f;

        [Header("=== SHOVE AI ===")]
        [Tooltip("Minimum delay between shove attempts while engaged (seconds).")]
        [SerializeField] private float shoveAttemptMin = 0.4f;

        [Tooltip("Maximum delay between shove attempts while engaged (seconds).")]
        [SerializeField] private float shoveAttemptMax = 1.2f;

        // ──────────────────────────────────────────────
        //  PLAY MODES
        // ──────────────────────────────────────────────

        public enum AiMode { Flank, RunRoute }
        public AiMode CurrentMode => mode;

        private AiMode mode = AiMode.Flank;
        private Vector2 routeDestination;   // world-space target while running a route
        private float routeTimer;

        /// <summary>
        /// Switches this blocker into a fixed-destination route until the
        /// timer expires or the leader changes (e.g. via a pass + control
        /// swap). The blocker sprints to `worldDestination` and holds.
        /// </summary>
        public void RunRoute(Vector2 worldDestination, float duration)
        {
            mode = AiMode.RunRoute;
            routeDestination = worldDestination;
            routeTimer = Mathf.Max(0.05f, duration);
        }

        /// <summary>Cancel any active route and revert to flank behaviour.</summary>
        public void EndRoute()
        {
            mode = AiMode.Flank;
            routeTimer = 0f;
        }

        // ──────────────────────────────────────────────
        //  RUNTIME
        // ──────────────────────────────────────────────

        private DemoballMovementController self;
        private DemoballInputBroker selfBroker;
        private DemoballMovementController leader;
        private Vector2 flankBasis = Vector2.right;
        private float shoveAttemptTimer;

        private void Awake()
        {
            self       = GetComponent<DemoballMovementController>();
            selfBroker = GetComponent<DemoballInputBroker>();
        }

        private void Update()
        {
            // Engagement: voluntary movement is suspended, but the AI still
            // ticks shove attempts to try and break or convert the hold.
            // Skipped when the player is driving this character — their input
            // broker handles shoves directly.
            if (self.IsEngaged)
            {
                if (selfBroker == null || !selfBroker.IsPlayerControlled)
                    TickAiShove();
                return;
            }

            // Reset shove cadence so the next engagement starts with a fresh delay.
            shoveAttemptTimer = Random.Range(shoveAttemptMin, shoveAttemptMax);

            // Defer to player input on this character.
            if (selfBroker != null && selfBroker.IsPlayerControlled) return;

            // Dead-ball setup: walk to the assigned formation slot.
            if (self.HasSetupTarget && !DemoballGameManager.IsPlayLive)
            {
                SeekSetupTarget();
                return;
            }

            DemoballMovementController newLeader = ResolveLeader();
            if (newLeader != leader)
            {
                leader = newLeader;
                if (leader != null) flankBasis = leader.GetFacingDirection();
                // A control swap (typically a pass) ends the play for this AI.
                EndRoute();
            }

            if (leader == null)
            {
                self.SetMoveInput(Vector2.zero);
                self.SetSprinting(false);
                return;
            }

            // Route mode: head to a fixed spot for `routeTimer` seconds, then
            // fall back to flank behaviour. Threats and engagements still take
            // priority — they handle the "blocker tries to catch but gets hit"
            // scenario without us re-implementing it here.
            if (mode == AiMode.RunRoute)
            {
                routeTimer -= Time.deltaTime;
                if (routeTimer <= 0f)
                {
                    EndRoute();
                }
                else
                {
                    SeekTarget(routeDestination);
                    return;
                }
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

            SeekTarget(target);
        }

        private void SeekTarget(Vector2 target)
        {
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
                self.SetMoveInput(BlendSeparation(toTarget / dist));
                self.SetSprinting(dist > sprintDistance);
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

        // Walks toward the assigned formation slot during a dead-ball window.
        // Stops once close enough so the player settles instead of jittering.
        private void SeekSetupTarget()
        {
            Vector2 target  = self.SetupTarget;
            Vector2 toTarget = target - (Vector2)transform.position;
            float dist = toTarget.magnitude;

            const float arriveTolerance = 0.25f;
            if (dist <= arriveTolerance)
            {
                self.SetMoveInput(Vector2.zero);
                self.SetSprinting(false);
                return;
            }

            self.SetMoveInput(BlendSeparation(toTarget / dist));
            self.SetSprinting(false); // jog-pace setup, not a sprint
        }

        private DemoballMovementController ResolveLeader()
        {
            if (teammates == null) return null;
            foreach (var t in teammates)
            {
                if (t == null || t == self) continue;
                var b = t.GetComponent<DemoballInputBroker>();
                if (b != null && b.IsPlayerControlled) return t;
            }
            return null;
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
