using UnityEngine;

namespace Sportland.InputHandling
{
    /// <summary>
    /// Human player input source. Reads keyboard/controller input and
    /// exposes it through IInputSource for the InputBroker.
    /// 
    /// Controls:
    ///   WASD / Arrow Keys — Move
    ///   Left Shift       — Sprint (hold)
    ///   Space            — Jump
    ///   E                — Dive
    ///   Q                — Special (Lunge if It / Evasion Burst if Runner)
    ///   F                — Tag attempt (walk-up)
    /// </summary>
    public class PlayerInputSource : IInputSource
    {
        private Vector2 moveInput;
        private bool sprinting;
        private bool jumpRequest;
        private bool diveRequest;
        private bool specialRequest;
        private bool tagRequest;

        public void OnActivate(GameObject character)
        {
            // Could cache references, show "you're controlling this player" UI, etc.
        }

        public void OnDeactivate()
        {
            // Clear all input so the character doesn't keep moving
            moveInput = Vector2.zero;
            sprinting = false;
            jumpRequest = false;
            diveRequest = false;
            specialRequest = false;
            tagRequest = false;
        }

        public void UpdateInput()
        {
            // Movement
            float horizontal = 0f;
            float vertical = 0f;

            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) vertical += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) vertical -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) horizontal += 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) horizontal -= 1f;

            moveInput = new Vector2(horizontal, vertical);
            if (moveInput.sqrMagnitude > 1f)
                moveInput.Normalize();

            // Sprint
            sprinting = Input.GetKey(KeyCode.LeftShift);

            // Actions (GetKeyDown — single frame)
            jumpRequest = Input.GetKeyDown(KeyCode.Space);
            diveRequest = Input.GetKeyDown(KeyCode.E);
            specialRequest = Input.GetKeyDown(KeyCode.Q);
            tagRequest = Input.GetKeyDown(KeyCode.F);
        }

        public Vector2 GetMoveInput() => moveInput;
        public bool IsSprinting() => sprinting;
        public bool JumpRequested() => jumpRequest;
        public bool DiveRequested() => diveRequest;
        public bool SpecialRequested() => specialRequest;
        public bool TagRequested() => tagRequest;
    }
}