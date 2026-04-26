using System;
using UnityEngine;

namespace Sportland.Sports.Demoball
{
    /// <summary>
    /// A Demoball ball. Owns its own state machine from Dormant through to Scored or OutOfPlay.
    ///
    /// Pickup rules (enforced via CanBePickedUpBy):
    ///   Fresh ball (WasEverCarried = false): Scorers only.
    ///   Dropped/fumbled ball (WasEverCarried = true): any offensive player (Scorer or Blocker).
    ///   Defense recovery: Defenders call PickUp → ball is immediately removed from play.
    ///
    /// Touch-down is initiated externally by DemoballMovementController.TryTouchDown(),
    /// which calls Score() here and fires the event the game manager listens to.
    /// </summary>
    public class Ball : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        //  STATE
        // ──────────────────────────────────────────────

        public enum BallState
        {
            Dormant,    // not yet activated; invisible and inert
            Launching,  // airborne from cannon — cannot be picked up
            InFlight,   // mid-air pass between two players — cannot be picked up
            Loose,      // on the ground; pickup eligibility depends on role rules
            Carried,    // held by a player
            Scored,     // touched down successfully; triggers replacement countdown
            OutOfPlay   // removed from game (defense recovery, knocked out of bounds, or period end)
        }

        // ──────────────────────────────────────────────
        //  CONFIGURATION
        // ──────────────────────────────────────────────

        [Tooltip("Duration of the launch arc animation before the ball lands (seconds).")]
        [SerializeField] private float launchDuration = 0.6f;

        [Tooltip("Time (seconds, including the launch arc) during which a fresh ball is " +
                 "off-limits to the defense. Lifts as soon as offense touches the ball.")]
        [SerializeField] private float freshWindowDuration = 4f;

        // ──────────────────────────────────────────────
        //  RUNTIME STATE
        // ──────────────────────────────────────────────

        public BallState State { get; private set; } = BallState.Dormant;

        /// <summary>
        /// True once a Scorer has possessed this ball.
        /// Unlocks pickup for Blockers if the ball is subsequently dropped.
        /// </summary>
        public bool WasEverCarried { get; private set; }

        /// <summary>
        /// True while the offense's setup window is active — ball has been
        /// introduced but no offensive player has touched it yet, and the
        /// freshWindowDuration timer has not expired. The defense is forbidden
        /// from picking up the ball during this window, and DefenderAi keeps
        /// its minimum exclusion distance.
        /// </summary>
        public bool IsFresh => !WasEverCarried && freshTimer > 0f;

        /// <summary>Seconds remaining on the fresh-ball window (0 once expired).</summary>
        public float FreshTimeRemaining => Mathf.Max(0f, freshTimer);

        /// <summary>The player currently carrying this ball, or null.</summary>
        public DemoballMovementController Carrier { get; private set; }

        // ──────────────────────────────────────────────
        //  EVENTS
        // ──────────────────────────────────────────────

        public event Action<Ball> OnPickedUp;
        public event Action<Ball> OnDropped;
        public event Action<Ball, bool> OnScored;           // bool = wasInBonusZone
        public event Action<Ball> OnRemovedFromPlay;
        /// <summary>Fired when an in-flight pass is successfully caught. Argument = receiver.</summary>
        public event Action<Ball, DemoballMovementController> OnPassCaught;
        /// <summary>Fired when an in-flight pass lands without being caught and goes Loose.</summary>
        public event Action<Ball> OnPassFailed;

        // ──────────────────────────────────────────────
        //  PRIVATE
        // ──────────────────────────────────────────────

        private float launchTimer;
        private Vector3 launchStart;
        private Vector3 launchTarget;
        private SpriteRenderer spriteRenderer;
        private float freshTimer;

        // Pass animation runtime
        private float passTimer;
        private float passDuration;
        private float passPeakHeight;
        private Vector3 passOrigin;
        private Vector3 passDestination;
        private DemoballMovementController passTarget;
        private float currentHeight;     // Y offset above ground while airborne (0 on the field)

        /// <summary>How high above the ground the ball currently is (world units). 0 when on the field.</summary>
        public float Height => currentHeight;

        /// <summary>Maximum distance from the target's current position at arrival that still counts as a catch.</summary>
        [Tooltip("Catch radius: if the receiver is within this many world units of the ball at arrival, the catch lands.")]
        [SerializeField] private float catchRadius = 1.2f;

        // ──────────────────────────────────────────────
        //  UNITY LIFECYCLE
        // ──────────────────────────────────────────────

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            SetVisible(false);
        }

        private void Update()
        {
            // Fresh-ball window ticks while the ball is on the field
            // (Launching or Loose) — the GameObject is inactive in other
            // states so Update wouldn't run anyway.
            if (freshTimer > 0f) freshTimer -= Time.deltaTime;

            switch (State)
            {
                case BallState.Launching: UpdateLaunching(); break;
                case BallState.InFlight:  UpdateInFlight();  break;
            }
        }

        private void UpdateLaunching()
        {
            launchTimer += Time.deltaTime;
            float t = Mathf.Clamp01(launchTimer / launchDuration);

            Vector3 flat = Vector3.Lerp(launchStart, launchTarget, t);
            float arc    = Mathf.Sin(t * Mathf.PI) * 1.5f;
            currentHeight = arc;
            transform.position = flat + new Vector3(0f, arc, 0f);

            if (t >= 1f) Land();
        }

        private void UpdateInFlight()
        {
            passTimer += Time.deltaTime;
            float t = Mathf.Clamp01(passTimer / passDuration);

            Vector3 flat = Vector3.Lerp(passOrigin, passDestination, t);
            float arc    = Mathf.Sin(t * Mathf.PI) * passPeakHeight;
            currentHeight = arc;
            transform.position = flat + new Vector3(0f, arc, 0f);

            if (t >= 1f) ResolvePassArrival();
        }

        // ──────────────────────────────────────────────
        //  ACTIVATION
        // ──────────────────────────────────────────────

        /// <summary>
        /// Called by BallCannon. Animates an arc from the cannon to the landing position.
        /// </summary>
        public void Launch(Vector3 fromPosition, Vector3 toPosition)
        {
            if (State != BallState.Dormant) return;

            launchStart  = fromPosition;
            launchTarget = toPosition;
            launchTimer  = 0f;

            // Fresh window covers the airborne arc plus the configured setup
            // time once the ball lands, so the offense gets the full window
            // counted from when they could realistically reach the ball.
            freshTimer   = freshWindowDuration + launchDuration;

            transform.position = fromPosition;
            SetVisible(true);
            State = BallState.Launching;
        }

        private void Land()
        {
            transform.position = launchTarget;
            State = BallState.Loose;
            WasEverCarried = false;
            // TODO: spawn land VFX / play thud SFX
        }

        // ──────────────────────────────────────────────
        //  PICKUP RULES
        // ──────────────────────────────────────────────

        /// <summary>
        /// Returns true if the given player is allowed to interact with this ball under current rules.
        /// </summary>
        public bool CanBePickedUpBy(DemoballMovementController player)
        {
            if (State != BallState.Loose) return false;

            switch (player.Role)
            {
                case DemoballRole.Defender:
                    // Defense is locked out during the offense's setup window.
                    return !IsFresh;

                case DemoballRole.Scorer:
                    return true; // scorers can always initiate

                case DemoballRole.Blocker:
                    return WasEverCarried; // blockers only after a scorer has touched it

                default:
                    return false;
            }
        }

        // ──────────────────────────────────────────────
        //  CARRY / DROP
        // ──────────────────────────────────────────────

        /// <summary>
        /// Pick up this ball. For Defenders, immediately removes the ball from play.
        /// Returns true if pickup succeeded.
        /// </summary>
        public bool PickUp(DemoballMovementController player)
        {
            if (!CanBePickedUpBy(player)) return false;

            if (player.Role == DemoballRole.Defender)
            {
                RemoveFromPlay();
                return true;
            }

            Carrier = player;
            WasEverCarried = true;
            State = BallState.Carried;
            SetVisible(false); // visual representation moves to the player
            OnPickedUp?.Invoke(this);
            return true;
        }

        /// <summary>Drops the ball at the given world position (e.g. after a tackle).</summary>
        public void Drop(Vector2 position)
        {
            if (State != BallState.Carried) return;

            Carrier = null;
            transform.position = new Vector3(position.x, position.y, transform.position.z);
            State = BallState.Loose;
            SetVisible(true);
            OnDropped?.Invoke(this);
        }

        /// <summary>
        /// Direct carrier handoff. Used both for instant transfers from the Carried
        /// state and to land an in-flight pass on the receiver. Caller is responsible
        /// for updating the receiver's HeldBall.
        /// </summary>
        public void TransferTo(DemoballMovementController newCarrier)
        {
            if (State != BallState.Carried && State != BallState.InFlight) return;
            Carrier = newCarrier;
            State = BallState.Carried;
            currentHeight = 0f;
            SetVisible(false);
        }

        // ──────────────────────────────────────────────
        //  PASS  (animated, may be caught or dropped)
        // ──────────────────────────────────────────────

        /// <summary>
        /// Launches the carried ball on an arc toward the target's current position.
        /// On arrival the ball attempts a catch — success transfers possession to
        /// the target; failure drops the ball at the destination as Loose.
        ///
        /// Caller is expected to clear the original carrier's HeldBall before calling.
        /// </summary>
        public void Pass(Vector3 fromPosition, DemoballMovementController target,
                         float peakHeight, float duration)
        {
            if (State != BallState.Carried) return;
            if (target == null) return;

            passOrigin       = fromPosition;
            passDestination  = target.transform.position;
            passTarget       = target;
            passPeakHeight   = Mathf.Max(0f, peakHeight);
            passDuration     = Mathf.Max(0.05f, duration);
            passTimer        = 0f;
            currentHeight    = 0f;

            Carrier = null;
            State   = BallState.InFlight;
            transform.position = fromPosition;
            SetVisible(true);   // re-activates the GameObject so Update runs the arc
        }

        private void ResolvePassArrival()
        {
            var target = passTarget;
            passTarget = null;
            currentHeight = 0f;
            transform.position = passDestination;

            if (target != null && CanCatch(target))
            {
                target.ReceivePass(this);          // TransferTo + HeldBall + OnBallPickedUp
                OnPassCaught?.Invoke(this, target);
                return;
            }

            // Miss — ball lands as a loose ball at the destination
            State = BallState.Loose;
            // SpriteRenderer is already enabled and the GameObject is active from InFlight.
            OnPassFailed?.Invoke(this);
        }

        private bool CanCatch(DemoballMovementController target)
        {
            if (target.NeedsTagUp || target.IsCarryingBall) return false;
            if (target.Role == DemoballRole.Defender) return false;

            float dist = Vector2.Distance(target.transform.position, transform.position);
            return dist <= catchRadius;
        }

        // ──────────────────────────────────────────────
        //  SCORE / REMOVE
        // ──────────────────────────────────────────────

        /// <summary>
        /// Called when the carrier successfully touches the ball down in the scoring ring.
        /// The game manager subscribes to OnScored to attribute points.
        /// </summary>
        public void Score(bool inBonusZone)
        {
            if (State != BallState.Carried) return;

            Carrier = null;
            State = BallState.Scored;
            SetVisible(false);
            OnScored?.Invoke(this, inBonusZone);
        }

        /// <summary>
        /// Removes this ball from play without scoring (defense recovery, knocked out of bounds,
        /// or period end cleanup).
        /// </summary>
        public void RemoveFromPlay()
        {
            Carrier = null;
            State = BallState.OutOfPlay;
            SetVisible(false);
            OnRemovedFromPlay?.Invoke(this);
        }

        /// <summary>Resets the ball to Dormant so BallCannon can reuse it next period.</summary>
        public void ResetToDormant()
        {
            Carrier = null;
            WasEverCarried = false;
            freshTimer = 0f;
            State = BallState.Dormant;
            SetVisible(false);
        }

        // ──────────────────────────────────────────────
        //  UTILITY
        // ──────────────────────────────────────────────

        private void SetVisible(bool visible)
        {
            if (spriteRenderer != null)
                spriteRenderer.enabled = visible;

            // Also toggle the GameObject so the ball fully leaves the field
            // when carried / scored / out of play. BallCannon and pass logic
            // still hold a reference and can call public methods to bring it back.
            if (gameObject.activeSelf != visible)
                gameObject.SetActive(visible);
        }
    }
}
