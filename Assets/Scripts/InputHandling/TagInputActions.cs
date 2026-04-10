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
    ///   Sprint      — L3 click (toggle) / Left Shift (hold)
    ///   Shuffle     — L1 (hold) / Tab (hold)
    ///   Lean Left   — L2 (analog, while shuffling) / Z
    ///   Lean Right  — R2 (analog, while shuffling) / C
    ///   Jump        — X button / Space
    ///   Dive        — Circle button / E
    ///   Special     — R1 / Q (Lunge when It, Evasion Burst when Runner)
    ///   Tag         — R2 / F (walk-up tag when not shuffling)
    ///   Reset       — Touchpad / R
    /// 
    /// Note: R2 is dual-purpose. When shuffling, R2 = Lean Right.
    ///       When not shuffling, R2 = Tag attempt.
    ///       The PlayerInputSource handles the context switching.
    /// </summary>
    public class TagInputActions
    {
        // ──────────────────────────────────────────────
        //  ACTIONS
        // ──────────────────────────────────────────────

        public InputAction Move { get; private set; }
        public InputAction SprintToggle { get; private set; }
        public InputAction SprintHold { get; private set; }
        public InputAction Shuffle { get; private set; }
        public InputAction LeanLeft { get; private set; }
        public InputAction LeanRight { get; private set; }
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

            // Sprint Toggle — L3 (left stick click)
            SprintToggle = actionMap.AddAction("SprintToggle", InputActionType.Button);
            SprintToggle.AddBinding("<Gamepad>/leftStickPress");

            // Sprint Hold — Left Shift (keyboard fallback, hold-style)
            SprintHold = actionMap.AddAction("SprintHold", InputActionType.Button);
            SprintHold.AddBinding("<Keyboard>/leftShift");

            // Shuffle / Defensive Stance — L1 / Tab (hold)
            Shuffle = actionMap.AddAction("Shuffle", InputActionType.Button);
            Shuffle.AddBinding("<Keyboard>/tab");
            Shuffle.AddBinding("<Gamepad>/leftShoulder");

            // Lean Left — L2 (analog) / Z
            LeanLeft = actionMap.AddAction("LeanLeft", InputActionType.Value);
            LeanLeft.AddBinding("<Keyboard>/z");
            LeanLeft.AddBinding("<Gamepad>/leftTrigger");

            // Lean Right — R2 (analog) / C
            LeanRight = actionMap.AddAction("LeanRight", InputActionType.Value);
            LeanRight.AddBinding("<Keyboard>/c");
            LeanRight.AddBinding("<Gamepad>/rightTrigger");

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

            // Tag — F (keyboard only — R2 on gamepad is context-sensitive)
            Tag = actionMap.AddAction("Tag", InputActionType.Button);
            Tag.AddBinding("<Keyboard>/f");

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