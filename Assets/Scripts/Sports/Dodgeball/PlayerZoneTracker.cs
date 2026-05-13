using System.Collections.Generic;
using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Tracks whether a player is in their assigned zone and enforces:
    ///   - 3-second return window
    ///   - Crossing-count warning (3 crossings in 30s, rolling window)
    ///   - "Cannot catch while out of zone"
    ///   - "Holding ball while out of zone = turnover"
    ///   - Jump exception: while airborne, a player holding the ball may
    ///     cross a line and throw before landing without triggering turnover.
    ///
    /// Penalty-after-warning behavior is intentionally left as a hook
    /// (OnPenaltyTriggered) — wire it once we decide the penalty.
    /// </summary>
    [RequireComponent(typeof(PlayerMovement))]
    public class PlayerZoneTracker : MonoBehaviour
    {
        // --- Tunables ---------------------------------------------------------
        [Header("Rule timings")]
        [SerializeField] private float returnGraceSeconds = 3f;
        [SerializeField] private int   crossingsForWarning = 3;
        [SerializeField] private float crossingWindowSeconds = 30f;

        // --- Assignment -------------------------------------------------------
        [Header("Assignment (set on spawn)")]
        public PlayerSpawn Spawn;
        public PlayZone AssignedZone;

        // --- Runtime state ---------------------------------------------------
        public bool IsInZone { get; private set; } = true;
        public bool HasBall { get; set; }
        public bool HasActiveWarning { get; private set; }

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
            movement.OnLanded += HandleLanded;
        }

        private void OnDestroy()
        {
            if (movement != null) movement.OnLanded -= HandleLanded;
        }

        public void Initialize(PlayerSpawn spawn)
        {
            Spawn = spawn;
            AssignedZone = ZoneFactory.For(spawn);
            IsInZone = AssignedZone.Contains(transform.position);
            outOfZoneSince = IsInZone ? -1f : Time.time;
            returnExpiryFired = false;
        }

        private void HandleLanded(PlayerMovement _)
        {
            // The airborne exception ends here: if we land out-of-zone still
            // holding the ball, it's a turnover.
            if (!IsInZone && HasBall)
            {
                OnTurnoverFromOutOfZoneWithBall?.Invoke(this);
            }
        }

        private void Update()
        {
            UpdateZoneState();
            PruneOldCrossings();
            CheckReturnTimer();
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

                // Holding the ball while out of zone is a turnover —
                // UNLESS the player is airborne (jump-over-line exception).
                // The airborne case is re-checked in HandleLanded: release
                // before landing = no turnover; still holding on landing = turnover.
                if (HasBall && !movement.IsAirborne)
                {
                    OnTurnoverFromOutOfZoneWithBall?.Invoke(this);
                }

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

                // If still holding the ball at expiry, that's also a turnover
                // (covers the case where they left without the ball, then
                // picked one up while out of zone).
                if (HasBall && !movement.IsAirborne)
                {
                    OnTurnoverFromOutOfZoneWithBall?.Invoke(this);
                }
            }
        }

        /// <summary>
        /// Called by the ball/catch system before processing a catch attempt.
        /// Returns false if the player isn't allowed to catch right now.
        /// </summary>
        public bool CanCatchBall()
        {
            // Restricted area = anywhere outside your assigned zone.
            return IsInZone;
        }

        /// <summary>
        /// For debugging — number of crossings in the current rolling window.
        /// </summary>
        public int CurrentCrossingCount => crossingTimestamps.Count;

        public float SecondsUntilReturnExpiry =>
            IsInZone ? 0f : Mathf.Max(0f, returnGraceSeconds - (Time.time - outOfZoneSince));
    }
}
