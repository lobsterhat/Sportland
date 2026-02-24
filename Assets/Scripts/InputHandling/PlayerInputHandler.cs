using Sportland.Movement;
using Sportland.Sports.Tag;
using UnityEngine;

namespace Sportland.InputHandling
{
    /// <summary>
    /// Reads player input and feeds it to a TagMovementController.
    /// 
    /// Controls:
    ///   WASD / Arrow Keys — Move
    ///   Left Shift       — Sprint (hold)
    ///   Space            — Jump
    ///   E                — Dive
    ///   Q                — Lunge (when It) / Evasion Burst (when Runner)
    ///   F                — Tag attempt (when It, non-lunge)
    ///   
    /// Drop this on the same GameObject as your TagMovementController.
    /// For testing with BaseMovementController only, it will gracefully
    /// skip Tag-specific calls.
    /// </summary>
    [RequireComponent(typeof(TagMovementController))]
    public class PlayerInputHandler : MonoBehaviour
    {
        private TagMovementController movement;

        private void Awake()
        {
            movement = GetComponent<TagMovementController>();
        }

        private void Update()
        {
            if (movement.IsEliminated) return;

            HandleMovementInput();
            HandleActionInput();
        }

        private void HandleMovementInput()
        {
            // Read raw axis input
            float horizontal = 0f;
            float vertical = 0f;

            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) vertical += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) vertical -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) horizontal += 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) horizontal -= 1f;

            movement.SetMoveInput(new Vector2(horizontal, vertical));

            // Sprint
            movement.SetSprinting(Input.GetKey(KeyCode.LeftShift));
        }

        private void HandleActionInput()
        {
            // Jump
            if (Input.GetKeyDown(KeyCode.Space))
            {
                movement.TryJump();
            }

            // Dive
            if (Input.GetKeyDown(KeyCode.E))
            {
                movement.TryDive();
            }

            // Q — context-sensitive: Lunge if It, Evasion Burst if Runner
            if (Input.GetKeyDown(KeyCode.Q))
            {
                if (movement.CurrentRole == TagMovementController.TagRole.It)
                {
                    movement.TryLunge();
                }
                else
                {
                    movement.TryEvasionBurst();
                }
            }

            // F — manual tag attempt (walk-up tag without lunging)
            if (Input.GetKeyDown(KeyCode.F))
            {
                movement.TryTag();
            }
        }
    }
}