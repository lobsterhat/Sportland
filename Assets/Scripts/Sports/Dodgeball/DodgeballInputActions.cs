using UnityEngine.InputSystem;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Input Actions for Dodgeball. DualShock 4 (PS4) + keyboard.
    ///
    /// Control scheme:
    ///   Move        — Left Stick / D-pad / WASD / Arrow Keys
    ///                 (analog magnitude respected on the stick; D-pad is digital)
    ///   Sprint      — L2 / Left Shift                  (hold)
    ///   Run         — D-pad double-tap (any direction) (sticky while D-pad held)
    ///   Jump        — Cross / Space                    (always a hop; with the ball it's an
    ///                                                   attack jump so you can throw across a
    ///                                                   line before landing)
    ///   Throw       — Square / Q                       (offense only)
    ///   Pass        — Circle / E                       (offense: tap = lob, hold = chest)
    ///   Catch       — Circle / E                       (defense: arm a catch)
    ///   Switch      — Triangle / F                     (defense: take the nearest teammate)
    ///   Stance      — R2 / Left-Ctrl                   (toggle defensive stance)
    ///   ReturnBall  — L1 / 1                           (debug: snap ball to player)
    /// </summary>
    public class DodgeballInputActions
    {
        public InputAction Move       { get; private set; }
        public InputAction Sprint     { get; private set; }
        public InputAction Evade      { get; private set; }
        public InputAction Throw      { get; private set; }
        public InputAction Circle     { get; private set; }  // pass on offense, catch on defense
        public InputAction Switch     { get; private set; }  // defense: take the nearest teammate
        public InputAction Stance     { get; private set; }
        public InputAction ReturnBall { get; private set; }

        // Per-direction D-pad press actions. The input layer watches their
        // .started callbacks to detect a same-direction double-tap and engage
        // run mode without requiring the second tap to release (which is what
        // a built-in multiTap interaction would demand).
        public InputAction DpadUp     { get; private set; }
        public InputAction DpadDown   { get; private set; }
        public InputAction DpadLeft   { get; private set; }
        public InputAction DpadRight  { get; private set; }

        private readonly InputActionMap actionMap;

        public DodgeballInputActions()
        {
            actionMap = new InputActionMap("DodgeballControls");

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
            Move.AddBinding("<Gamepad>/dpad");

            Sprint = actionMap.AddAction("Sprint", InputActionType.Button);
            Sprint.AddBinding("<Keyboard>/leftShift");
            Sprint.AddBinding("<Gamepad>/leftTrigger");

            // Per-direction D-pad press actions for manual double-tap
            // detection in the input layer.
            DpadUp    = actionMap.AddAction("DpadUp",    InputActionType.Button);
            DpadDown  = actionMap.AddAction("DpadDown",  InputActionType.Button);
            DpadLeft  = actionMap.AddAction("DpadLeft",  InputActionType.Button);
            DpadRight = actionMap.AddAction("DpadRight", InputActionType.Button);
            DpadUp.AddBinding("<Gamepad>/dpad/up");
            DpadDown.AddBinding("<Gamepad>/dpad/down");
            DpadLeft.AddBinding("<Gamepad>/dpad/left");
            DpadRight.AddBinding("<Gamepad>/dpad/right");

            Evade = actionMap.AddAction("Evade", InputActionType.Button);
            Evade.AddBinding("<Keyboard>/space");
            Evade.AddBinding("<Gamepad>/buttonSouth");

            Throw = actionMap.AddAction("Throw", InputActionType.Button);
            Throw.AddBinding("<Keyboard>/q");
            Throw.AddBinding("<Gamepad>/buttonWest");   // Square

            Circle = actionMap.AddAction("Circle", InputActionType.Button);
            Circle.AddBinding("<Keyboard>/e");
            Circle.AddBinding("<Gamepad>/buttonEast");   // Circle / O

            Switch = actionMap.AddAction("Switch", InputActionType.Button);
            Switch.AddBinding("<Keyboard>/f");
            Switch.AddBinding("<Gamepad>/buttonNorth");   // Triangle

            Stance = actionMap.AddAction("Stance", InputActionType.Button);
            Stance.AddBinding("<Keyboard>/leftCtrl");
            Stance.AddBinding("<Gamepad>/rightTrigger");   // R2

            ReturnBall = actionMap.AddAction("ReturnBall", InputActionType.Button);
            ReturnBall.AddBinding("<Keyboard>/1");
            ReturnBall.AddBinding("<Gamepad>/leftShoulder");
        }

        public void Enable()  => actionMap.Enable();
        public void Disable() => actionMap.Disable();
    }
}
