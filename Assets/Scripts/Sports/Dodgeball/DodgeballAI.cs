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

        private PlayerMovement movement;
        private PlayerZoneTracker tracker;
        private DodgeballAttributes attr;
        private Ball ball;

        private enum Reaction { None, Catch, Duck, Jump, Sidestep }
        private Reaction reaction;
        private bool threatActive;
        private bool armedThisThreat;
        private bool jumpedThisThreat;

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            tracker = GetComponent<PlayerZoneTracker>();
            attr = GetComponent<DodgeballAttributes>();
        }

        private void Update()
        {
            if (ball == null) ball = FindFirstObjectByType<Ball>();
            if (ball == null || tracker.HasBall) { EndThreat(); Idle(); return; }

            if (IsIncomingThreat(out Vector2 ballDir))
            {
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

        private void Idle()
        {
            movement.IsRunning = false;
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
