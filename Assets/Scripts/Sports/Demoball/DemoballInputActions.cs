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
    ///   Action      — R1     / E                   (pickup loose ball, or pass if carrying;
    ///                                               R1 keeps the right thumb free for the right stick)
    ///   TouchDown   — Circle / Q                   (score while in scoring ring and carrying)
    ///   Tackle      — Square / T                   (Defenders only)
    ///   DebugReset  — D-pad Up    / R              (debug: reload the scene)
    ///   DebugBall   — D-pad Down  / B              (debug: fire another ball from the cannon)
    /// </summary>
    public class DemoballInputActions
    {
        public InputAction Move { get; private set; }
        public InputAction Aim { get; private set; }
        public InputAction Sprint { get; private set; }
        public InputAction Action { get; private set; }
        public InputAction TouchDown { get; private set; }
        public InputAction Tackle { get; private set; }
        public InputAction CallPlay { get; private set; }
        public InputAction DebugReset { get; private set; }
        public InputAction DebugSpawnBall { get; private set; }

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

            // Action — R1 / E (pickup or pass; R1 leaves the right thumb on the aim stick)
            Action = actionMap.AddAction("Action", InputActionType.Button);
            Action.AddBinding("<Keyboard>/e");
            Action.AddBinding("<Gamepad>/rightShoulder");

            // TouchDown — Circle / Q
            TouchDown = actionMap.AddAction("TouchDown", InputActionType.Button);
            TouchDown.AddBinding("<Keyboard>/q");
            TouchDown.AddBinding("<Gamepad>/buttonEast");

            // Tackle — Square / T
            Tackle = actionMap.AddAction("Tackle", InputActionType.Button);
            Tackle.AddBinding("<Keyboard>/t");
            Tackle.AddBinding("<Gamepad>/buttonWest");

            // Call Play — L1 / Tab (carrier only): peels blockers off into receiver routes
            CallPlay = actionMap.AddAction("CallPlay", InputActionType.Button);
            CallPlay.AddBinding("<Keyboard>/tab");
            CallPlay.AddBinding("<Gamepad>/leftShoulder");

            // Debug: reload the scene — D-pad Up / R
            DebugReset = actionMap.AddAction("DebugReset", InputActionType.Button);
            DebugReset.AddBinding("<Keyboard>/r");
            DebugReset.AddBinding("<Gamepad>/dpad/up");

            // Debug: fire another ball — D-pad Down / B
            DebugSpawnBall = actionMap.AddAction("DebugSpawnBall", InputActionType.Button);
            DebugSpawnBall.AddBinding("<Keyboard>/b");
            DebugSpawnBall.AddBinding("<Gamepad>/dpad/down");
        }

        public void Enable()  => actionMap.Enable();
        public void Disable() => actionMap.Disable();
    }
}
