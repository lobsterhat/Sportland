using UnityEngine;
using UnityEngine.InputSystem;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Routes PS4 / keyboard input into a single human-controlled Dodgeball
    /// player. Move uses analog magnitude. L2 toggles sprint mode. Cross
    /// jumps. Circle throws the held ball (no-op when empty-handed).
    /// </summary>
    [RequireComponent(typeof(PlayerMovement))]
    [RequireComponent(typeof(PlayerZoneTracker))]
    public class DodgeballPlayerInput : MonoBehaviour
    {
        [SerializeField] private float throwPower = 10f;

        private PlayerMovement movement;
        private PlayerZoneTracker tracker;
        private DodgeballInputActions actions;

        private bool sprintMode;
        private Vector2 lastMoveDirection = Vector2.right;

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            tracker = GetComponent<PlayerZoneTracker>();
            actions = new DodgeballInputActions();
            actions.Sprint.performed += OnSprintPressed;
            actions.Jump.performed   += OnJumpPressed;
            actions.Throw.performed  += OnThrowPressed;
        }

        private void OnEnable()  => actions?.Enable();
        private void OnDisable() => actions?.Disable();

        private void OnDestroy()
        {
            if (actions == null) return;
            actions.Sprint.performed -= OnSprintPressed;
            actions.Jump.performed   -= OnJumpPressed;
            actions.Throw.performed  -= OnThrowPressed;
            actions.Disable();
        }

        private void Update()
        {
            Vector2 input = actions.Move.ReadValue<Vector2>();
            if (input.sqrMagnitude > 0.04f) lastMoveDirection = input.normalized;

            movement.IsSprinting = sprintMode;
            movement.ApplyMove(input);
        }

        private void OnSprintPressed(InputAction.CallbackContext _) => sprintMode = !sprintMode;
        private void OnJumpPressed(InputAction.CallbackContext _)   => movement.TryJump();

        private void OnThrowPressed(InputAction.CallbackContext _)
        {
            var ball = tracker.HeldBall;
            if (ball == null) return;
            ball.Throw(lastMoveDirection, throwPower);
        }
    }
}
