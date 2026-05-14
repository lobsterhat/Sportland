using UnityEngine.InputSystem;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Input Actions for Dodgeball. DualShock 4 (PS4) + keyboard.
    ///
    /// Control scheme:
    ///   Move    — Left Stick / WASD / Arrow Keys   (analog magnitude respected)
    ///   Sprint  — L2 / Left Shift                  (hold)
    ///   Jump    — Cross / Space
    ///   Throw   — Circle / Q                       (only fires while holding the ball)
    /// </summary>
    public class DodgeballInputActions
    {
        public InputAction Move   { get; private set; }
        public InputAction Sprint { get; private set; }
        public InputAction Jump   { get; private set; }
        public InputAction Throw  { get; private set; }

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

            Sprint = actionMap.AddAction("Sprint", InputActionType.Button);
            Sprint.AddBinding("<Keyboard>/leftShift");
            Sprint.AddBinding("<Gamepad>/leftTrigger");

            Jump = actionMap.AddAction("Jump", InputActionType.Button);
            Jump.AddBinding("<Keyboard>/space");
            Jump.AddBinding("<Gamepad>/buttonSouth");

            Throw = actionMap.AddAction("Throw", InputActionType.Button);
            Throw.AddBinding("<Keyboard>/q");
            Throw.AddBinding("<Gamepad>/buttonEast");
        }

        public void Enable()  => actionMap.Enable();
        public void Disable() => actionMap.Disable();
    }
}
