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
    ///   Shove       — Triangle / G                 (engaged blockers and defenders; rolls a shove outcome)
    ///   Block play       — L1           / 1        (both blockers flank-and-block the carrier)
    ///   Release left     — D-pad Left   / 2        (both blockers release left; lead blocker = right-stick-aligned)
    ///   Release right    — D-pad Right  / 3        (both blockers release right; lead blocker = right-stick-aligned)
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
        public InputAction Shove { get; private set; }
        public InputAction BlockPlay { get; private set; }
        public InputAction ReleaseLeftPlay { get; private set; }
        public InputAction ReleaseRightPlay { get; private set; }
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

            // Shove — Triangle / G (engaged blockers and defenders only)
            Shove = actionMap.AddAction("Shove", InputActionType.Button);
            Shove.AddBinding("<Keyboard>/g");
            Shove.AddBinding("<Gamepad>/buttonNorth");

            // Plays — carrier-only. Right-stick direction at call time decides
            // which blocker becomes the lead vs. the primary receiver.
            //   BlockPlay         — L1 / 1     (both blockers stay flank-and-block)
            //   ReleaseLeftPlay   — D-pad Left / 2
            //   ReleaseRightPlay  — D-pad Right / 3
            BlockPlay = actionMap.AddAction("BlockPlay", InputActionType.Button);
            BlockPlay.AddBinding("<Keyboard>/1");
            BlockPlay.AddBinding("<Gamepad>/leftShoulder");

            ReleaseLeftPlay = actionMap.AddAction("ReleaseLeftPlay", InputActionType.Button);
            ReleaseLeftPlay.AddBinding("<Keyboard>/2");
            ReleaseLeftPlay.AddBinding("<Gamepad>/dpad/left");

            ReleaseRightPlay = actionMap.AddAction("ReleaseRightPlay", InputActionType.Button);
            ReleaseRightPlay.AddBinding("<Keyboard>/3");
            ReleaseRightPlay.AddBinding("<Gamepad>/dpad/right");

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
