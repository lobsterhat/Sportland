using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Routes left-stick / WASD / arrow input into PlayerMovement.ApplyMove
    /// for a single human-controlled Dodgeball player.
    ///
    /// Attached only to the player the user is driving; AI / inert players
    /// don't need this component.
    /// </summary>
    [RequireComponent(typeof(PlayerMovement))]
    public class DodgeballPlayerInput : MonoBehaviour
    {
        private PlayerMovement movement;
        private DodgeballInputActions actions;

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            actions = new DodgeballInputActions();
        }

        private void OnEnable()  => actions?.Enable();
        private void OnDisable() => actions?.Disable();

        private void Update()
        {
            var input = actions.Move.ReadValue<Vector2>();
            movement.ApplyMove(input);
        }
    }
}
