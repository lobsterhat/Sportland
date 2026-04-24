using UnityEngine.InputSystem;

namespace Sportland.Sports.Demoball
{
    /// <summary>
    /// Input Actions for Demoball. Supports DualShock 4 (PS4) controller and keyboard.
    ///
    /// Control Scheme:
    ///   Move        — Left Stick / WASD / Arrow Keys
    ///   Sprint      — L3 click (toggle) / Left Shift (hold)
    ///   Action      — Circle / E        (pickup loose ball, or pass if carrying)
    ///   TouchDown   — R1     / Q        (score while in scoring ring and carrying)
    ///   Tackle      — Square / T        (Defenders only)
    /// </summary>
    public class DemoballInputActions
    {
        public InputAction Move { get; private set; }
        public InputAction SprintToggle { get; private set; }
        public InputAction SprintHold { get; private set; }
        public InputAction Action { get; private set; }
        public InputAction TouchDown { get; private set; }
        public InputAction Tackle { get; private set; }

        private readonly InputActionMap actionMap;

        public DemoballInputActions()
        {
            actionMap = new InputActionMap("DemoballControls");

            // Move — WASD + Arrows + Left Stick
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

            // Sprint — L3 toggle / Left Shift hold
            SprintToggle = actionMap.AddAction("SprintToggle", InputActionType.Button);
            SprintToggle.AddBinding("<Gamepad>/leftStickPress");

            SprintHold = actionMap.AddAction("SprintHold", InputActionType.Button);
            SprintHold.AddBinding("<Keyboard>/leftShift");

            // Action — Circle / E (pickup or pass)
            Action = actionMap.AddAction("Action", InputActionType.Button);
            Action.AddBinding("<Keyboard>/e");
            Action.AddBinding("<Gamepad>/buttonEast");

            // TouchDown — R1 / Q
            TouchDown = actionMap.AddAction("TouchDown", InputActionType.Button);
            TouchDown.AddBinding("<Keyboard>/q");
            TouchDown.AddBinding("<Gamepad>/rightShoulder");

            // Tackle — Square / T
            Tackle = actionMap.AddAction("Tackle", InputActionType.Button);
            Tackle.AddBinding("<Keyboard>/t");
            Tackle.AddBinding("<Gamepad>/buttonWest");
        }

        public void Enable()  => actionMap.Enable();
        public void Disable() => actionMap.Disable();
    }
}
