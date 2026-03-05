using UnityEngine;
using UnityEngine.InputSystem;

namespace Sportland.InputHandling
{
    /// <summary>
    /// Creates and manages Input Actions for the Tag game (and eventually all sports).
    /// Supports PS4 controller (DualShock 4) and keyboard fallback.
    /// 
    /// Control Scheme:
    ///   Move        — Left Stick / WASD
    ///   Sprint      — L2 (hold) / Left Shift
    ///   Jump        — X button / Space
    ///   Dive        — Circle button / E
    ///   Special     — R1 / Q (Lunge when It, Evasion Burst when Runner)
    ///   Tag         — R2 / F (walk-up tag when It)
    /// 
    /// Usage:
    ///   var controls = new TagInputActions();
    ///   controls.Enable();
    ///   Vector2 move = controls.Move.ReadValue&lt;Vector2&gt;();
    ///   bool jumped = controls.Jump.WasPressedThisFrame();
    /// </summary>
    public class TagInputActions
    {
        // ──────────────────────────────────────────────
        //  ACTIONS
        // ──────────────────────────────────────────────

        public InputAction Move { get; private set; }
        public InputAction Sprint { get; private set; }
        public InputAction Shuffle { get; private set; }
        public InputAction Jump { get; private set; }
        public InputAction Dive { get; private set; }
        public InputAction Special { get; private set; }
        public InputAction Tag { get; private set; }
        public InputAction Reset { get; private set; }

        private InputActionMap actionMap;

        // ──────────────────────────────────────────────
        //  CONSTRUCTOR
        // ──────────────────────────────────────────────

        public TagInputActions()
        {
            actionMap = new InputActionMap("TagControls");

            // Move — composite WASD + left stick
            Move = actionMap.AddAction("Move", InputActionType.Value);
            Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");
            Move.AddBinding("<Gamepad>/leftStick");

            // Sprint — L2 / Left Shift
            Sprint = actionMap.AddAction("Sprint", InputActionType.Button);
            Sprint.AddBinding("<Keyboard>/leftShift");
            Sprint.AddBinding("<Gamepad>/leftTrigger");

            // Shuffle / Defensive Stance — L1 / Tab (hold)
            Shuffle = actionMap.AddAction("Shuffle", InputActionType.Button);
            Shuffle.AddBinding("<Keyboard>/tab");
            Shuffle.AddBinding("<Gamepad>/leftShoulder");

            // Jump — X button (buttonSouth) / Space
            Jump = actionMap.AddAction("Jump", InputActionType.Button);
            Jump.AddBinding("<Keyboard>/space");
            Jump.AddBinding("<Gamepad>/buttonSouth");

            // Dive — Circle button (buttonEast) / E
            Dive = actionMap.AddAction("Dive", InputActionType.Button);
            Dive.AddBinding("<Keyboard>/e");
            Dive.AddBinding("<Gamepad>/buttonEast");

            // Special — R1 / Q
            Special = actionMap.AddAction("Special", InputActionType.Button);
            Special.AddBinding("<Keyboard>/q");
            Special.AddBinding("<Gamepad>/rightShoulder");

            // Tag — R2 / F
            Tag = actionMap.AddAction("Tag", InputActionType.Button);
            Tag.AddBinding("<Keyboard>/f");
            Tag.AddBinding("<Gamepad>/rightTrigger");

            // Reset — Touchpad / R
            Reset = actionMap.AddAction("Reset", InputActionType.Button);
            Reset.AddBinding("<Keyboard>/r");
            Reset.AddBinding("<DualShockGamepad>/touchpadButton");
        }

        // ──────────────────────────────────────────────
        //  ENABLE / DISABLE
        // ──────────────────────────────────────────────

        public void Enable()
        {
            actionMap.Enable();
        }

        public void Disable()
        {
            actionMap.Disable();
        }
    }
}