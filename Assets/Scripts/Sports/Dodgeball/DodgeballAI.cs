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

        private PlayerMovement movement;
        private PlayerZoneTracker tracker;
        private DodgeballAttributes attr;
        private Ball ball;

        private enum Reaction { None, Catch, Evade }
        private Reaction reaction;
        private bool threatActive;
        private bool armedThisThreat;

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
                if (!threatActive)
                {
                    threatActive = true;
                    armedThisThreat = false;
                    reaction = Decide();
                }
                if (reaction == Reaction.Catch) DoCatch(ballDir);
                else DoEvade(ballDir);
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

        // Commit once per threat: catch if I can legally catch here and a
        // catching-weighted roll says so, otherwise dodge.
        private Reaction Decide()
        {
            if (!tracker.CanCatchBall()) return Reaction.Evade;
            float catch01 = attr != null ? attr.Catching01 : 0.6f;
            return Random.value < catch01 ? Reaction.Catch : Reaction.Evade;
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

        private void DoEvade(Vector2 ballDir)
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

        private void Prepare(PlayerZoneTracker carrier)
        {
            movement.IsRunning = false;
            movement.ApplyMove(Vector2.zero);
            movement.SetFacing((Vector2)carrier.transform.position - (Vector2)transform.position);
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
