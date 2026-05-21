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
    ///   Jump        — Cross / Space
    ///   Throw       — Square / Q                       (only fires while holding the ball)
    ///   Pass        — Triangle / F                     (tap = lob, hold = chest;
    ///                                                   target = teammate most aligned
    ///                                                   with last move direction)
    ///   Catch       — Circle / E                       (active grab of a nearby ball)
    ///   ReturnBall  — L1 / 1                           (debug: snap ball to player)
    /// </summary>
    public class DodgeballInputActions
    {
        public InputAction Move       { get; private set; }
        public InputAction Sprint     { get; private set; }
        public InputAction Jump       { get; private set; }
        public InputAction Throw      { get; private set; }
        public InputAction Pass       { get; private set; }
        public InputAction Catch      { get; private set; }
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

            Jump = actionMap.AddAction("Jump", InputActionType.Button);
            Jump.AddBinding("<Keyboard>/space");
            Jump.AddBinding("<Gamepad>/buttonSouth");

            Throw = actionMap.AddAction("Throw", InputActionType.Button);
            Throw.AddBinding("<Keyboard>/q");
            Throw.AddBinding("<Gamepad>/buttonWest");   // Square

            Pass = actionMap.AddAction("Pass", InputActionType.Button);
            Pass.AddBinding("<Keyboard>/f");
            Pass.AddBinding("<Gamepad>/buttonNorth");   // Triangle

            Catch = actionMap.AddAction("Catch", InputActionType.Button);
            Catch.AddBinding("<Keyboard>/e");
            Catch.AddBinding("<Gamepad>/buttonEast");   // Circle

            ReturnBall = actionMap.AddAction("ReturnBall", InputActionType.Button);
            ReturnBall.AddBinding("<Keyboard>/1");
            ReturnBall.AddBinding("<Gamepad>/leftShoulder");
        }

        public void Enable()  => actionMap.Enable();
        public void Disable() => actionMap.Disable();
    }
}
