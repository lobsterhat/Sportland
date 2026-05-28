using System.Collections.Generic;
using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Tracks whether a player is in their assigned zone and enforces:
    ///   - 3-second return window
    ///   - Crossing-count warning (3 crossings in 30s, rolling window)
    ///   - "Cannot catch while out of zone"
    ///   - "Grounded out of your area with the ball = drop it (loose)"
    ///   - Jump exception: while airborne, a player holding the ball may
    ///     cross a line and throw/pass before landing without dropping it.
    ///
    /// Penalty-after-warning behavior is intentionally left as a hook
    /// (OnPenaltyTriggered) — wire it once we decide the penalty.
    /// </summary>
    [RequireComponent(typeof(PlayerMovement))]
    public class PlayerZoneTracker : MonoBehaviour
    {
        // --- Tunables ---------------------------------------------------------
        [Header("Rule timings")]
        [Tooltip("Seconds a player may be out of their assigned zone before the return-window timer fires (positioning rule via OnReturnTimerExpired).")]
        [SerializeField] private float returnGraceSeconds = 2f;
        [SerializeField] private int   crossingsForWarning = 3;
        [SerializeField] private float crossingWindowSeconds = 30f;

        // --- Assignment -------------------------------------------------------
        [Header("Assignment (set on spawn)")]
        public PlayerSpawn Spawn;
        public PlayZone AssignedZone;

        // --- Runtime state ---------------------------------------------------
        public bool IsInZone { get; private set; } = true;
        public Ball HeldBall { get; set; }
        public bool HasBall => HeldBall != null;
        public bool HasActiveWarning { get; private set; }

        // Time of the most recent Catch press (press-window reaction). The Ball
        // reads this to decide whether a catch attempt is "armed" and to score
        // the timing of the press relative to the ball's arrival.
        public float CatchArmedAt { get; private set; } = -999f;

        /// <summary>Records a catch press at the current time.</summary>
        public void ArmCatch() => CatchArmedAt = Time.time;

        /// <summary>True if a catch was pressed within the last <paramref name="window"/> seconds.</summary>
        public bool IsCatchArmed(float window) => Time.time - CatchArmedAt <= window;

        /// <summary>
        /// Live registry of trackers in the scene. Used by Ball for pickup
        /// proximity checks. Maintained in OnEnable/OnDisable.
        /// </summary>
        public static readonly List<PlayerZoneTracker> All = new List<PlayerZoneTracker>();

        // Time the player went out-of-zone. Negative when in-zone.
        private float outOfZoneSince = -1f;

        // True once the return-grace timer has expired on the current trip
        // out of zone. Reset when the player gets back in zone.
        private bool returnExpiryFired;

        // Timestamps of recent crossings (entering out-of-zone state).
        // Pruned to the rolling window each frame.
        private readonly Queue<float> crossingTimestamps = new Queue<float>();

        // Cache the PlayerMovement so we can check airborne state.
        private PlayerMovement movement;

        // --- Events (hook these up in the game manager) ----------------------
        public System.Action<PlayerZoneTracker> OnLeftZone;
        public System.Action<PlayerZoneTracker> OnReturnedToZone;
        public System.Action<PlayerZoneTracker> OnReturnTimerExpired; // didn't get back in 3s
        public System.Action<PlayerZoneTracker> OnTurnoverFromOutOfZoneWithBall;
        public System.Action<PlayerZoneTracker> OnWarningIssued;
        public System.Action<PlayerZoneTracker> OnPenaltyTriggered;   // post-warning offense

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();
        }

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        public void Initialize(PlayerSpawn spawn)
        {
            Spawn = spawn;
            AssignedZone = ZoneFactory.For(spawn);
            IsInZone = AssignedZone.Contains(transform.position);
            outOfZoneSince = IsInZone ? -1f : Time.time;
            returnExpiryFired = false;
        }

        private void Update()
        {
            UpdateZoneState();
            PruneOldCrossings();
            CheckReturnTimer();
            EnforceCarrierInZone();
        }

        private void UpdateZoneState()
        {
            bool inZoneNow = AssignedZone.Contains(transform.position);

            if (inZoneNow && !IsInZone)
            {
                // Just returned.
                IsInZone = true;
                outOfZoneSince = -1f;
                returnExpiryFired = false;
                OnReturnedToZone?.Invoke(this);
            }
            else if (!inZoneNow && IsInZone)
            {
                // Just left — record a crossing.
                IsInZone = false;
                outOfZoneSince = Time.time;
                RecordCrossing();

                // (Being grounded out of your area while holding the ball is
                // handled per-frame by EnforceCarrierInZone — you drop it.)
                OnLeftZone?.Invoke(this);
            }
        }

        private void RecordCrossing()
        {
            crossingTimestamps.Enqueue(Time.time);

            if (crossingTimestamps.Count >= crossingsForWarning)
            {
                if (!HasActiveWarning)
                {
                    HasActiveWarning = true;
                    OnWarningIssued?.Invoke(this);
                }
                else
                {
                    // Already warned — escalate to penalty.
                    OnPenaltyTriggered?.Invoke(this);
                }
            }
        }

        private void PruneOldCrossings()
        {
            float cutoff = Time.time - crossingWindowSeconds;
            while (crossingTimestamps.Count > 0 && crossingTimestamps.Peek() < cutoff)
            {
                crossingTimestamps.Dequeue();
            }

            // Warning lifts once the rolling window no longer holds enough
            // crossings to justify it. Re-crossing the threshold later issues
            // a fresh warning rather than jumping straight to penalty.
            if (HasActiveWarning && crossingTimestamps.Count < crossingsForWarning)
            {
                HasActiveWarning = false;
            }
        }

        private void CheckReturnTimer()
        {
            if (IsInZone || outOfZoneSince < 0f || returnExpiryFired) return;

            if (Time.time - outOfZoneSince >= returnGraceSeconds)
            {
                returnExpiryFired = true;
                OnReturnTimerExpired?.Invoke(this);
            }
        }

        // A carrier may be out of their area only while airborne — the jump/dive
        // exception that lets them release a throw or pass over the line. The
        // instant they're grounded out of their area still holding the ball they
        // drop it (it goes loose); they can't pick it back up until they're home
        // (CanCatchBall), so the opposing team in that area recovers it.
        private void EnforceCarrierInZone()
        {
            if (HasBall && !IsInZone && movement.IsGrounded)
                DropHeldBall();
        }

        private void DropHeldBall()
        {
            var ball = HeldBall;
            OnTurnoverFromOutOfZoneWithBall?.Invoke(this);   // hook for HUD / audio
            Debug.Log($"[Dodgeball] {Spawn.id} grounded out of their area with the ball — dropped it.");
            if (ball != null) ball.Drop();
        }

        /// <summary>
        /// Called by the ball/catch system before processing a catch attempt.
        /// Returns false if the player isn't allowed to catch right now.
        /// </summary>
        public bool CanCatchBall()
        {
            // Restricted area = anywhere outside your assigned zone — but a
            // diving lunge that crosses the line is legal while you're still
            // airborne (not grounded). You only drop the ball once you touch
            // down out of zone, so a dive can still snag a ball just past it.
            return IsInZone || !movement.IsGrounded;
        }

        /// <summary>
        /// For debugging — number of crossings in the current rolling window.
        /// </summary>
        public int CurrentCrossingCount => crossingTimestamps.Count;

        public float SecondsUntilReturnExpiry =>
            IsInZone ? 0f : Mathf.Max(0f, returnGraceSeconds - (Time.time - outOfZoneSince));
    }
}
