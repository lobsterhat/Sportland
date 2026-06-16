using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// CPU brain for a non-human dodgeball player.
    ///
    /// Per-frame behavior chain (priority order; first node to claim the frame wins):
    ///   TryActWithBall                — I have the ball → offense (throw / pass / carry home)
    ///   TryAnticipateOutfielderCatch  — outfielder reading a throw landing in my strip
    ///   TryReactToOpposingPass        — closest infielder intercepts; others set against the passer
    ///   TryReactToIncomingThrow       — emergency: catch / duck / jump / sidestep
    ///   TryPrepareForCarrier          — opposing carrier exists → infielders back off, outfielders slide toward midline (top/bottom)
    ///   TrySupportTeammate            — same-team carrier exists → off-ball support position
    ///   TryChaseLooseBall             — loose ball + I'm closest in my retrieval zone
    ///   Idle                          — fallback: drift home so formation holds
    ///
    /// Role gates (infielder vs outfielder) live inside each node so a single chain
    /// serves every player. Movement targets are clamped to the assigned zone so the
    /// AI stays legal and in formation. The AI drives the same PlayerMovement /
    /// catch-arm API the human uses; the Ball resolves an armed AI catch with the
    /// same skill check as a human.
    /// </summary>
    [RequireComponent(typeof(PlayerMovement))]
    [RequireComponent(typeof(PlayerZoneTracker))]
    public class DodgeballAI : MonoBehaviour
    {
        [Header("Threat detection")]
        [Tooltip("Start reacting when the ball is within this distance along its path.")]
        [SerializeField] private float reactDistance = 7f;
        [Tooltip("Perpendicular distance from the ball's path that counts as 'coming at me'.")]
        [SerializeField] private float threatRadius = 1.5f;

        [Header("Reaction")]
        [Tooltip("How far to sidestep when evading.")]
        [SerializeField] private float evadeDistance = 1.5f;
        [Tooltip("Arm the catch once the ball is within this distance (gives the press-window timing).")]
        [SerializeField] private float armWithinDistance = 2f;
        [Tooltip("Stop nudging home once within this distance of the spawn spot.")]
        [SerializeField] private float homeDeadzone = 0.4f;

        [Header("Evasion thresholds (predicted ball Height)")]
        [Tooltip("Catch only viable if the predicted arrival height is at/below this (catch reach).")]
        [SerializeField] private float maxCatchHeight = 1.5f;
        [Tooltip("At/above this predicted height, duck under the throw.")]
        [SerializeField] private float highBallThreshold = 0.9f;
        [Tooltip("At/below this predicted height, jump over the throw.")]
        [SerializeField] private float lowBallThreshold = 0.6f;

        [Header("Offense (throwing)")]
        [Tooltip("Seconds the AI holds the ball (winds up) before throwing.")]
        [SerializeField] private float windupTime = 0.7f;
        [Tooltip("Release speed (u/s) at throwSpeed rating 0 and 100; the rating lerps between them.")]
        [SerializeField] private float minThrowSpeed = 12f;
        [SerializeField] private float maxThrowSpeed = 36f;
        [Tooltip("At accuracy 0, the aim scatters up to this many units per unit of distance (→0 at accuracy 100).")]
        [SerializeField] private float accuracyErrorPerUnit = 0.15f;
        [Tooltip("Base target height (u) for a jump-attack throw, instead of carryHeight (chest). 0.9 = waist; a spike lands lower than a flat throw would. Set to carryHeight (1.29) to match standing throws.")]
        [SerializeField] private float jumpThrowAimHeight = 0.9f;
        [Tooltip("Vertical scatter coefficient for jump throws (per unit of throw distance, scaled by 1 - accuracy01). A low-accuracy thrower at long distance can have their spike's target height pushed below zero — the ball short-arcs into the ground instead of arriving at the target. 0.08 gives a 0.3-accuracy thrower at 8m about ±0.45 u of vertical miss.")]
        [SerializeField] private float jumpThrowVerticalScatter = 0.08f;
        [Tooltip("Base lateral speed (u/s) of an outfielder's lob back to an infielder; scales up with distance.")]
        [SerializeField] private float passSpeed = 12f;
        [Tooltip("Speed multiplier on passSpeed for a hard chest pass when the lane is clear — fast & flat, harder to set up against, easier to intercept.")]
        [SerializeField] private float hardPassSpeedMul = 1.6f;
        [Tooltip("Perpendicular distance (u) within which an opponent counts as 'in the lane' — flips the pass from hard chest to lob, and tells defenders where to step in.")]
        public float laneClearRadius = 1.5f;
        [Tooltip("Wider lane-clearance radius (u) used specifically for outfielder→infielder passes. Outfielder paths are long and cross opposing territory, so the lane check should be more cautious — even a defender 2-3 m off the direct line has time to step into a chest pass. Bump higher to force more lobs. 2.0 matches Ball.lobLaneRadius so the AI's decision and the Ball's clearance bump agree.")]
        public float outfielderPassLaneRadius = 2.0f;
        [Tooltip("Ball Height (u) above which an intercepting defender jumps for extra reach (PickupHeightFor scales with the jump).")]
        [SerializeField] private float interceptJumpHeight = 1.4f;
        [Tooltip("How far (u) an infielder shifts toward the opposing half when supporting an outfielder carrier — closer for the pass-back and the follow-up shot, but more exposed if the pass is intercepted. Applies to non-best-shooter teammates; the best shooter retreats by supportRetreatShift instead.")]
        public float supportForwardShift = 1.5f;
        [Tooltip("How far (u) the BEST-SHOOTER teammate infielder retreats AWAY from the centerline when supporting an outfielder carrier. Creates a deep, safe lob target the outfielder can drop the ball into past the front-line defenders. Slider 0..5 in the tuning panel; bake the value you like in code.")]
        public float supportRetreatShift = 2f;

        [Tooltip("When true, an outfielder carrier whose lane to the best infielder is blocked will rotate the ball to a teammate outfielder with a clear lane instead (backcourt rotation).")]
        public bool enableOutfielderRotation = false;
        [Tooltip("Per-unit distance penalty applied when comparing pass-target score potential. A far teammate needs to be substantially better to be preferred over a close one.")]
        public float passDistancePenalty01 = 0.02f;
        [Tooltip("How much higher (in 0..1 score-potential space) a teammate's effective shot must be before a carrier-infielder passes instead of shooting themselves.")]
        public float passOverThrowBias = 0.10f;

        [Header("Attacks")]
        [Tooltip("Base chance (0..1) that a carrier-infielder commits to a NON-stationary attack (running / jump / run-jump) on a possession, instead of the cautious stationary windup throw. Scaled by the player's aggression (accuracy+speed) and teamAggressionMul.")]
        [Range(0f, 1f)] public float attackChance = 0.5f;
        [Tooltip("Manual team aggression multiplier on attackChance. 1 = neutral. Multiplies with the automatic game-state strategy (below) — leave at 1 to let strategy drive it alone, or bias the whole team.")]
        [Range(0.2f, 2f)] public float teamAggressionMul = 1f;
        [Tooltip("Drive aggression automatically from match state (score + time, or bodies remaining in elimination): behind → aggressive, ahead → cautious, amplified late. Off = manual teamAggressionMul only.")]
        public bool useGameStateStrategy = true;
        [Tooltip("Relative weight of the RUNNING attack: advance toward the line, throw grounded with running momentum (faster ball, closer, exposed near the midline).")]
        public float runningWeight = 0.5f;
        [Tooltip("Relative weight of the JUMP attack: jump in place, release at apex — a different (descending) angle that can disrupt catch timing; exposed on landing.")]
        public float jumpWeight = 0.3f;
        [Tooltip("Relative weight of the RUN-JUMP attack: run up + jump + release deep into the opposing zone — most power, most committal, most exposed.")]
        public float runJumpWeight = 0.2f;
        [Tooltip("Distance (u) to my zone's forward edge at which a RUNNING attack releases the grounded throw. Smaller = closer to the line (more momentum, more exposed).")]
        public float runningReleaseDistance = 1.0f;
        [Tooltip("Distance (u) to my zone's forward edge at which the RUN-JUMP commits to the jump + release. Smaller = release closer to the line — more momentum but riskier (jump too late and you cross the line on release, neutering the shot).")]
        public float runJumpEdgeDistance = 1.2f;
        [Tooltip("Movement-input magnitude during a running approach. 1 = full run; lower is less committal but builds less momentum.")]
        [SerializeField, Range(0.4f, 1f)] private float runJumpInputSpeed = 1f;

        [Header("Loose-ball retrieval")]
        [Tooltip("Dive for a bouncing (deflected) ball when its predicted landing is within this distance — a lunging catch with arms extended. The dive may cross the zone line (legal while airborne).")]
        [SerializeField] private float diveRange = 3f;
        [Tooltip("Speed when ambling toward a loose ball, as a fraction of walk speed — there's no urgency, so it shouldn't sprint. 1 = full walk.")]
        [SerializeField, Range(0.2f, 1f)] private float looseChaseSpeed = 0.6f;
        [Tooltip("Cross-retrieval distance cap (u). Don't chase a loose ball outside my own zone if it's farther than this — keeps the whole infield from sprinting after a deep-corner ball.")]
        public float crossRetrieveMaxDist = 8f;
        [Tooltip("How far (u) beyond my assigned strip the predicted ball-landing can be and still trigger the outfielder anticipate behavior. Wider = outfielders react to off-line throws / lobs more aggressively, but they may abandon their strip more often.")]
        public float anticipateBuffer = 2f;
        [Tooltip("How far (u) a top/bottom outfielder slides FORWARD (toward the backline of opposing infield) when their team is on offense — coverage for missed shots and deflections that end up deep.")]
        public float outfielderBacklineShift = 2f;
        [Tooltip("How far (u) a top/bottom outfielder slides BACKWARD (toward the midline / centerline) when their team is on defense — coverage for deflections coming back toward our side after the opposing team's throw.")]
        public float outfielderMidlineShift = 2f;
        [Tooltip("Distance (u) from a movement target at which input starts scaling down toward zero. Players approaching a position will decelerate over this radius rather than carrying their momentum past the target. Smaller = sharper stops but more overshoot risk; larger = gentler arrival but slower.")]
        public float arrivalSlowdownRadius = 1.8f;

        private PlayerMovement movement;
        private PlayerZoneTracker tracker;
        private DodgeballAttributes attr;
        private Ball ball;
        private DodgeballMatch match;

        private enum Reaction { None, Catch, Duck, Jump, Sidestep }
        private Reaction reaction;
        private bool threatActive;
        private bool armedThisThreat;
        private bool jumpedThisThreat;

        private float holdStartTime = -1f;       // when we picked up the ball (wind-up clock)
        private PlayerZoneTracker throwTarget;

        // Attack selection. Chosen once per possession when we lock a target;
        // committed until release. Reset on carry-home / pass / release.
        //   Stationary — windup throw from where we stand (default, cautious).
        //   Running    — advance toward the line, throw grounded with momentum.
        //   Jump       — jump in place, release at apex (different angle).
        //   RunJump    — run up + jump + release deep (most committal/exposed).
        private enum AttackType { Stationary, Running, Jump, RunJump }
        // Phase within a moving attack:
        //   Approach — running toward the forward edge (Running / RunJump).
        //   Airborne — mid-air, holding the throw until the jump apex (Jump / RunJump).
        private enum AttackPhase { None, Approach, Airborne }
        private AttackType currentAttack = AttackType.Stationary;
        private AttackPhase attackPhase = AttackPhase.None;
        private bool attackChosen;
        private float jumpStartTime = -1f;

        /// <summary>
        /// Short label of the chain node driving this AI this frame ("Offense",
        /// "Intercept", "Chase", "Idle", etc.). Empty until the first frame runs.
        /// Read-only — set by the Try* nodes and the Idle path. Surfaced by
        /// <see cref="DodgeballPlayerLabels"/> as a debug overlay under the jersey.
        /// </summary>
        public string CurrentDecision { get; private set; } = "";

        // Offense decision instrumentation (read by the diagnostics HUD's AI
        // panel). Updated each frame while this player carries the ball.
        public string DbgBranch { get; private set; } = "";   // chosen offense branch
        public string DbgTarget { get; private set; } = "";   // throw target jersey
        public string DbgPass   { get; private set; } = "";   // pass-vs-throw numbers
        public string DbgAttack { get; private set; } = "";   // attack-roll breakdown

        private static string Short(PlayerZoneTracker t) => t != null ? $"{t.Spawn.team}{t.Number}" : "?";

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            tracker = GetComponent<PlayerZoneTracker>();
            attr = GetComponent<DodgeballAttributes>();
        }

        // Per-frame priority chain. First Try* to claim the frame wins; later
        // nodes don't run. TryReactToIncomingThrow owns the threat state machine;
        // every other reachable node calls EndThreat() on entry so stale state
        // doesn't survive between frames. Adding a behavior = insert one node
        // into the ordered list.
        private void Update()
        {
            if (ball == null) ball = FindFirstObjectByType<Ball>();
            if (ball == null) { EndThreat(); CurrentDecision = "Idle"; Idle(); return; }

            if (TryActWithBall())               return;
            holdStartTime = -1f;                // not holding — reset wind-up
            if (TryAnticipateOutfielderCatch()) return;
            if (TryReactToOpposingPass())       return;
            if (TryReactToIncomingThrow())      return;
            EndThreat();
            if (TryPrepareForCarrier())         return;
            if (TrySupportTeammate())           return;
            if (TryChaseLooseBall())            return;
            CurrentDecision = "Idle";
            Idle();
        }

        // ── Behavior chain nodes ──
        // Each Try* returns true iff it handled the frame. Role gates live
        // inside the node, so one chain serves both infielders and outfielders.
        // Each claiming node also stamps CurrentDecision so the debug overlay
        // (DodgeballPlayerLabels) can show who's deciding what in real time.

        // I'm holding the ball → wind up and throw, or carry home first.
        private bool TryActWithBall()
        {
            if (!tracker.HasBall) return false;
            EndThreat();
            CurrentDecision = "Offense";
            Offense();
            return true;
        }

        // Outfielders only: a ball is in flight (Thrown) OR still in motion
        // after first ground contact (Bouncing), and its predicted landing is
        // near my strip. Get under it to take it out of the air or off the hop.
        //
        // "Near" = in my AssignedZone OR within anticipateBuffer of it — gives
        // a little forgiveness for off-line throws / wall-bounce mispredictions.
        //
        // The closest same-team outfielder commits; the others fall through to
        // their Idle / strip-center support spot so we don't have all three
        // chasing the same ball.
        private bool TryAnticipateOutfielderCatch()
        {
            if (tracker.Spawn.role == PlayerRole.Infielder) return false;
            EndThreat();

            var st = ball.CurrentState;
            if (st != Ball.State.Thrown && st != Ball.State.Bouncing) return false;

            Vector2 land = ball.PredictGroundPoint();
            if (!IsNearMyStrip(land)) return false;
            if (!IsClosestSameTeamOutfielderToPoint(land)) return false;

            CurrentDecision = "Anticipate";
            AnticipateCatch(land);
            return true;
        }

        private bool IsNearMyStrip(Vector2 point)
        {
            var zone = tracker.AssignedZone;
            if (zone.Contains(point)) return true;
            Vector2 clamped = zone.Clamp(point);
            return Vector2.Distance(clamped, point) <= anticipateBuffer;
        }

        // True if no same-team outfielder is closer to point than I am.
        private bool IsClosestSameTeamOutfielderToPoint(Vector2 point)
        {
            float myDistSq = ((Vector2)transform.position - point).sqrMagnitude;
            var team = tracker.Spawn.team;
            var all = PlayerZoneTracker.All;
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (t == null || t == tracker) continue;
                if (t.Spawn.team != team || t.Spawn.role != PlayerRole.Outfielder) continue;
                float d = ((Vector2)t.transform.position - point).sqrMagnitude;
                if (d < myDistSq) return false;
            }
            return true;
        }

        // Infielders only: an opposing pass is in flight. The closest same-team
        // infielder steps to the lane; the others stay set facing the passer,
        // ready for the next throw.
        private bool TryReactToOpposingPass()
        {
            if (tracker.Spawn.role != PlayerRole.Infielder) return false;
            if (ball.CurrentState != Ball.State.Passing) return false;
            var passer = ball.RecentThrower;
            if (passer == null || passer.Spawn.team == tracker.Spawn.team) return false;
            EndThreat();
            if (IsClosestInfielderToPassLine()) { CurrentDecision = "Intercept";   InterceptPass(); }
            else                                { CurrentDecision = "Prep (pass)"; Prepare(passer); }
            return true;
        }

        // Infielders only: a live throw's line passes through me. Commit once
        // per threat to Catch/Duck/Jump/Sidestep and execute it each frame
        // until the threat clears.
        private bool TryReactToIncomingThrow()
        {
            if (tracker.Spawn.role != PlayerRole.Infielder) return false;
            if (!IsIncomingThreat(out Vector2 ballDir)) return false;

            movement.SetStance(true);   // reacting to a live throw — fully set
            Vector2 ballPos = ball.transform.position;
            float distToBall = Vector2.Distance(transform.position, ballPos);
            float predictedHeight = ball.PredictHeightAfter(distToBall);

            if (!threatActive)
            {
                threatActive = true;
                armedThisThreat = false;
                jumpedThisThreat = false;
                reaction = Decide(predictedHeight);
            }

            switch (reaction)
            {
                case Reaction.Catch:    CurrentDecision = "Catch!"; DoCatch(ballDir);            break;
                case Reaction.Duck:     CurrentDecision = "Duck";   DoDuck(ballPos);             break;
                case Reaction.Jump:     CurrentDecision = "Jump";   DoJump(ballPos, distToBall); break;
                default:                CurrentDecision = "Dodge";  DoSidestep(ballDir);         break;
            }
            return true;
        }

        // An opposing carrier exists.
        //   Infielders: back off along the zone-depth axis so any throw has
        //   further to travel.
        //   Outfielders: top/bottom slide TOWARD the midline — deflections off
        //   our defenders bounce back that way after the opposing throw.
        //   Back outfielder stays centered in their strip.
        private bool TryPrepareForCarrier()
        {
            var carrier = ball.Carrier;
            if (carrier == null || carrier.Spawn.team == tracker.Spawn.team) return false;

            if (tracker.Spawn.role == PlayerRole.Infielder)
            {
                CurrentDecision = "Prepare";
                Prepare(carrier);
                return true;
            }

            CurrentDecision = "Prepare";
            PrepareOutfielderDefense(carrier);
            return true;
        }

        // Outfielder defensive positioning. Faces the opposing carrier; top
        // and bottom outfielders slide toward the midline (back toward our
        // half) since that's where deflections off our defenders end up.
        // Back outfielder holds strip center.
        private void PrepareOutfielderDefense(PlayerZoneTracker carrier)
        {
            movement.IsRunning = false;
            movement.SetStance(true);   // set and watching the ball
            Vector2 me = transform.position;
            movement.SetFacing((Vector2)carrier.transform.position - me);

            Vector2 stripCenter = (tracker.AssignedZone.min + tracker.AssignedZone.max) * 0.5f;
            bool isBack = tracker.Spawn.id.EndsWith("_Back");
            Vector2 target;
            if (isBack)
            {
                target = stripCenter;
            }
            else
            {
                Vector2 forward = tracker.Spawn.team == Team.A ? Vector2.right : Vector2.left;
                target = stripCenter - forward * outfielderMidlineShift;
            }

            MoveToward(ClampToZone(target));
        }

        // Both roles: my teammate has the ball. Slide to a useful off-ball spot
        // so the offense looks like a play instead of one player working alone.
        // Outfielders center in their strip (an obvious pass target); infielders
        // supporting an outfielder carrier shift forward (closer for the pass-
        // back + follow-up shot); infielders supporting another infielder hold
        // spawn (maintains spread, prevents bunching).
        private bool TrySupportTeammate()
        {
            var carrier = ball.Carrier;
            if (carrier == null) return false;
            if (carrier == tracker) return false;   // shouldn't reach here (TryActWithBall caught me)
            if (carrier.Spawn.team != tracker.Spawn.team) return false;
            CurrentDecision = "Support";
            SupportTeammate(carrier);
            return true;
        }

        private void SupportTeammate(PlayerZoneTracker carrier)
        {
            movement.IsRunning = false;
            movement.SetStance(false);
            Vector2 me = transform.position;
            movement.SetFacing((Vector2)carrier.transform.position - me);

            bool iAmInfielder = tracker.Spawn.role == PlayerRole.Infielder;
            bool carrierIsOutfielder = carrier.Spawn.role == PlayerRole.Outfielder;
            Vector2 supportTarget;

            if (iAmInfielder)
            {
                if (carrierIsOutfielder)
                {
                    // Best-shooter teammate (highest ScorePotential) RETREATS
                    // deep — gives the outfielder a safe lob target past the
                    // centerline defenders. Other infielders shift forward as
                    // close-support options so the defense has to split.
                    bool iAmBestShooter = AmIBestShooterOnMyTeam();
                    Vector2 forward = tracker.Spawn.team == Team.A ? Vector2.right : Vector2.left;
                    supportTarget = iAmBestShooter
                        ? (Vector2)tracker.Spawn.position - forward * supportRetreatShift
                        : (Vector2)tracker.Spawn.position + forward * supportForwardShift;
                }
                else
                {
                    // Infielder supporting another infielder: hold spawn so we
                    // don't collapse onto the carrier and break the spread.
                    supportTarget = tracker.Spawn.position;
                }
            }
            else
            {
                // Outfielder on offense (my team has the ball). Top / bottom
                // slide FORWARD toward the backline — that's where missed
                // shots and deflections tend to end up after our attack.
                // The BACK outfielder slides up/down to match the Y of the
                // current throw target, so they're already in line with any
                // overshoot past that target. Falls back to strip center
                // when no throw target is set (pre-windup, mid-pass, etc.).
                Vector2 stripCenter = (tracker.AssignedZone.min + tracker.AssignedZone.max) * 0.5f;
                bool isBack = tracker.Spawn.id.EndsWith("_Back");
                if (isBack)
                {
                    float targetY = stripCenter.y;
                    var intent = ball.IntendedTarget;
                    if (intent != null && intent.Spawn.team != tracker.Spawn.team)
                        targetY = intent.transform.position.y;
                    supportTarget = new Vector2(stripCenter.x, targetY);
                }
                else
                {
                    Vector2 forward = tracker.Spawn.team == Team.A ? Vector2.right : Vector2.left;
                    supportTarget = stripCenter + forward * outfielderBacklineShift;
                }
            }

            MoveToward(ClampToZone(supportTarget));
        }

        // Loose ball retrieval. Two sub-cases, top-to-bottom:
        //  1. Ball in my own zone (infielder's half, outfielder's strip) → chase
        //     as before. Infielders gate on "closest teammate infielder";
        //     outfielders chase unconditionally inside their strip.
        //  2. Ball outside my zone → cross-retrieve. Conservative + capped:
        //     only commit if I'm the closest teammate (any role), no opposing
        //     player is closer, and the ball is within crossRetrieveMaxDist.
        //     The "Cross" decision label flags this in the debug overlay.
        private bool TryChaseLooseBall()
        {
            // Ref is mid-handoff; nobody scrambles for the parked ball.
            if (DodgeballMatch.RefereeTransferActive) return false;
            if (!BallSettled()) return false;

            Vector2 ballPos = ball.transform.position;
            bool ballInOwnZone = tracker.AssignedZone.Contains(ballPos);
            bool isInfielder = tracker.Spawn.role == PlayerRole.Infielder;

            if (ballInOwnZone)
            {
                if (isInfielder && !IsClosestTeammateToBall()) return false;
                CurrentDecision = "Chase";
                ChaseLooseBall();
                return true;
            }

            // Cross-retrieval gate.
            float distToBall = Vector2.Distance(transform.position, ballPos);
            if (distToBall > crossRetrieveMaxDist) return false;
            if (!IsClosestTeammateAnyRoleToBall()) return false;
            if (AnyOpposingPlayerCloserToBall()) return false;

            CurrentDecision = "Cross";
            ChaseLooseBall(clamp: false);
            return true;
        }

        private void EndThreat()
        {
            threatActive = false;
            reaction = Reaction.None;
            armedThisThreat = false;
            jumpedThisThreat = false;
        }

        // True if an opponent's throw is in flight and on a line that passes
        // within threatRadius of me, ahead of the ball and within reactDistance.
        private bool IsIncomingThreat(out Vector2 ballDir)
        {
            ballDir = Vector2.zero;
            if (ball.CurrentState != Ball.State.Thrown) return false;

            var thrower = ball.RecentThrower;
            if (thrower != null && thrower.Spawn.team == tracker.Spawn.team) return false; // our own throw

            Vector2 vel = ball.Velocity;
            if (vel.sqrMagnitude < 0.0001f) return false;
            ballDir = vel.normalized;

            Vector2 ballPos = ball.transform.position;
            Vector2 toMe = (Vector2)transform.position - ballPos;
            float along = Vector2.Dot(toMe, ballDir);
            if (along <= 0f || along > reactDistance) return false;   // behind the ball or too far

            Vector2 closest = ballPos + ballDir * along;
            return Vector2.Distance(closest, transform.position) <= threatRadius;
        }

        // Commit once per threat, using all known factors: try to catch when
        // it's legal/reachable and a catching-weighted roll says so; otherwise
        // pick the evasion that best clears the predicted arrival height —
        // duck under a high ball, jump over a low one, sidestep the rest.
        private Reaction Decide(float predictedHeight)
        {
            bool canCatch = tracker.CanCatchBall() && predictedHeight <= maxCatchHeight;
            float catch01 = attr != null ? attr.EffectiveCatching01 : 0.6f;
            if (canCatch && Random.value < catch01) return Reaction.Catch;

            if (predictedHeight >= highBallThreshold) return Reaction.Duck;
            if (predictedHeight <= lowBallThreshold)  return Reaction.Jump;
            return Reaction.Sidestep;
        }

        private void DoCatch(Vector2 ballDir)
        {
            movement.IsRunning = true;
            Vector2 me = transform.position;
            Vector2 ballPos = ball.transform.position;

            // Slide onto the ball's line (closest point on its path).
            float along = Mathf.Max(0f, Vector2.Dot(me - ballPos, ballDir));
            Vector2 onPath = ballPos + ballDir * along;
            MoveToward(ClampToZone(onPath));
            movement.SetFacing(ballPos - me);   // face the incoming ball for the facing bonus

            if (!armedThisThreat && Vector2.Distance(me, ballPos) <= armWithinDistance)
            {
                tracker.ArmCatch();
                armedThisThreat = true;
            }
        }

        private void DoSidestep(Vector2 ballDir)
        {
            movement.IsRunning = true;
            Vector2 me = transform.position;
            Vector2 ballPos = ball.transform.position;

            Vector2 perp = new Vector2(-ballDir.y, ballDir.x);
            float side = Vector2.Dot(me - ballPos, perp);          // which side of the path I'm on
            Vector2 dodge = side >= 0f ? perp : -perp;             // move further off the line
            MoveToward(ClampToZone(me + dodge * evadeDistance));
            movement.SetFacing(dodge);
        }

        // Hold position, face the ball, and stay crouched so a high throw passes over.
        private void DoDuck(Vector2 ballPos)
        {
            movement.IsRunning = false;
            movement.ApplyMove(Vector2.zero);
            movement.SetFacing(ballPos - (Vector2)transform.position);
            movement.Duck();
        }

        // Hold position, face the ball, and time a single jump so the apex
        // (highest feet) lands on the ball's arrival — a low throw passes under.
        private void DoJump(Vector2 ballPos, float distToBall)
        {
            movement.IsRunning = false;
            movement.ApplyMove(Vector2.zero);
            movement.SetFacing(ballPos - (Vector2)transform.position);

            if (jumpedThisThreat) return;
            float speed = ball.Velocity.magnitude;
            float timeToArrival = speed > 0.01f ? distToBall / speed : 999f;
            if (timeToArrival <= movement.JumpApexTime)
            {
                movement.TryJump();
                jumpedThisThreat = true;
            }
        }

        // An opponent is holding the ball: watch the thrower and back away to the
        // far edge of the assigned zone so any throw has to travel further. We
        // retreat along the dominant "depth" axis (the way the zone sits away
        // from the carrier) but hold the spawn lane on the other axis, so the
        // defenders spread across the zone instead of collapsing into the corners.
        private void Prepare(PlayerZoneTracker carrier)
        {
            movement.IsRunning = false;
            movement.SetStance(true);   // set and watching the ball
            Vector2 me = transform.position;
            Vector2 carrierPos = carrier.transform.position;
            movement.SetFacing(carrierPos - me);

            PlayZone zone = tracker.AssignedZone;
            Vector2 zoneCenter = (zone.min + zone.max) * 0.5f;
            Vector2 away = zoneCenter - carrierPos;   // team-consistent retreat direction
            Vector2 home = tracker.Spawn.position;

            Vector2 target = Mathf.Abs(away.x) >= Mathf.Abs(away.y)
                ? new Vector2(away.x >= 0f ? zone.max.x : zone.min.x, home.y)   // depth = X, spread in Y
                : new Vector2(home.x, away.y >= 0f ? zone.max.y : zone.min.y);  // depth = Y, spread in X

            MoveToward(ClampToZone(target));
        }

        // ── Offense (holding the ball) ──

        // Plant, face a target opponent, wind up, then throw. Optionally
        // commits to a running-jump attack instead of the stationary windup.
        private void Offense()
        {
            movement.SetStance(false);

            // Airborne: don't re-decide anything. Continue an in-progress jump /
            // run-jump attack (awaiting apex); otherwise just preserve the
            // trajectory — the launch velocity is the throw's momentum bonus and
            // re-routing mid-jump would turn the player back at the line.
            if (!movement.IsGrounded)
            {
                if (attackPhase == AttackPhase.Airborne && throwTarget != null)
                    DoMovingAttack(throwTarget);
                return;
            }

            // Grabbed the ball out of our area — carry it home before we can throw.
            if (!tracker.IsInZone)
            {
                DbgBranch = "carry home (out of zone)";
                movement.IsRunning = true;
                Vector2 back = tracker.Spawn.position;
                movement.SetFacing(back - (Vector2)transform.position);
                MoveToward(back);
                ResetAttack();   // attack choice + wind-up only once we're set in our area
                return;
            }

            // Outfielders never shoot (no scoring credit) → always pass. Carrier-
            // infielders shoot UNLESS a teammate is substantially better
            // positioned for the kill — then route the ball there first.
            bool wantsToPass;
            if (tracker.Spawn.role != PlayerRole.Infielder) { wantsToPass = true; DbgPass = "outfielder -> pass"; }
            else wantsToPass = ShouldPassToBetterTeammate();   // sets DbgPass
            if (wantsToPass)
            {
                DbgBranch = "pass";
                ClearAttackChoice();   // NOT ResetAttack — keep holdStartTime so the pass wind-up accrues
                movement.IsRunning = false;
                movement.ApplyMove(Vector2.zero);
                PassToInfielder();   // may overwrite DbgBranch = "HOLD (no pass target)"
                return;
            }

            // Lock the target and choose an attack type once per possession.
            if (throwTarget == null || throwTarget.Spawn.team == tracker.Spawn.team)
            {
                throwTarget = PickThrowTarget();
                attackChosen = false;
            }
            if (throwTarget == null) { DbgBranch = "no opposing target"; return; }
            DbgTarget = Short(throwTarget);

            if (!attackChosen)
            {
                currentAttack = SelectAttack();   // sets DbgAttack
                attackChosen = true;
                BeginAttack();
            }

            DbgBranch = currentAttack == AttackType.Stationary ? "windup (stationary)" : $"attack: {currentAttack}";
            if (currentAttack == AttackType.Stationary) DoStationaryThrow();
            else DoMovingAttack(throwTarget);
        }

        // Pick an attack for this possession. Usually Stationary, but with
        // probability attackChance — scaled by the player's aggression
        // (accuracy+speed, 0.5×..1.5×) and the team aggression multiplier —
        // commit to a moving attack, split among Running / Jump / RunJump by
        // their relative weights.
        private AttackType SelectAttack()
        {
            float agg = attr != null
                ? (attr.EffectiveThrowAccuracy01 + attr.EffectiveThrowSpeed01) * 0.5f
                : 0.5f;
            float strategy = 1f;
            if (useGameStateStrategy)
            {
                if (match == null) match = FindFirstObjectByType<DodgeballMatch>();
                if (match != null) strategy = match.TeamAggression(tracker.Spawn.team);
            }
            float chance = Mathf.Clamp01(attackChance * (0.5f + agg) * teamAggressionMul * strategy);
            float roll = UnityEngine.Random.value;
            AttackType result;
            if (roll > chance)
            {
                result = AttackType.Stationary;
            }
            else
            {
                float total = runningWeight + jumpWeight + runJumpWeight;
                if (total <= 0.0001f) result = AttackType.Stationary;
                else
                {
                    float r = UnityEngine.Random.value * total;
                    result = r < runningWeight ? AttackType.Running
                           : r < runningWeight + jumpWeight ? AttackType.Jump
                           : AttackType.RunJump;
                }
            }
            DbgAttack = $"p{chance:F2}(c{attackChance:F2} a{(0.5f + agg):F2} t{teamAggressionMul:F2} s{strategy:F2}) roll{roll:F2} -> {result}";
            return result;
        }

        // Set the initial phase for the chosen attack. Jump launches immediately
        // in place; Running / RunJump start their approach run; Stationary just
        // resets the wind-up clock.
        private void BeginAttack()
        {
            attackPhase = AttackPhase.None;
            jumpStartTime = -1f;
            holdStartTime = -1f;
            switch (currentAttack)
            {
                case AttackType.Running:
                case AttackType.RunJump:
                    attackPhase = AttackPhase.Approach;
                    break;
                case AttackType.Jump:
                    if (movement.IsGrounded)
                    {
                        movement.TryJump();
                        attackPhase = AttackPhase.Airborne;
                        jumpStartTime = Time.time;
                    }
                    break;
            }
        }

        // Stationary windup throw (default, cautious): stand, face, telegraph
        // for windupTime, release from where we stand.
        private void DoStationaryThrow()
        {
            movement.IsRunning = false;
            movement.ApplyMove(Vector2.zero);
            movement.SetFacing((Vector2)throwTarget.transform.position - (Vector2)transform.position);

            if (holdStartTime < 0f) holdStartTime = Time.time;
            if (Time.time - holdStartTime >= windupTime)
            {
                ThrowAtTarget(throwTarget);
                ResetAttack();
            }
        }

        // Running / Jump / RunJump execution.
        //   Approach: run toward the target. At the forward edge, a Running
        //     attack releases the grounded throw (momentum from the run); a
        //     RunJump jumps and switches to Airborne.
        //   Airborne: hold the throw until the jump apex (max power/accuracy —
        //     the timing skill tax), then release. Lateral velocity is preserved
        //     through the jump, so the run momentum still feeds a RunJump's throw.
        private void DoMovingAttack(PlayerZoneTracker target)
        {
            if (attackPhase == AttackPhase.Approach)
            {
                movement.IsRunning = true;
                Vector2 me = transform.position;
                Vector2 dir = (Vector2)target.transform.position - me;
                if (dir.sqrMagnitude < 0.0001f) dir = Vector2.right;
                dir.Normalize();
                movement.SetFacing(dir);
                movement.ApplyMove(dir * runJumpInputSpeed);

                float edgeDist = DistanceToZoneEdgeAlong(dir);
                float release = currentAttack == AttackType.RunJump ? runJumpEdgeDistance : runningReleaseDistance;
                if (edgeDist <= release && movement.IsGrounded)
                {
                    if (currentAttack == AttackType.RunJump)
                    {
                        movement.TryJump();
                        attackPhase = AttackPhase.Airborne;
                        jumpStartTime = Time.time;
                    }
                    else   // Running — release grounded, carrying the run momentum
                    {
                        ThrowAtTarget(target);
                        ResetAttack();
                    }
                }
                return;
            }

            if (attackPhase == AttackPhase.Airborne)
            {
                movement.SetFacing((Vector2)target.transform.position - (Vector2)transform.position);
                float airTime = Time.time - jumpStartTime;
                if (airTime >= movement.JumpApexTime)
                {
                    ThrowAtTarget(target);
                    ResetAttack();
                }
            }
        }

        // Clear the attack selection but NOT holdStartTime (the wind-up clock),
        // which the pass path also uses. Clearing it every frame — as the pass
        // branch does each tick — would perpetually restart the pass wind-up and
        // the carrier would never release (stands until the shot clock).
        private void ClearAttackChoice()
        {
            throwTarget = null;
            currentAttack = AttackType.Stationary;
            attackPhase = AttackPhase.None;
            attackChosen = false;
            jumpStartTime = -1f;
        }

        private void ResetAttack()
        {
            ClearAttackChoice();
            holdStartTime = -1f;
        }

        // Distance (u) from my current position to the AssignedZone boundary
        // along direction `dir`. Used by the run-jump to know when we're close
        // enough to the forward edge to commit the jump + release.
        private float DistanceToZoneEdgeAlong(Vector2 dir)
        {
            Vector2 me = transform.position;
            var zone = tracker.AssignedZone;
            float tMax = float.MaxValue;
            if (Mathf.Abs(dir.x) > 0.001f)
            {
                float edgeX = dir.x > 0f ? zone.max.x : zone.min.x;
                float t = (edgeX - me.x) / dir.x;
                if (t > 0f) tMax = Mathf.Min(tMax, t);
            }
            if (Mathf.Abs(dir.y) > 0.001f)
            {
                float edgeY = dir.y > 0f ? zone.max.y : zone.min.y;
                float t = (edgeY - me.y) / dir.y;
                if (t > 0f) tMax = Mathf.Min(tMax, t);
            }
            return tMax == float.MaxValue ? 0f : tMax;
        }

        // Carrier-infielder pass-vs-throw decision. Pass if a teammate's
        // effective shot (their ScorePotential01, minus the per-unit pass
        // distance penalty) beats mine by at least passOverThrowBias. The
        // threshold keeps the decision stable so we don't oscillate mid-windup
        // as positions shift slightly.
        private bool ShouldPassToBetterTeammate()
        {
            if (attr == null) { DbgPass = "no attr"; return false; }
            var best = BestTeammateInfielderToPass();
            if (best == null) { DbgPass = "no better teammate -> throw"; return false; }
            var bestAttr = best.GetComponent<DodgeballAttributes>();
            if (bestAttr == null) { DbgPass = "teammate no attr -> throw"; return false; }
            float myScore = attr.ScorePotential01;
            float dist = Vector2.Distance(transform.position, best.transform.position);
            float theirEffective = bestAttr.ScorePotential01 - passDistancePenalty01 * dist;
            bool pass = theirEffective > myScore + passOverThrowBias;
            DbgPass = $"mine {myScore:F2} vs {Short(best)} {theirEffective:F2} (+{passOverThrowBias:F2}) -> {(pass ? "PASS" : "THROW")}";
            return pass;
        }

        // Nearest opposing infielder. Outfielders can't be eliminated, so they
        // aren't worth targeting.
        private PlayerZoneTracker PickThrowTarget()
        {
            PlayerZoneTracker best = null;
            float bestDistSq = float.MaxValue;
            Vector2 me = transform.position;
            var team = tracker.Spawn.team;

            var trackers = PlayerZoneTracker.All;
            for (int i = 0; i < trackers.Count; i++)
            {
                var t = trackers[i];
                if (t == null || t.Spawn.team == team || t.Spawn.role != PlayerRole.Infielder) continue;
                float d = ((Vector2)t.transform.position - me).sqrMagnitude;
                if (d < bestDistSq) { bestDistSq = d; best = t; }
            }
            return best;
        }

        private void ThrowAtTarget(PlayerZoneTracker target)
        {
            float power = Mathf.Lerp(minThrowSpeed, maxThrowSpeed, attr != null ? attr.EffectiveThrowSpeed01 : 0.6f);
            float anticipation = attr != null ? attr.EffectiveAnticipation01 : 0f;
            var targetRb = target.GetComponent<Rigidbody2D>();
            Vector2 targetVel = targetRb != null ? targetRb.linearVelocity : Vector2.zero;

            Vector2 aim = ball.LeadAim(transform.position, target.transform.position, targetVel, power, anticipation);
            ball.IntendedTarget = target;
            // Throws never arc UP off the thrower's hand. For airborne
            // releases (jump / dive), aim a little lower (waist-height
            // spike) AND add vertical scatter — a wild jump throw can have
            // its target height pushed below 0, in which case the math
            // produces a steep enough vy that the ball hits the ground
            // before reaching the target.
            float targetHeight = -1f;   // ThrowAt default = carryHeight
            if (!movement.IsGrounded)
            {
                float acc01 = attr != null ? attr.EffectiveThrowAccuracy01 : 0.6f;
                float dist = Vector2.Distance(transform.position, target.transform.position);
                float vErr = (1f - acc01) * jumpThrowVerticalScatter * dist;
                targetHeight = jumpThrowAimHeight + Random.Range(-vErr, vErr);
            }
            ball.ThrowAt(ApplyAccuracy(aim), power, targetHeight);
        }

        // Scatter the aim; the miss grows with distance and with how far below
        // 100 the thrower's accuracy rating is.
        private Vector2 ApplyAccuracy(Vector2 aimPoint)
        {
            float acc01 = attr != null ? attr.EffectiveThrowAccuracy01 : 0.6f;
            float dist = Vector2.Distance(transform.position, aimPoint);
            float maxError = (1f - acc01) * accuracyErrorPerUnit * dist;
            return aimPoint + Random.insideUnitCircle * maxError;
        }

        // ── Loose-ball retrieval ──

        private bool BallSettled() =>
            ball.CurrentState == Ball.State.Loose || ball.CurrentState == Ball.State.Bouncing;

        private bool BallIsLoose() =>
            BallSettled() && tracker.AssignedZone.Contains(ball.transform.position);

        // True if no same-team infielder is closer to the ball than I am, so
        // only the nearest one commits to the chase (not the whole line).
        private bool IsClosestTeammateToBall()
        {
            Vector2 ballPos = ball.transform.position;
            float myDistSq = ((Vector2)transform.position - ballPos).sqrMagnitude;
            var team = tracker.Spawn.team;
            var trackers = PlayerZoneTracker.All;
            for (int i = 0; i < trackers.Count; i++)
            {
                var t = trackers[i];
                if (t == null || t == tracker) continue;
                if (t.Spawn.team != team || t.Spawn.role != PlayerRole.Infielder) continue;
                if (((Vector2)t.transform.position - ballPos).sqrMagnitude < myDistSq) return false;
            }
            return true;
        }

        // Like IsClosestTeammateToBall but counts ALL teammates (any role).
        // Used by cross-retrieval, where outfielders may also commit so an
        // infielder shouldn't bother if a teammate outfielder is already closer.
        private bool IsClosestTeammateAnyRoleToBall()
        {
            Vector2 ballPos = ball.transform.position;
            float myDistSq = ((Vector2)transform.position - ballPos).sqrMagnitude;
            var team = tracker.Spawn.team;
            var trackers = PlayerZoneTracker.All;
            for (int i = 0; i < trackers.Count; i++)
            {
                var t = trackers[i];
                if (t == null || t == tracker || t.Spawn.team != team) continue;
                if (((Vector2)t.transform.position - ballPos).sqrMagnitude < myDistSq) return false;
            }
            return true;
        }

        // True if any opposing-team player is closer to the ball than I am.
        // Used by cross-retrieval to back off when we'd lose the race anyway.
        private bool AnyOpposingPlayerCloserToBall()
        {
            Vector2 ballPos = ball.transform.position;
            float myDistSq = ((Vector2)transform.position - ballPos).sqrMagnitude;
            var team = tracker.Spawn.team;
            var trackers = PlayerZoneTracker.All;
            for (int i = 0; i < trackers.Count; i++)
            {
                var t = trackers[i];
                if (t == null || t.Spawn.team == team) continue;
                if (((Vector2)t.transform.position - ballPos).sqrMagnitude < myDistSq) return true;
            }
            return false;
        }

        // clamp=true for in-zone chases (keeps the AI legal); clamp=false for
        // cross-retrieval, which is explicitly allowed across the zone line.
        private void ChaseLooseBall(bool clamp = true)
        {
            movement.IsRunning = false;   // amble after a loose ball — no need to sprint
            movement.SetStance(false);
            Vector2 me = transform.position;
            Vector2 ballPos = ball.transform.position;
            movement.SetFacing(ballPos - me);

            // A deflected ball (Bouncing) is still live to grab: if its predicted
            // landing is close, lunge for it. The dive is unclamped so it can carry
            // us across the zone line — legal as long as we don't touch down out of
            // zone — and it extends our reach (arms out) to snag the deflection.
            // (Ball auto-catches an AI within reach via TryTakeBall, no arm needed.)
            if (ball.CurrentState == Ball.State.Bouncing)
            {
                Vector2 land = ball.PredictGroundPoint();
                if (Vector2.Distance(me, land) <= diveRange)
                {
                    movement.Dive(land - me);
                    return;
                }
            }

            MoveToward(clamp ? ClampToZone(ballPos) : ballPos, looseChaseSpeed);
        }

        // Read the throw's predicted landing and get under it (clamped to our
        // zone), facing the ball and arming a catch when it's near so we can take
        // it out of the air; otherwise we're there to grab it off the hop.
        private void AnticipateCatch(Vector2 land)
        {
            movement.IsRunning = true;
            movement.SetStance(false);
            movement.SetFacing((Vector2)ball.transform.position - (Vector2)transform.position);
            MoveToward(ClampToZone(land));

            if (Vector2.Distance(transform.position, ball.transform.position) <= armWithinDistance)
                tracker.ArmCatch();
        }

        // Pass the ball to the best-positioned teammate. Default target is
        // the highest-scoring teammate infielder; outfielder carriers fall
        // back to a teammate outfielder (backcourt rotation) when no infielder
        // lane is clear, since rotating to a better angle beats forcing a
        // blocked lob.
        private void PassToInfielder()
        {
            var target = BestTeammateInfielderToPass();
            bool isOutfielderCarrier = tracker.Spawn.role != PlayerRole.Infielder;

            // Outfielder fallback: if my best-infielder pass would be blocked,
            // rotate the ball to a teammate outfielder with a clear lane. They
            // can try the delivery from their position next tick. Gated on
            // enableOutfielderRotation while we stabilize positioning.
            if (enableOutfielderRotation && isOutfielderCarrier && target != null
                && !LaneIsClear(target.transform.position, outfielderPassLaneRadius))
            {
                var rotation = BestTeammateOutfielderWithClearLane();
                if (rotation != null) target = rotation;
            }

            if (target == null) { DbgBranch = "HOLD (no pass target)"; return; }   // nobody to feed — just hold

            movement.SetFacing((Vector2)target.transform.position - (Vector2)transform.position);

            if (holdStartTime < 0f) holdStartTime = Time.time;
            if (Time.time - holdStartTime >= windupTime)
            {
                // Lane check: outfielders use a wider clearance radius because
                // their passes are long and cross opposing territory — a
                // defender 2-3 m off the direct line still has time to step
                // into a chest pass.
                float laneRadius = isOutfielderCarrier ? outfielderPassLaneRadius : laneClearRadius;
                bool laneClear = LaneIsClear(target.transform.position, laneRadius);
                float speed = laneClear ? passSpeed * hardPassSpeedMul : passSpeed;
                ball.IntendedTarget = target;
                ball.Pass(target.transform.position, speed, isLob: !laneClear);
                holdStartTime = -1f;
            }
        }

        // ── Pass interception (defenders) ──

        // Step onto the passer→receiver line — clamped to my zone — and arm a
        // catch. Jump for high lobs (PickupHeightFor extends with jump height).
        private void InterceptPass()
        {
            movement.IsRunning = true;
            movement.SetStance(true);
            Vector2 me = transform.position;
            Vector2 ballPos = ball.transform.position;
            var ballTarget = ball.IntendedTarget;
            Vector2 targetPos = ballTarget != null ? (Vector2)ballTarget.transform.position : ballPos;

            Vector2 seg = targetPos - ballPos;
            float segLen = seg.magnitude;
            if (segLen < 0.01f) { Idle(); return; }
            Vector2 dir = seg / segLen;
            float along = Mathf.Clamp(Vector2.Dot(me - ballPos, dir), 0f, segLen);
            Vector2 onLine = ballPos + dir * along;

            movement.SetFacing(ballPos - me);
            MoveToward(ClampToZone(onLine));

            // Jump for a high lob — the catch reach rises with the jump.
            if (ball.Height > interceptJumpHeight && Vector2.Distance(me, ballPos) < laneClearRadius)
                movement.TryJump();

            // Arm the catch as the ball nears (skill catch on opponent's pass).
            if (Vector2.Distance(me, ballPos) <= armWithinDistance)
                tracker.ArmCatch();
        }

        // True if I'm the same-team infielder closest to the current pass
        // segment — only the nearest one drops back to intercept.
        private bool IsClosestInfielderToPassLine()
        {
            var ballTarget = ball.IntendedTarget;
            if (ballTarget == null) return false;
            Vector2 ballPos = ball.transform.position;
            Vector2 targetPos = ballTarget.transform.position;
            Vector2 seg = targetPos - ballPos;
            float segLen = seg.magnitude;
            if (segLen < 0.01f) return false;
            Vector2 dir = seg / segLen;

            float myDist = DistanceToSegment(transform.position, ballPos, dir, segLen);
            var team = tracker.Spawn.team;
            var all = PlayerZoneTracker.All;
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (t == null || t == tracker) continue;
                if (t.Spawn.team != team || t.Spawn.role != PlayerRole.Infielder) continue;
                if (DistanceToSegment(t.transform.position, ballPos, dir, segLen) < myDist) return false;
            }
            return true;
        }

        // No opponent stands within laneClearRadius of the passer→target segment
        // (and between the two, not before / past the ends). Default overload
        // uses the standard laneClearRadius; the radius parameter overload
        // lets callers (like outfielder passes) widen the check.
        private bool LaneIsClear(Vector2 target) => LaneIsClear(target, laneClearRadius);

        private bool LaneIsClear(Vector2 target, float radius)
        {
            Vector2 ballPos = transform.position;
            Vector2 seg = target - ballPos;
            float segLen = seg.magnitude;
            if (segLen < 0.01f) return true;
            Vector2 dir = seg / segLen;
            var team = tracker.Spawn.team;
            var all = PlayerZoneTracker.All;
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (t == null || t.Spawn.team == team) continue;
                Vector2 toT = (Vector2)t.transform.position - ballPos;
                float along = Vector2.Dot(toT, dir);
                if (along <= 0.5f || along >= segLen - 0.5f) continue;
                if (Vector2.Distance(ballPos + dir * along, t.transform.position) <= radius)
                    return false;
            }
            return true;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 dir, float len)
        {
            Vector2 ap = p - a;
            float along = Mathf.Clamp(Vector2.Dot(ap, dir), 0f, len);
            return Vector2.Distance(a + dir * along, p);
        }

        private PlayerZoneTracker NearestTeammateInfielder()
        {
            PlayerZoneTracker best = null;
            float bestDistSq = float.MaxValue;
            Vector2 me = transform.position;
            var team = tracker.Spawn.team;
            var trackers = PlayerZoneTracker.All;
            for (int i = 0; i < trackers.Count; i++)
            {
                var t = trackers[i];
                if (t == null || t.Spawn.team != team || t.Spawn.role != PlayerRole.Infielder) continue;
                float d = ((Vector2)t.transform.position - me).sqrMagnitude;
                if (d < bestDistSq) { bestDistSq = d; best = t; }
            }
            return best;
        }

        // True if no same-team infielder has a higher ScorePotential01 than me.
        // Drives the "best-shooter retreats deep when our outfielder has the
        // ball" behavior in TrySupportTeammate — the star is the lob target.
        private bool AmIBestShooterOnMyTeam()
        {
            if (attr == null || tracker.Spawn.role != PlayerRole.Infielder) return false;
            float myScore = attr.ScorePotential01;
            var team = tracker.Spawn.team;
            var trackers = PlayerZoneTracker.All;
            for (int i = 0; i < trackers.Count; i++)
            {
                var t = trackers[i];
                if (t == null || t == tracker) continue;
                if (t.Spawn.team != team || t.Spawn.role != PlayerRole.Infielder) continue;
                var a = t.GetComponent<DodgeballAttributes>();
                if (a != null && a.ScorePotential01 > myScore) return false;
            }
            return true;
        }

        // Nearest same-team outfielder whose current position has a clear
        // pass lane from me (using the wider outfielderPassLaneRadius).
        // Used as a backcourt-rotation fallback when no infielder lane is
        // clear — the receiving outfielder will try to deliver next tick.
        private PlayerZoneTracker BestTeammateOutfielderWithClearLane()
        {
            PlayerZoneTracker best = null;
            float bestDistSq = float.MaxValue;
            Vector2 me = transform.position;
            var team = tracker.Spawn.team;
            var trackers = PlayerZoneTracker.All;
            for (int i = 0; i < trackers.Count; i++)
            {
                var t = trackers[i];
                if (t == null || t == tracker) continue;
                if (t.Spawn.team != team || t.Spawn.role != PlayerRole.Outfielder) continue;
                if (!LaneIsClear(t.transform.position, outfielderPassLaneRadius)) continue;
                float d = ((Vector2)t.transform.position - me).sqrMagnitude;
                if (d < bestDistSq) { bestDistSq = d; best = t; }
            }
            return best;
        }

        // Pick the same-team infielder with the highest effective pass-target
        // score (DodgeballAttributes.ScorePotential01 minus a per-unit distance
        // penalty). Self-excluded so a carrier-infielder doesn't pass to
        // themselves. Used by both outfielder passing AND carrier-infielder
        // "pass to better shooter" logic.
        private PlayerZoneTracker BestTeammateInfielderToPass()
        {
            PlayerZoneTracker best = null;
            float bestScore = float.NegativeInfinity;
            Vector2 me = transform.position;
            var team = tracker.Spawn.team;
            var trackers = PlayerZoneTracker.All;
            for (int i = 0; i < trackers.Count; i++)
            {
                var t = trackers[i];
                if (t == null || t == tracker) continue;
                if (t.Spawn.team != team || t.Spawn.role != PlayerRole.Infielder) continue;
                var a = t.GetComponent<DodgeballAttributes>();
                if (a == null) continue;
                float dist = Vector2.Distance(me, t.transform.position);
                float score = a.ScorePotential01 - passDistancePenalty01 * dist;
                if (score > bestScore) { bestScore = score; best = t; }
            }
            return best;
        }

        // Drift back to my spawn home. Sprints if I'm out of my assigned zone
        // (in particular after a run-jump landing in opp infield, so I don't
        // get stranded), walks the last stretch once back in my zone. Uses the
        // shared MoveToward arrival ramp so we don't blow past the spawn point.
        private void Idle()
        {
            Vector2 home = tracker.Spawn.position;
            Vector2 delta = home - (Vector2)transform.position;
            bool farFromHome = delta.sqrMagnitude > homeDeadzone * homeDeadzone;

            movement.IsRunning = farFromHome && !tracker.IsInZone;
            movement.SetStance(false);

            if (farFromHome) MoveToward(home);
            else             movement.ApplyMove(Vector2.zero);
        }

        // Move toward a target with an arrival ramp: input scales linearly from
        // full at arrivalSlowdownRadius down to zero at the target itself.
        // Players bleed off velocity BEFORE reaching the spot rather than
        // carrying their momentum past it.
        private void MoveToward(Vector2 target, float maxInput = 1f)
        {
            Vector2 delta = target - (Vector2)transform.position;
            float distSq = delta.sqrMagnitude;
            if (distSq < 0.0004f)
            {
                movement.ApplyMove(Vector2.zero);
                return;
            }

            float dist = Mathf.Sqrt(distSq);
            Vector2 dir = delta / dist;
            float scale = Mathf.Clamp01(dist / arrivalSlowdownRadius);
            movement.ApplyMove(dir * (maxInput * scale));
        }

        private Vector2 ClampToZone(Vector2 pos) => tracker.AssignedZone.Clamp(pos);
    }
}
