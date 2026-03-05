using UnityEngine;
using Sportland.Sports.Tag;
using Sportland.Movement;

namespace Sportland.InputHandling
{
    /// <summary>
    /// Sits on every character. Routes input from the active IInputSource
    /// to the movement controller. Swap control at runtime by calling
    /// SetInputSource().
    /// 
    /// The movement controller never knows who's driving — it just
    /// receives commands. This enables seamless player/AI swapping
    /// for ball possession, tag transfers, or manual player switching.
    /// 
    /// Setup:
    ///   1. Attach to same GameObject as TagMovementController
    ///   2. Configure default input type and layer masks in Inspector
    ///   3. Call SetInputSource() at runtime to swap control
    /// </summary>
    [RequireComponent(typeof(TagMovementController))]
    public class InputBroker : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        //  CONFIGURATION
        // ──────────────────────────────────────────────

        public enum DefaultInputType { Player, AI }

        [Header("=== DEFAULT INPUT ===")]
        [Tooltip("Which input source this character starts with.")]
        [SerializeField] private DefaultInputType defaultInput = DefaultInputType.AI;

        [Header("=== AI CONFIGURATION ===")]
        [Tooltip("Layer mask for finding other players (used by AI).")]
        [SerializeField] private LayerMask playerLayer;

        [Tooltip("Layer mask for walls/obstacles (used by AI).")]
        [SerializeField] private LayerMask wallLayer;

        // ──────────────────────────────────────────────
        //  RUNTIME STATE
        // ──────────────────────────────────────────────

        private IInputSource currentSource;
        private TagMovementController movement;

        /// <summary>
        /// True if currently controlled by a human player.
        /// </summary>
        public bool IsPlayerControlled => currentSource is PlayerInputSource;

        /// <summary>
        /// The currently active input source.
        /// </summary>
        public IInputSource CurrentSource => currentSource;

        // ──────────────────────────────────────────────
        //  INITIALIZATION
        // ──────────────────────────────────────────────

        private void Awake()
        {
            movement = GetComponent<TagMovementController>();
        }

        private void Start()
        {
            // Create and activate default input source
            IInputSource source;

            if (defaultInput == DefaultInputType.Player)
            {
                source = new PlayerInputSource();
            }
            else
            {
                source = new AIInputSource(playerLayer, wallLayer);
            }

            SetInputSource(source);
        }

        // ──────────────────────────────────────────────
        //  INPUT SOURCE MANAGEMENT
        // ──────────────────────────────────────────────

        /// <summary>
        /// Swap to a new input source. Deactivates the old source,
        /// activates the new one. This is the core of player/AI swapping.
        /// 
        /// Example usage from a game manager:
        ///   oldPlayer.GetComponent&lt;InputBroker&gt;().SetInputSource(new AIInputSource(...));
        ///   newPlayer.GetComponent&lt;InputBroker&gt;().SetInputSource(new PlayerInputSource());
        /// </summary>
        public void SetInputSource(IInputSource newSource)
        {
            // Deactivate old source
            currentSource?.OnDeactivate();

            // Activate new source
            currentSource = newSource;
            currentSource?.OnActivate(gameObject);
        }

        /// <summary>
        /// Convenience: swap this character to player control.
        /// </summary>
        public void SwapToPlayer()
        {
            SetInputSource(new PlayerInputSource());
        }

        /// <summary>
        /// Convenience: swap this character to AI control.
        /// </summary>
        public void SwapToAI()
        {
            SetInputSource(new AIInputSource(playerLayer, wallLayer));
        }

        // ──────────────────────────────────────────────
        //  UPDATE LOOP
        // ──────────────────────────────────────────────

        private void Update()
        {
            if (currentSource == null) return;
            if (movement.IsEliminated) return;

            // Let the source process input (read keyboard, run AI logic, etc.)
            currentSource.UpdateInput();

            // Feed input to movement controller
            movement.SetMoveInput(currentSource.GetMoveInput());
            movement.SetSprinting(currentSource.IsSprinting());
            movement.SetShuffling(currentSource.IsShuffling());

            // Single-frame actions
            if (currentSource.JumpRequested())
                movement.TryJump();

            if (currentSource.DiveRequested())
                movement.TryDive();

            if (currentSource.SpecialRequested())
            {
                if (movement.CurrentRole == TagMovementController.TagRole.It)
                    movement.TryLunge();
                else
                    movement.TryEvasionBurst();
            }

            if (currentSource.TagRequested())
                movement.TryTag();
        }
    }
}