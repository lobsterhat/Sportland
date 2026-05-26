using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// CPU brain for a non-human dodgeball player. First slice: defensive
    /// reactions when the opposing team has the ball.
    ///
    ///   Idle     — no threat / our team has the ball: drift back to the
    ///              spawn home so the team holds formation.
    ///   Prepare  — an opponent is holding the ball: square up and face them.
    ///   React    — an opponent's throw is in flight and heading at me. A
    ///              one-time decision (weighted by the catching rating, and
    ///              only if I can legally catch here) commits to either:
    ///                Catch — slide onto the ball's line and arm a catch, or
    ///                Evade — sidestep out of the ball's path.
    ///
    /// Movement targets are clamped to the assigned zone so the AI stays legal
    /// and in formation. Drives the same PlayerMovement / catch-arm API the
    /// human uses; the Ball resolves an armed AI catch with the same skill
    /// check as a human.
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

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            tracker = GetComponent<PlayerZoneTracker>();
            attr = GetComponent<DodgeballAttributes>();
        }

        private void Update()
        {
            if (ball == null) ball = FindFirstObjectByType<Ball>();
            if (ball == null) { EndThreat(); Idle(); return; }

            // Holding the ball: go on offense — wind up and throw at an opponent.
            if (tracker.HasBall) { EndThreat(); Offense(); return; }
            holdStartTime = -1f;   // not holding — reset any wind-up

            // Outfielders don't defend (can't be eliminated): they fetch a loose
            // ball that's settled in their strip, otherwise hold formation. Once
            // they grab one, Offense lobs it back to an infielder.
            if (tracker.Spawn.role != PlayerRole.Infielder)
            {
                EndThreat();
                if (BallIsLoose()) ChaseLooseBall();
                else Idle();
                return;
            }

            if (IsIncomingThreat(out Vector2 ballDir))
            {
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
                    case Reaction.Catch:    DoCatch(ballDir); break;
                    case Reaction.Duck:     DoDuck(ballPos); break;
                    case Reaction.Jump:     DoJump(ballPos, distToBall); break;
                    default:                DoSidestep(ballDir); break;
                }
                return;
            }

            EndThreat();

            var carrier = ball.Carrier;
            if (carrier != null && carrier.Spawn.team != tracker.Spawn.team) Prepare(carrier);
            else if (carrier == null && BallIsLoose() && IsClosestTeammateToBall()) ChaseLooseBall();
            else Idle();
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

        // Plant, face a target opponent, wind up, then throw.
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
                return;
            }

            movement.IsRunning = false;
            movement.ApplyMove(Vector2.zero);   // plant to throw / pass

            // Outfielders feed the front court: lob the ball back to an infielder
            // rather than throwing at opponents.
            if (tracker.Spawn.role != PlayerRole.Infielder) { PassToInfielder(); return; }

            if (throwTarget == null || throwTarget.Spawn.team == tracker.Spawn.team)
                throwTarget = PickThrowTarget();
            if (throwTarget == null) return;    // nobody to throw at — just hold

            movement.SetFacing((Vector2)throwTarget.transform.position - (Vector2)transform.position);

            if (holdStartTime < 0f) holdStartTime = Time.time;
            if (Time.time - holdStartTime >= windupTime)
            {
                ThrowAtTarget(throwTarget);
                holdStartTime = -1f;
                throwTarget = null;
            }
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

        private void ChaseLooseBall()
        {
            movement.IsRunning = true;
            movement.SetStance(false);
            Vector2 ballPos = ball.transform.position;
            movement.SetFacing(ballPos - (Vector2)transform.position);
            MoveToward(ClampToZone(ballPos));
        }

        // Outfielder offense: lob the ball back to the nearest teammate infielder
        // (over the opposing court — Ball.Pass raises the arc to clear opponents).
        private void PassToInfielder()
        {
            var target = NearestTeammateInfielder();
            if (target == null) return;   // no infielder to feed — just hold

            movement.SetFacing((Vector2)target.transform.position - (Vector2)transform.position);

            if (holdStartTime < 0f) holdStartTime = Time.time;
            if (Time.time - holdStartTime >= windupTime)
            {
                ball.Pass(target.transform.position, passSpeed, isLob: true);
                holdStartTime = -1f;
            }
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

        private void Idle()
        {
            movement.IsRunning = false;
            movement.SetStance(false);
            Vector2 home = tracker.Spawn.position;
            Vector2 delta = home - (Vector2)transform.position;
            movement.ApplyMove(delta.sqrMagnitude > homeDeadzone * homeDeadzone
                ? Vector2.ClampMagnitude(delta, 1f) : Vector2.zero);
        }

        private void MoveToward(Vector2 target)
        {
            Vector2 delta = target - (Vector2)transform.position;
            movement.ApplyMove(delta.sqrMagnitude > 0.0004f ? Vector2.ClampMagnitude(delta, 1f) : Vector2.zero);
        }

        private Vector2 ClampToZone(Vector2 pos) => tracker.AssignedZone.Clamp(pos);
    }
}
