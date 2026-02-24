using UnityEngine;

namespace Sportland.InputHandling
{
    /// <summary>
    /// Interface for any input source that can drive a character.
    /// Implemented by both player (keyboard/controller) and AI input sources.
    /// 
    /// The movement controller doesn't know or care who's driving —
    /// it just receives commands through this interface.
    /// </summary>
    public interface IInputSource
    {
        /// <summary>
        /// Called every frame by InputBroker to get movement direction.
        /// Return Vector2.zero for no movement.
        /// </summary>
        Vector2 GetMoveInput();

        /// <summary>
        /// Whether the character should be sprinting.
        /// </summary>
        bool IsSprinting();

        /// <summary>
        /// Whether a jump was requested this frame.
        /// </summary>
        bool JumpRequested();

        /// <summary>
        /// Whether a dive was requested this frame.
        /// </summary>
        bool DiveRequested();

        /// <summary>
        /// Whether a special action was requested this frame.
        /// Context-sensitive: lunge (when It) or evasion burst (when Runner).
        /// </summary>
        bool SpecialRequested();

        /// <summary>
        /// Whether a tag attempt was requested this frame (walk-up tag).
        /// </summary>
        bool TagRequested();

        /// <summary>
        /// Called when this source becomes active on a character.
        /// Use for initialization, caching references, etc.
        /// </summary>
        void OnActivate(GameObject character);

        /// <summary>
        /// Called when this source is deactivated (another source is taking over).
        /// Use for cleanup.
        /// </summary>
        void OnDeactivate();

        /// <summary>
        /// Called every frame while active. Use for AI decision-making,
        /// input buffering, etc. PlayerInput can use this for controller polling.
        /// </summary>
        void UpdateInput();
    }
}