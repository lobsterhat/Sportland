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

        // ──────────────────────────────────────────────
        //  RUNTIME STATE
        // ──────────────────────────────────────────────

        public BallState State { get; private set; } = BallState.Dormant;

        /// <summary>
        /// True once a Scorer has possessed this ball.
        /// Unlocks pickup for Blockers if the ball is subsequently dropped.
        /// </summary>
        public bool WasEverCarried { get; private set; }

        /// <summary>The player currently carrying this ball, or null.</summary>
        public DemoballMovementController Carrier { get; private set; }

        // ──────────────────────────────────────────────
        //  EVENTS
        // ──────────────────────────────────────────────

        public event Action<Ball> OnPickedUp;
        public event Action<Ball> OnDropped;
        public event Action<Ball, bool> OnScored;           // bool = wasInBonusZone
        public event Action<Ball> OnRemovedFromPlay;

        // ──────────────────────────────────────────────
        //  PRIVATE
        // ──────────────────────────────────────────────

        private float launchTimer;
        private Vector3 launchStart;
        private Vector3 launchTarget;
        private SpriteRenderer spriteRenderer;

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
            if (State != BallState.Launching) return;

            launchTimer += Time.deltaTime;
            float t = Mathf.Clamp01(launchTimer / launchDuration);

            // Simple arc: lerp XZ, parabola on Y
            Vector3 flat = Vector3.Lerp(launchStart, launchTarget, t);
            float arc    = Mathf.Sin(t * Mathf.PI) * 1.5f;  // peak height in world units
            transform.position = flat + new Vector3(0f, arc, 0f);

            if (t >= 1f)
                Land();
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
                    return true; // defense recovers → ball out of play

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
        }
    }
}
