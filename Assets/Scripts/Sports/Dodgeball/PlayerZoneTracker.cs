using System.Collections.Generic;
using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Tracks whether a player is in their assigned zone and enforces:
    ///   - Crossing-count warning (3 crossings in 30s, rolling window)
    ///   - Loose-ball retrieval is allowed anywhere; carry it home within the
    ///     return window or drop it (EnforceCarrierInZone)
    ///   - No releasing the ball (throw/pass) while grounded out of your area
    ///     — an offensive play from the opponent's side — except while airborne
    ///     (the jump exception); enforced in Ball.CarrierMayRelease
    ///   - Catching a LIVE throw is illegal out of zone except a diving catch
    ///     (CanCatchBall)
    ///
    /// Penalty-after-warning behavior is intentionally left as a hook
    /// (OnPenaltyTriggered) — wire it once we decide the penalty.
    /// </summary>
    [RequireComponent(typeof(PlayerMovement))]
    public class PlayerZoneTracker : MonoBehaviour
    {
        // --- Tunables ---------------------------------------------------------
        /// <summary>Return window (seconds): how long a carrier may be out of their area before they're forced to drop the ball AND their team takes a -1 point penalty. Static so the runtime match-controls slider tunes it for all players at once.</summary>
        public static float ReturnGraceSeconds = 2f;

        [Header("Rule timings")]
        [SerializeField] private int   crossingsForWarning = 3;
        [SerializeField] private float crossingWindowSeconds = 30f;

        // --- Assignment -------------------------------------------------------
        [Header("Assignment (set on spawn)")]
        public PlayerSpawn Spawn;
        public PlayZone AssignedZone;

        /// <summary>Per-team jersey number (1-6) for on-screen identification / debugging.</summary>
        public int Number;

        // --- Runtime state ---------------------------------------------------
        public bool IsInZone { get; private set; } = true;
        public Ball HeldBall { get; set; }
        public bool HasBall => HeldBall != null;
        public bool HasActiveWarning { get; private set; }

        /// <summary>The other team. Convenience for the opposing-infield rules.</summary>
        public Team OpposingTeam => Spawn.team == Team.A ? Team.B : Team.A;
        /// <summary>The opposing team's infield zone (front-court half) — the area an outfielder may not enter while carrying.</summary>
        public PlayZone OpposingInfield => ZoneFactory.InfieldFor(OpposingTeam);
        /// <summary>True when this player is physically inside the opposing team's infield (does not gate on grounded — check IsGrounded for the rules-relevant version).</summary>
        public bool IsInOpposingInfield => OpposingInfield.Contains(transform.position);
        /// <summary>True when the player's feet are on the ground (not airborne, not diving). Convenience pass-through to PlayerMovement.</summary>
        public bool IsGrounded => movement != null && movement.IsGrounded;

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

        // Opposing-infield state (Phase B rules — only meaningful for outfielders):
        //   wasInOppInfieldGrounded — true if the player was grounded inside
        //     the opposing infield last tick. Used to detect grounded-entry
        //     transitions; airborne fly-overs (jump/dive) don't trigger.
        //   opposingInfieldEnteredAt — timestamp of the most recent grounded
        //     entry. After 2 s, if the outfielder is still grounded inside
        //     the opposing infield with the ball, the carrier turnover fires.
        private bool wasInOppInfieldGrounded;
        private float opposingInfieldEnteredAt = -1f;

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

        /// <summary>Static signal: a carrier was forced to drop the ball (return window expired). The match subscribes to apply the -1 point penalty.</summary>
        public static event System.Action<PlayerZoneTracker> OnAnyForcedDrop;

        /// <summary>Static signal: an outfielder + ball + opposing infield triggered a turnover (the ball drops loose with no point penalty). The match subscribes for play-by-play logging.</summary>
        public static event System.Action<PlayerZoneTracker> OnAnyTurnover;

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
            wasInOppInfieldGrounded = IsInOpposingInfield && movement != null && movement.IsGrounded;
            opposingInfieldEnteredAt = wasInOppInfieldGrounded ? Time.time : -1f;
        }

        private void Update()
        {
            UpdateZoneState();
            PruneOldCrossings();
            CheckReturnTimer();
            EnforceOpposingInfieldRules();   // outfielder-in-opposing-infield turnover takes precedence
            EnforceCarrierInZone();          // general carrier-out-of-zone -1 path
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

            if (Time.time - outOfZoneSince >= ReturnGraceSeconds)
            {
                returnExpiryFired = true;
                OnReturnTimerExpired?.Invoke(this);
            }
        }

        // Crossing into another area to retrieve a loose ball and carry it back is
        // fine, but you must return to your own side within the return window.
        // Holding the ball out of your area past that window drops it (loose) —
        // but only once grounded, so the jump/dive exception holds until you land.
        // The clock runs from when you left your area, so re-grabbing a dropped
        // ball out there buys no extra time (no stalling).
        //
        // Outfielders standing in the opposing infield are handled by
        // EnforceOpposingInfieldRules instead — that path fires a turnover
        // (no -1) on entry-with-ball or 2 s expiry-with-ball, so we skip the
        // general -1 path here to avoid double-firing.
        private void EnforceCarrierInZone()
        {
            if (Spawn.role == PlayerRole.Outfielder && IsInOpposingInfield) return;

            if (HasBall && !IsInZone && movement.IsGrounded
                && outOfZoneSince >= 0f && Time.time - outOfZoneSince >= ReturnGraceSeconds)
                DropHeldBall();
        }

        // Outfielder-only. Grounded entry into the opposing infield while
        // carrying the ball is an immediate turnover. Empty-handed entry starts
        // a 2 s presence window; if they're still grounded in there with the
        // ball when it expires, that's also a turnover. Airborne fly-overs
        // (jump / dive) don't trigger — the rule only watches grounded state.
        // No -1 penalty for either path — that's the difference vs the general
        // out-of-zone rule.
        private void EnforceOpposingInfieldRules()
        {
            if (Spawn.role != PlayerRole.Outfielder) return;
            if (movement == null || !movement.IsGrounded) return;

            bool nowInOpp = IsInOpposingInfield;

            if (nowInOpp && !wasInOppInfieldGrounded)
            {
                opposingInfieldEnteredAt = Time.time;
                if (HasBall) DropAsTurnover();
            }
            else if (nowInOpp && HasBall
                     && opposingInfieldEnteredAt >= 0f
                     && Time.time - opposingInfieldEnteredAt >= ReturnGraceSeconds)
            {
                DropAsTurnover();
            }

            wasInOppInfieldGrounded = nowInOpp;
        }

        private void DropHeldBall()
        {
            var ball = HeldBall;
            OnTurnoverFromOutOfZoneWithBall?.Invoke(this);   // hook for HUD / audio
            OnAnyForcedDrop?.Invoke(this);                   // penalty signal (the match deducts -1)
            Debug.Log($"[Dodgeball] {Spawn.id} didn't return the ball to their area in time — dropped it (-1 penalty).");
            if (ball != null) ball.Drop();
        }

        // The opposing-infield drop path: ball becomes loose with NO point
        // penalty — possession swing only. Distinct event from OnAnyForcedDrop
        // so the match scorer can log it without applying -1.
        private void DropAsTurnover()
        {
            var ball = HeldBall;
            OnAnyTurnover?.Invoke(this);
            Debug.Log($"[Dodgeball] {Spawn.id} turnover — outfielder caught with the ball in the opposing infield.");
            if (ball != null) ball.Drop();
        }

        /// <summary>
        /// Called by the ball/catch system before processing a catch attempt.
        /// Returns false if the player isn't allowed to catch right now.
        /// </summary>
        public bool CanCatchBall()
        {
            // Live-ball catch (interception): legal only in your own area, or
            // while airborne — a diving lunge may cross the line to make the
            // catch (you then have the return window to carry it home). Loose-ball
            // retrieval is unrestricted and handled separately (Ball.TryTakeBall).
            return IsInZone || !movement.IsGrounded;
        }

        /// <summary>
        /// For debugging — number of crossings in the current rolling window.
        /// </summary>
        public int CurrentCrossingCount => crossingTimestamps.Count;

        public float SecondsUntilReturnExpiry =>
            IsInZone ? 0f : Mathf.Max(0f, ReturnGraceSeconds - (Time.time - outOfZoneSince));
    }
}
