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
        [Tooltip("Delay (s) between a carrier crossing into the opposing infield and the forced drop firing — gives the player 'a step or two' so the release looks natural rather than instantaneous.")]
        [SerializeField] private float carrierDropDelay = 0.3f;

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

        // Opposing-infield state (applies to both roles):
        //   wasInOppInfieldGrounded — true if the player was grounded inside
        //     the opposing infield last tick. Used to detect grounded-entry
        //     transitions; airborne fly-overs (jump/dive) don't trigger.
        //   hadBallLastTick — used to detect the just-picked-up transition,
        //     so the 2 s "pass it out" timer starts on PICKUP-in-opp-infield
        //     rather than on entry. A player who jumps the line empty and
        //     throws before landing never picks up here, so never trips this.
        //   pendingDropAt — unified deadline for the eventual drop. Set to
        //     entry-time + carrierDropDelay (~0.3 s) when a carrier crosses
        //     in with the ball, or to pickup-time + ReturnGraceSeconds (2 s)
        //     when a player picks up a loose ball while already inside.
        //     Cancelled if they retreat / lose the ball before it fires.
        //     Negative = no drop pending.
        //   pickupLockedInOppInfield — true after the rules forced a drop
        //     this visit. Prevents the dropper from re-grabbing the loose ball
        //     at their feet and looping. Cleared only when they make it back
        //     to their own zone.
        private bool wasInOppInfieldGrounded;
        private bool hadBallLastTick;
        private float pendingDropAt = -1f;
        private bool pickupLockedInOppInfield;

        /// <summary>True if a recent opp-infield drop has locked this player out of loose-ball pickup until they leave / jump. Read by Ball.TryTakeBall to break the drop/re-grab loop. Suppressed while debug TimersDisabled is on so stale lockout state doesn't block test pickups.</summary>
        public bool PickupLockedInOppInfield => pickupLockedInOppInfield && !DodgeballTuningPanel.TimersDisabled;

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
            hadBallLastTick = HasBall;
            pendingDropAt = -1f;
            pickupLockedInOppInfield = false;
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
            if (DodgeballTuningPanel.TimersDisabled) return;

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
        // Players standing in the opposing infield are handled by
        // EnforceOpposingInfieldRules instead — that path fires an immediate
        // drop on entry-with-ball (turnover for outfielders, -1 for infielders)
        // and a drop at 2 s expiry-with-ball, so we skip the general -1 path
        // here to avoid double-firing.
        private void EnforceCarrierInZone()
        {
            if (IsInOpposingInfield) return;
            if (DodgeballTuningPanel.TimersDisabled) return;

            if (HasBall && !IsInZone && movement.IsGrounded
                && outOfZoneSince >= 0f && Time.time - outOfZoneSince >= ReturnGraceSeconds)
                DropHeldBall();
        }

        // Two distinct scenarios fire a drop, with different deadlines:
        //
        // (a) CARRIER crosses into opp infield with the ball → deferred 0.3 s
        //     drop (carrierDropDelay) so the release looks natural — the
        //     player walks a step or two in and the ball slides out with their
        //     momentum rather than freezing at the line.
        //
        // (b) Player PICKS UP a loose ball while standing inside opp infield
        //     → 2 s window (ReturnGraceSeconds) to pass it out. The timer
        //     starts at the pickup moment, NOT at entry — a player who jumps
        //     the line, throws mid-air, and lands empty in opp infield never
        //     trips it because they never pick up. No advantage to lingering
        //     empty-handed, but also no penalty.
        //
        // No point penalty for either path (just turnover / possession swing).
        //
        // After any forced drop the player is locked out of loose-ball pickup
        // until they make it back to their OWN zone — they can't re-grab the
        // ball at their feet, can't grab it from a teammate's strip, can't
        // jump-escape to it. Has to walk home first.
        //
        // Airborne fly-overs (jump / dive) don't trigger or progress the rules
        // — but they DON'T clear the lockout either; only re-entering own zone
        // does.
        private void EnforceOpposingInfieldRules()
        {
            if (movement == null) return;
            if (DodgeballTuningPanel.TimersDisabled) return;

            bool nowInOpp = IsInOpposingInfield;
            bool grounded = movement.IsGrounded;

            // Lockout clears only when back home in own zone — neutral regions
            // and airborne states don't qualify; the player has to physically
            // return to their assigned zone.
            if (IsInZone) pickupLockedInOppInfield = false;

            if (!grounded)
            {
                hadBallLastTick = HasBall;   // keep tracking through airborne so transitions are accurate on landing
                return;
            }

            bool justEntered = nowInOpp && !wasInOppInfieldGrounded;
            bool justGotBall = HasBall && !hadBallLastTick;

            // Carrier crossed in with the ball → deferred natural release.
            if (justEntered && HasBall && pendingDropAt < 0f)
            {
                pendingDropAt = Time.time + carrierDropDelay;
            }
            // Picked up the ball while standing inside opp infield → 2 s window.
            else if (nowInOpp && justGotBall && pendingDropAt < 0f)
            {
                pendingDropAt = Time.time + ReturnGraceSeconds;
            }

            // Cancel a pending drop if the player retreated back out, or
            // lost the ball before the deadline expired.
            if (pendingDropAt >= 0f && (!nowInOpp || !HasBall))
                pendingDropAt = -1f;

            if (pendingDropAt >= 0f && Time.time >= pendingDropAt && HasBall)
            {
                DropAsTurnover();
                pickupLockedInOppInfield = true;
                pendingDropAt = -1f;
            }

            hadBallLastTick = HasBall;
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
        // so the match scorer can log it without applying -1. The ball
        // inherits the player's velocity so it slides forward naturally with
        // their motion instead of freezing at their feet.
        private void DropAsTurnover()
        {
            var ball = HeldBall;
            if (ball == null) return;
            Vector2 carrierVel = movement != null ? movement.Velocity : Vector2.zero;
            OnAnyTurnover?.Invoke(this);
            Debug.Log($"[Dodgeball] {Spawn.id} turnover — carrier crossed into the opposing infield with the ball.");
            ball.Drop(carrierVel);
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
