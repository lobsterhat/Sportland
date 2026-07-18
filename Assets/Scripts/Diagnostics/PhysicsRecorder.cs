using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Sportland.Sports.Dodgeball;

namespace Sportland.Diagnostics
{
    /// <summary>
    /// Keeps a rolling window of physics state. Fixed-size ring buffer — no
    /// allocation growth over a long session.
    ///
    /// Attach to a scene object. Register bodies you care about via Track().
    /// Report events from your own game logic via RecordEvent().
    /// </summary>
    public class PhysicsRecorder : MonoBehaviour
    {
        public static PhysicsRecorder Instance { get; private set; }

        [Tooltip("Seconds of history to retain.")]
        public float windowSeconds = 3f;

        [Tooltip("Samples per second. 20 is plenty — you're describing motion, not reproducing it.")]
        public int sampleRate = 20;

        private Frame[] _ring;
        private int _head;
        private int _count;
        private float _sampleInterval;
        private float _nextSample;

        // Grounded/height come from the game's own components, not the
        // rigidbody: this is a top-down game, so rb velocity is court-plane
        // movement and says nothing about the vertical axis. Players simulate
        // their jump arc in PlayerMovement; the ball simulates flight height
        // in Ball. Both are cached at Track() time.
        private struct TrackedBody
        {
            public Rigidbody2D rb;
            public PlayerMovement mover;   // players
            public Ball ball;              // the dodgeball
        }

        private readonly List<TrackedBody> _tracked = new List<TrackedBody>();
        private readonly List<GameEvent> _pendingEvents = new List<GameEvent>();

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            int capacity = Mathf.Max(1, Mathf.CeilToInt(windowSeconds * sampleRate));
            _ring = new Frame[capacity];
            _sampleInterval = 1f / sampleRate;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Start tracking a body. Safe to call repeatedly.</summary>
        public void Track(Rigidbody2D body)
        {
            if (body == null) return;
            for (int i = 0; i < _tracked.Count; i++)
                if (_tracked[i].rb == body) return;

            _tracked.Add(new TrackedBody
            {
                rb = body,
                mover = body.GetComponent<PlayerMovement>(),
                ball = body.GetComponent<Ball>(),
            });
        }

        public void Untrack(Rigidbody2D body)
        {
            for (int i = 0; i < _tracked.Count; i++)
                if (_tracked[i].rb == body) { _tracked.RemoveAt(i); return; }
        }

        /// <summary>
        /// Record something that happened. Call this from your turnover logic,
        /// your scoring logic — anywhere a discrete thing occurs.
        /// </summary>
        public void RecordEvent(string kind, string description,
                                Vector2 point = default,
                                Vector2 relativeVelocity = default,
                                float impactSpeed = 0f)
        {
            _pendingEvents.Add(new GameEvent
            {
                time = Time.time,
                kind = kind,
                description = description,
                point = point,
                relativeVelocity = relativeVelocity,
                impactSpeed = impactSpeed,
            });
        }

        void FixedUpdate()
        {
            if (Time.time < _nextSample) return;
            _nextSample = Time.time + _sampleInterval;

            var bodies = new List<BodyState>(_tracked.Count);
            for (int i = 0; i < _tracked.Count; i++)
            {
                var t = _tracked[i];
                if (t.rb == null) continue;

                bodies.Add(new BodyState
                {
                    name = t.rb.gameObject.name,
                    position = t.rb.position,
                    velocity = t.rb.linearVelocity,
                    angularVelocity = t.rb.angularVelocity,
                    speed = t.rb.linearVelocity.magnitude,
                    height = Height(t),
                    isGrounded = IsGrounded(t),
                });
            }

            List<GameEvent> events = null;
            if (_pendingEvents.Count > 0)
            {
                events = new List<GameEvent>(_pendingEvents);
                _pendingEvents.Clear();
            }

            _ring[_head] = new Frame { time = Time.time, bodies = bodies, events = events };
            _head = (_head + 1) % _ring.Length;
            if (_count < _ring.Length) _count++;
        }

        // Players: the movement controller's own check (airborne/diving).
        // Ball: its simulated flight height. Anything else: assume grounded —
        // there is no gravity on the court plane.
        private static bool IsGrounded(TrackedBody t)
        {
            if (t.mover != null) return t.mover.IsGrounded;
            if (t.ball != null) return t.ball.Height <= 0.01f;
            return true;
        }

        private static float Height(TrackedBody t)
        {
            if (t.mover != null) return t.mover.CurrentJumpHeight;
            if (t.ball != null) return t.ball.Height;
            return 0f;
        }

        /// <summary>
        /// Flatten the ring into a text transcript. Text rather than JSON on purpose:
        /// it's about a third of the tokens and the model reads it just as well.
        /// </summary>
        public string BuildTranscript()
        {
            var sb = new StringBuilder(4096);
            sb.AppendLine($"Physics transcript — last {windowSeconds}s at {sampleRate}Hz");
            sb.AppendLine("Top-down 2D court: pos/vel are the court plane (X long side, Y short side), " +
                          "h is the separately simulated vertical (jump arc / ball flight).");
            sb.AppendLine($"fixedDeltaTime: {Time.fixedDeltaTime}");
            sb.AppendLine();

            int start = (_head - _count + _ring.Length) % _ring.Length;
            float t0 = _count > 0 ? _ring[start].time : 0f;

            for (int i = 0; i < _count; i++)
            {
                var frame = _ring[(start + i) % _ring.Length];
                if (frame.bodies == null) continue;

                sb.Append($"t+{(frame.time - t0):F3}  ");
                for (int b = 0; b < frame.bodies.Count; b++)
                {
                    if (b > 0) sb.Append("  |  ");
                    sb.Append(frame.bodies[b].ToString());
                }
                sb.AppendLine();

                if (frame.events != null)
                    foreach (var e in frame.events)
                        sb.AppendLine("        " + e.ToString());
            }

            return sb.ToString();
        }
    }
}
