using UnityEngine;

namespace Sportland.InputHandling
{
    /// <summary>
    /// Human player input source using Unity's new Input System.
    /// Supports DualShock 4 (PS4) controller and keyboard simultaneously.
    /// 
    /// Controls:
    ///   Left Stick / WASD    — Move (analog stick gives variable speed)
    ///   L2 / Left Shift      — Sprint (hold)
    ///   X / Space            — Jump
    ///   Circle / E           — Dive
    ///   R1 / Q               — Special (Lunge if It / Evasion Burst if Runner)
    ///   R2 / F               — Tag attempt (walk-up)
    /// </summary>
    public class PlayerInputSource : IInputSource
    {
        private TagInputActions controls;

        private Vector2 moveInput;
        private bool sprinting;
        private bool jumpRequest;
        private bool diveRequest;
        private bool specialRequest;
        private bool tagRequest;

        public void OnActivate(GameObject character)
        {
            controls = new TagInputActions();
            controls.Enable();
        }

        public void OnDeactivate()
        {
            controls?.Disable();
            controls = null;

            moveInput = Vector2.zero;
            sprinting = false;
            jumpRequest = false;
            diveRequest = false;
            specialRequest = false;
            tagRequest = false;
        }

        public void UpdateInput()
        {
            if (controls == null) return;

            // Movement — analog stick gives 0-1 range automatically
            moveInput = controls.Move.ReadValue<Vector2>();
            if (moveInput.sqrMagnitude > 1f)
                moveInput.Normalize();

            // Sprint
            sprinting = controls.Sprint.IsPressed();

            // Buffer single-frame actions — once set to true, they stay true
            // until consumed by the getter. This prevents missed inputs
            // when Update timing doesn't perfectly align.
            if (controls.Jump.WasPressedThisFrame()) jumpRequest = true;
            if (controls.Dive.WasPressedThisFrame()) diveRequest = true;
            if (controls.Special.WasPressedThisFrame()) specialRequest = true;
            if (controls.Tag.WasPressedThisFrame()) tagRequest = true;
        }

        public Vector2 GetMoveInput() => moveInput;
        public bool IsSprinting() => sprinting;

        public bool JumpRequested()
        {
            bool val = jumpRequest;
            jumpRequest = false;
            return val;
        }

        public bool DiveRequested()
        {
            bool val = diveRequest;
            diveRequest = false;
            return val;
        }

        public bool SpecialRequested()
        {
            bool val = specialRequest;
            specialRequest = false;
            return val;
        }

        public bool TagRequested()
        {
            bool val = tagRequest;
            tagRequest = false;
            return val;
        }
    }
}