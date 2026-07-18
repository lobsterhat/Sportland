using UnityEngine;
using Sportland.Sports.Dodgeball;

namespace Sportland.Diagnostics
{
    /// <summary>
    /// Feeds ball events into the recorder. Put this on the ball.
    ///
    /// The dodgeball resolves contact itself (manual distance checks against a
    /// simulated height axis) — physics collision callbacks never see any of
    /// it. So instead of OnCollisionEnter2D this listens to the Ball's own
    /// gameplay events: impacts, deferred scoring hits, catches, releases.
    /// </summary>
    public class BallCollisionReporter : MonoBehaviour
    {
        private Rigidbody2D _rb;
        private Ball _ball;

        void Start()
        {
            _rb = GetComponent<Rigidbody2D>();
            _ball = GetComponent<Ball>();
            if (_ball == null)
            {
                Debug.LogWarning("[BallCollisionReporter] No Ball component here — nothing to report.");
                enabled = false;
                return;
            }

            PhysicsRecorder.Instance?.Track(_rb);

            _ball.OnImpact += OnImpact;
            _ball.OnHit += OnHit;
            _ball.OnCaught += OnCaught;
            _ball.OnReleased += OnReleased;
            _ball.OnBecameLoose += OnBecameLoose;
        }

        void OnDestroy()
        {
            if (_ball != null)
            {
                _ball.OnImpact -= OnImpact;
                _ball.OnHit -= OnHit;
                _ball.OnCaught -= OnCaught;
                _ball.OnReleased -= OnReleased;
                _ball.OnBecameLoose -= OnBecameLoose;
            }
            if (PhysicsRecorder.Instance != null && _rb != null)
                PhysicsRecorder.Instance.Untrack(_rb);
        }

        // Contact the instant it happens (damage lands here, before the
        // rebound resolves and before any catch-save can negate the point).
        private void OnImpact(PlayerZoneTracker victim, float ballSpeed, float contactMul)
        {
            PhysicsRecorder.Instance?.RecordEvent(
                kind: "impact",
                description: $"ball contacted {Label(victim)} ({(contactMul < 1f ? "glancing/bobble" : "direct")}), point now pending",
                point: victim.transform.position,
                relativeVelocity: _ball.Velocity,
                impactSpeed: ballSpeed);
        }

        // The deferred scoring hit — fires only once the pending point
        // confirms (ball grounded, or the throwing team caught it).
        private void OnHit(PlayerZoneTracker victim, float ballSpeed)
        {
            PhysicsRecorder.Instance?.RecordEvent(
                kind: "hit",
                description: $"pending point on {Label(victim)} confirmed",
                point: victim.transform.position,
                relativeVelocity: _ball.Velocity,
                impactSpeed: ballSpeed);
        }

        private void OnCaught(PlayerZoneTracker catcher)
        {
            PhysicsRecorder.Instance?.RecordEvent(
                kind: "catch",
                description: $"{Label(catcher)} caught the throw",
                point: catcher.transform.position,
                relativeVelocity: _ball.Velocity);
        }

        private void OnReleased(PlayerZoneTracker thrower, PlayerZoneTracker target, bool isThrow)
        {
            string at = target != null ? $" at {Label(target)}" : "";
            PhysicsRecorder.Instance?.RecordEvent(
                kind: isThrow ? "throw" : "pass",
                description: $"{Label(thrower)} {(isThrow ? "threw" : "passed")}{at}",
                point: thrower.transform.position,
                relativeVelocity: _ball.Velocity,
                impactSpeed: _ball.Velocity.magnitude);
        }

        private void OnBecameLoose()
        {
            PhysicsRecorder.Instance?.RecordEvent(
                kind: "loose",
                description: "ball settled loose (play ended without a catch)",
                point: transform.position);
        }

        private static string Label(PlayerZoneTracker p) =>
            p == null ? "?" : $"{(p.Spawn.team == Team.A ? "A" : "B")}{p.Number}";
    }
}
