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
    ///   Jump        — Triangle / Space
    ///   Throw       — Circle / Q                       (only fires while holding the ball)
    ///   Pass        — Cross / F                        (tap = lob, hold = chest;
    ///                                                   target = teammate most aligned
    ///                                                   with last move direction)
    ///   ReturnBall  — L1 / 1                           (debug: snap ball to player)
    /// </summary>
    public class DodgeballInputActions
    {
        public InputAction Move       { get; private set; }
        public InputAction Sprint     { get; private set; }
        public InputAction Run        { get; private set; }
        public InputAction Jump       { get; private set; }
        public InputAction Throw      { get; private set; }
        public InputAction Pass       { get; private set; }
        public InputAction ReturnBall { get; private set; }

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

            // Double-tap any D-pad direction to engage run mode. Each binding
            // has its own multiTap state, so it's same-direction double-taps
            // rather than any two D-pad presses within the window.
            Run = actionMap.AddAction("Run", InputActionType.Button);
            Run.AddBinding("<Gamepad>/dpad/up").WithInteraction("multiTap");
            Run.AddBinding("<Gamepad>/dpad/down").WithInteraction("multiTap");
            Run.AddBinding("<Gamepad>/dpad/left").WithInteraction("multiTap");
            Run.AddBinding("<Gamepad>/dpad/right").WithInteraction("multiTap");

            Jump = actionMap.AddAction("Jump", InputActionType.Button);
            Jump.AddBinding("<Keyboard>/space");
            Jump.AddBinding("<Gamepad>/buttonNorth");

            Throw = actionMap.AddAction("Throw", InputActionType.Button);
            Throw.AddBinding("<Keyboard>/q");
            Throw.AddBinding("<Gamepad>/buttonEast");

            Pass = actionMap.AddAction("Pass", InputActionType.Button);
            Pass.AddBinding("<Keyboard>/f");
            Pass.AddBinding("<Gamepad>/buttonSouth");

            ReturnBall = actionMap.AddAction("ReturnBall", InputActionType.Button);
            ReturnBall.AddBinding("<Keyboard>/1");
            ReturnBall.AddBinding("<Gamepad>/leftShoulder");
        }

        public void Enable()  => actionMap.Enable();
        public void Disable() => actionMap.Disable();
    }
}
