using UnityEngine.InputSystem;

namespace Sportland.Sports.Demoball
{
    /// <summary>
    /// Input Actions for Demoball. Supports DualShock 4 (PS4) controller and keyboard.
    ///
    /// Control Scheme:
    ///   Move        — Left Stick / WASD / Arrow Keys
    ///   Aim         — Right Stick                  (selects pass target while carrying)
    ///   Sprint      — L2 / Left Shift              (hold)
    ///   Action      — Circle / E                   (pickup loose ball, or pass if carrying)
    ///   TouchDown   — R1     / Q                   (score while in scoring ring and carrying)
    ///   Tackle      — Square / T                   (Defenders only)
    /// </summary>
    public class DemoballInputActions
    {
        public InputAction Move { get; private set; }
        public InputAction Aim { get; private set; }
        public InputAction Sprint { get; private set; }
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

            // Aim — Right Stick (used to choose a pass target while carrying)
            Aim = actionMap.AddAction("Aim", InputActionType.Value);
            Aim.AddBinding("<Gamepad>/rightStick");

            // Sprint — L2 / Left Shift (hold)
            Sprint = actionMap.AddAction("Sprint", InputActionType.Button);
            Sprint.AddBinding("<Keyboard>/leftShift");
            Sprint.AddBinding("<Gamepad>/leftTrigger");

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
