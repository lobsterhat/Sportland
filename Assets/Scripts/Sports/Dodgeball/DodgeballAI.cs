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
    ///   TryPrepareForCarrier          — opposing carrier exists → face & hold zone depth
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
        [Tooltip("Base lateral speed (u/s) of an outfielder's lob back to an infielder; scales up with distance.")]
        [SerializeField] private float passSpeed = 12f;
        [Tooltip("Speed multiplier on passSpeed for a hard chest pass when the lane is clear — fast & flat, harder to set up against, easier to intercept.")]
        [SerializeField] private float hardPassSpeedMul = 1.6f;
        [Tooltip("Perpendicular distance (u) within which an opponent counts as 'in the lane' — flips the pass from hard chest to lob, and tells defenders where to step in.")]
        [SerializeField] private float laneClearRadius = 1.5f;
        [Tooltip("Ball Height (u) above which an intercepting defender jumps for extra reach (PickupHeightFor scales with the jump).")]
        [SerializeField] private float interceptJumpHeight = 1.4f;
        [Tooltip("How far (u) an infielder shifts toward the opposing half when supporting an outfielder carrier — closer for the pass-back and the follow-up shot, but more exposed if the pass is intercepted.")]
        [SerializeField] private float supportForwardShift = 1.5f;
        [Tooltip("Per-unit distance penalty applied when comparing pass-target score potential. A far teammate needs to be substantially better to be preferred over a close one.")]
        [SerializeField] private float passDistancePenalty01 = 0.02f;
        [Tooltip("How much higher (in 0..1 score-potential space) a teammate's effective shot must be before a carrier-infielder passes instead of shooting themselves.")]
        [SerializeField] private float passOverThrowBias = 0.10f;

        [Header("Run-jump attack")]
        [Tooltip("Base probability (0..1) that a carrier-infielder commits to a running-jump throw on a given possession. The actual chance scales with the player's accuracy + speed — an aggressive specialist run-jumps more often than a cautious one.")]
        [SerializeField, Range(0f, 1f)] private float runJumpProbability = 0.4f;
        [Tooltip("Distance (u) to my zone's forward edge at which the run-up commits to the jump + release. Smaller = release closer to the line — more momentum but riskier (if I jump too late I cross the line on release and Phase D neuters the shot).")]
        [SerializeField] private float runJumpEdgeDistance = 1.2f;
        [Tooltip("Movement-input magnitude during the run-up. 1 = full run; lower values are less committal but build less momentum.")]
        [SerializeField, Range(0.4f, 1f)] private float runJumpInputSpeed = 1f;

        [Header("Loose-ball retrieval")]
        [Tooltip("Dive for a bouncing (deflected) ball when its predicted landing is within this distance — a lunging catch with arms extended. The dive may cross the zone line (legal while airborne).")]
        [SerializeField] private float diveRange = 3f;
        [Tooltip("Speed when ambling toward a loose ball, as a fraction of walk speed — there's no urgency, so it shouldn't sprint. 1 = full walk.")]
        [SerializeField, Range(0.2f, 1f)] private float looseChaseSpeed = 0.6f;
        [Tooltip("Cross-retrieval distance cap (u). Don't chase a loose ball outside my own zone if it's farther than this — keeps the whole infield from sprinting after a deep-corner ball.")]
        [SerializeField] private float crossRetrieveMaxDist = 8f;

        private PlayerMovement movement;
        private PlayerZoneTracker tracker;
        private DodgeballAttributes attr;
        private Ball ball;

        private enum Reaction { None, Catch, Duck, Jump, Sidestep }
        private Reaction reaction;
        private bool threatActive;
        private bool armedThisThreat;
        private bool jumpedThisThreat;

        private float holdStartTime = -1f;       // when we picked up the ball (wind-up clock)
        private PlayerZoneTracker throwTarget;

        // Run-jump attack phase. Decided once per possession when we lock on
        // a target; committed until release. Reset on carry-home / pass /
        // release.
        //   RunUp                — running toward the zone edge, ball held.
        //   AirborneAwaitingApex — jumped, mid-air, holding the throw until
        //                          the apex of the jump. Releasing earlier
        //                          forfeits power and accuracy (timing skill).
        private enum RunJumpPhase { None, RunUp, AirborneAwaitingApex }
        private RunJumpPhase runJumpPhase = RunJumpPhase.None;
        private float runJumpJumpStartTime = -1f;

        /// <summary>
        /// Short label of the chain node driving this AI this frame ("Offense",
        /// "Intercept", "Chase", "Idle", etc.). Empty until the first frame runs.
        /// Read-only — set by the Try* nodes and the Idle path. Surfaced by
        /// <see cref="DodgeballPlayerLabels"/> as a debug overlay under the jersey.
        /// </summary>
        public string CurrentDecision { get; private set; } = "";

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

        // Outfielders only: an opposing throw is in flight and lands in my
        // strip. Get under it to take it out of the air or off the hop.
        // If it's not landing in my strip we fall through to ChaseLooseBall / Idle.
        private bool TryAnticipateOutfielderCatch()
        {
            if (tracker.Spawn.role == PlayerRole.Infielder) return false;
            EndThreat();
            if (ball.CurrentState != Ball.State.Thrown) return false;
            Vector2 land = ball.PredictGroundPoint();
            if (!tracker.AssignedZone.Contains(land)) return false;
            CurrentDecision = "Anticipate";
            AnticipateCatch(land);
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

        // Infielders only: an opposing carrier exists — back off along the
        // zone-depth axis so any throw has further to travel.
        private bool TryPrepareForCarrier()
        {
            if (tracker.Spawn.role != PlayerRole.Infielder) return false;
            var carrier = ball.Carrier;
            if (carrier == null || carrier.Spawn.team == tracker.Spawn.team) return false;
            CurrentDecision = "Prepare";
            Prepare(carrier);
            return true;
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
                    // Receiver setup: shift forward (toward opposing half) so
                    // I'm closer for the pass-back and the follow-up shot.
                    Vector2 forward = tracker.Spawn.team == Team.A ? Vector2.right : Vector2.left;
                    supportTarget = (Vector2)tracker.Spawn.position + forward * supportForwardShift;
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
                // Outfielder: center in my strip — an obvious pass target from
                // any angle, and the existing PassToInfielder targeting picks
                // a closer player but a centered outfielder is reliably findable.
                supportTarget = (tracker.AssignedZone.min + tracker.AssignedZone.max) * 0.5f;
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
            float catch01 = attr != null ? attr.Catching01 : 0.6f;
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

            // Grabbed the ball out of our area — carry it home before we can throw.
            if (!tracker.IsInZone)
            {
                movement.IsRunning = true;
                Vector2 back = tracker.Spawn.position;
                movement.SetFacing(back - (Vector2)transform.position);
                MoveToward(back);
                holdStartTime = -1f;   // wind-up only starts once we're set in our area
                runJumpPhase = RunJumpPhase.None;
                runJumpJumpStartTime = -1f;
                return;
            }

            // Outfielders never shoot (no scoring credit). Carrier-infielders
            // shoot UNLESS a teammate is substantially better positioned for
            // the kill — then route the ball there first.
            bool wantsToPass = tracker.Spawn.role != PlayerRole.Infielder
                            || ShouldPassToBetterTeammate();
            if (wantsToPass)
            {
                throwTarget = null;
                runJumpPhase = RunJumpPhase.None;
                runJumpJumpStartTime = -1f;
                movement.IsRunning = false;
                movement.ApplyMove(Vector2.zero);
                PassToInfielder();
                return;
            }

            // Lock the target on first frame; roll for run-jump at the same time.
            if (throwTarget == null || throwTarget.Spawn.team == tracker.Spawn.team)
            {
                throwTarget = PickThrowTarget();
                if (throwTarget != null && holdStartTime < 0f && runJumpPhase == RunJumpPhase.None)
                {
                    if (UnityEngine.Random.value < EffectiveRunJumpProb())
                        runJumpPhase = RunJumpPhase.RunUp;
                }
            }
            if (throwTarget == null) return;

            if (runJumpPhase == RunJumpPhase.RunUp)
            {
                DoRunJumpAttack(throwTarget);
                return;
            }

            // Stationary windup throw (default).
            movement.IsRunning = false;
            movement.ApplyMove(Vector2.zero);
            movement.SetFacing((Vector2)throwTarget.transform.position - (Vector2)transform.position);

            if (holdStartTime < 0f) holdStartTime = Time.time;
            if (Time.time - holdStartTime >= windupTime)
            {
                ThrowAtTarget(throwTarget);
                holdStartTime = -1f;
                throwTarget = null;
            }
        }

        // Per-player run-jump aggression: base probability scaled by the
        // thrower's accuracy + speed. A 0/0 player rolls 0 (never), a 100/100
        // player rolls up to 2× the base.
        private float EffectiveRunJumpProb()
        {
            if (attr == null) return runJumpProbability;
            float aggression = (attr.ThrowAccuracy01 + attr.ThrowSpeed01) * 0.5f;   // 0..1
            return Mathf.Clamp01(runJumpProbability * aggression * 2f);
        }

        // Two-phase run-jump attack:
        //   RunUp: run toward the target until within runJumpEdgeDistance of
        //   my zone's forward edge, then TryJump and switch to AirborneAwaitingApex.
        //   AirborneAwaitingApex: hold the throw mid-air until JumpApexTime
        //   has elapsed since the jump, then release. Releasing at apex (or
        //   shortly after, on descent) is the maximum-power timing — that's
        //   the skill tax on this attack. The carrier-velocity bonus is the
        //   running lateral speed (preserved through the jump since vertical
        //   motion is decoupled from rb.linearVelocity).
        private void DoRunJumpAttack(PlayerZoneTracker target)
        {
            if (runJumpPhase == RunJumpPhase.RunUp)
            {
                movement.IsRunning = true;
                Vector2 me = transform.position;
                Vector2 dir = ((Vector2)target.transform.position - me);
                if (dir.sqrMagnitude < 0.0001f) dir = Vector2.right;
                dir.Normalize();

                movement.SetFacing(dir);
                movement.ApplyMove(dir * runJumpInputSpeed);

                float edgeDist = DistanceToZoneEdgeAlong(dir);
                if (edgeDist <= runJumpEdgeDistance && movement.IsGrounded)
                {
                    movement.TryJump();
                    runJumpPhase = RunJumpPhase.AirborneAwaitingApex;
                    runJumpJumpStartTime = Time.time;
                }
                return;
            }

            if (runJumpPhase == RunJumpPhase.AirborneAwaitingApex)
            {
                // Lateral velocity is preserved during the jump (Height is
                // decoupled), so we keep facing the target and hold input
                // direction — Ball.Throw will capture the running velocity
                // as the momentum bonus at release.
                movement.SetFacing((Vector2)target.transform.position - (Vector2)transform.position);

                float airTime = Time.time - runJumpJumpStartTime;
                if (airTime >= movement.JumpApexTime)
                {
                    ThrowAtTarget(target);
                    holdStartTime = -1f;
                    throwTarget = null;
                    runJumpPhase = RunJumpPhase.None;
                    runJumpJumpStartTime = -1f;
                }
                // else: still rising — hold the throw. (Releasing early would
                // forfeit power; the AI plays the optimal release timing.)
            }
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
            if (attr == null) return false;
            var best = BestTeammateInfielderToPass();
            if (best == null) return false;
            var bestAttr = best.GetComponent<DodgeballAttributes>();
            if (bestAttr == null) return false;
            float myScore = attr.ScorePotential01;
            float dist = Vector2.Distance(transform.position, best.transform.position);
            float theirEffective = bestAttr.ScorePotential01 - passDistancePenalty01 * dist;
            return theirEffective > myScore + passOverThrowBias;
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
            float power = Mathf.Lerp(minThrowSpeed, maxThrowSpeed, attr != null ? attr.ThrowSpeed01 : 0.6f);
            float anticipation = attr != null ? attr.Anticipation01 : 0f;
            var targetRb = target.GetComponent<Rigidbody2D>();
            Vector2 targetVel = targetRb != null ? targetRb.linearVelocity : Vector2.zero;

            Vector2 aim = ball.LeadAim(transform.position, target.transform.position, targetVel, power, anticipation);
            ball.IntendedTarget = target;
            ball.ThrowAt(ApplyAccuracy(aim), power);
        }

        // Scatter the aim; the miss grows with distance and with how far below
        // 100 the thrower's accuracy rating is.
        private Vector2 ApplyAccuracy(Vector2 aimPoint)
        {
            float acc01 = attr != null ? attr.ThrowAccuracy01 : 0.6f;
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

        // Pass the ball to the best-positioned teammate infielder — used by
        // outfielders feeding the front court AND by carrier-infielders who've
        // decided their teammate has a better shot. "Best" = highest
        // ScorePotential01 minus a distance penalty, so a close-but-okay
        // teammate beats a far-but-great one.
        private void PassToInfielder()
        {
            var target = BestTeammateInfielderToPass();
            if (target == null) return;   // no infielder to feed — just hold

            movement.SetFacing((Vector2)target.transform.position - (Vector2)transform.position);

            if (holdStartTime < 0f) holdStartTime = Time.time;
            if (Time.time - holdStartTime >= windupTime)
            {
                // Lane clear → hard chest pass (fast & flat, catches defense
                // unaware); opponent in the lane → lob over them.
                bool laneClear = LaneIsClear(target.transform.position);
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
        // (and between the two, not before / past the ends).
        private bool LaneIsClear(Vector2 target)
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
                if (Vector2.Distance(ballPos + dir * along, t.transform.position) <= laneClearRadius)
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

        private void Idle()
        {
            movement.IsRunning = false;
            movement.SetStance(false);
            Vector2 home = tracker.Spawn.position;
            Vector2 delta = home - (Vector2)transform.position;
            movement.ApplyMove(delta.sqrMagnitude > homeDeadzone * homeDeadzone
                ? Vector2.ClampMagnitude(delta, 1f) : Vector2.zero);
        }

        private void MoveToward(Vector2 target, float maxInput = 1f)
        {
            Vector2 delta = target - (Vector2)transform.position;
            movement.ApplyMove(delta.sqrMagnitude > 0.0004f ? Vector2.ClampMagnitude(delta, maxInput) : Vector2.zero);
        }

        private Vector2 ClampToZone(Vector2 pos) => tracker.AssignedZone.Clamp(pos);
    }
}
