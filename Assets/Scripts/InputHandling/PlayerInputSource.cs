using UnityEngine;
using Sportland.Rendering;

namespace Sportland.InputHandling
{
    /// <summary>
    /// Human player input source using Unity's new Input System.
    /// Supports DualShock 4 (PS4) controller and keyboard simultaneously.
    /// 
    /// Controls:
    ///   Left Stick / WASD       — Move
    ///   L3 click / Left Shift   — Sprint (L3 = toggle, Shift = hold)
    ///   L1 / Tab                — Shuffle / Defensive Stance (hold)
    ///   L2 / Z                  — Lean Left (while shuffling, analog)
    ///   R2 / C                  — Lean Right (while shuffling, analog)
    ///   R2 / F                  — Tag attempt (when NOT shuffling)
    ///   X / Space               — Jump
    ///   Circle / E              — Dive
    ///   R1 / Q                  — Special (Lunge if It / Evasion Burst if Runner)
    ///   Touchpad / R            — Reset
    /// </summary>
    public class PlayerInputSource : IInputSource
    {
        private TagInputActions controls;

        // Output state
        private Vector2 moveInput;
        private bool sprinting;
        private bool sprintToggleState; // tracks the L3 toggle
        private bool shuffling;
        private float leanInput;        // -1 to 1
        private bool jumpRequest;
        private bool diveRequest;
        private bool specialRequest;
        private bool tagRequest;

        public void OnActivate(GameObject character)
        {
            controls = new TagInputActions();
            controls.Enable();
            sprintToggleState = false;

            var statusIcons = character.GetComponent<StatusIconDisplay>();
            if (statusIcons != null)
                statusIcons.SetAIDecisionState(StatusIconDisplay.AIDecisionState.PlayerControlled);
        }

        public void OnDeactivate()
        {
            controls?.Disable();
            controls = null;

            moveInput = Vector2.zero;
            sprinting = false;
            sprintToggleState = false;
            shuffling = false;
            leanInput = 0f;
            jumpRequest = false;
            diveRequest = false;
            specialRequest = false;
            tagRequest = false;
        }

        public void UpdateInput()
        {
            if (controls == null) return;

            // ── Movement ──
            moveInput = controls.Move.ReadValue<Vector2>();
            if (moveInput.sqrMagnitude > 1f)
                moveInput.Normalize();

            // ── Sprint ──
            // L3 click toggles sprint on/off
            if (controls.SprintToggle.WasPressedThisFrame())
            {
                sprintToggleState = !sprintToggleState;
            }
            // Keyboard: Left Shift is hold-style (overrides toggle)
            bool keyboardSprint = controls.SprintHold.IsPressed();
            sprinting = keyboardSprint || sprintToggleState;

            // ── Shuffle ──
            shuffling = controls.Shuffle.IsPressed();

            // Shuffle cancels sprint
            if (shuffling)
            {
                sprinting = false;
                sprintToggleState = false; // reset toggle when entering shuffle
            }

            // ── Lean (only while shuffling) ──
            if (shuffling)
            {
                float leftTrigger = controls.LeanLeft.ReadValue<float>();
                float rightTrigger = controls.LeanRight.ReadValue<float>();

                // Combine: L2 = lean left (negative), R2 = lean right (positive)
                // If both pressed, they cancel out
                leanInput = rightTrigger - leftTrigger;
                leanInput = Mathf.Clamp(leanInput, -1f, 1f);
            }
            else
            {
                leanInput = 0f;
            }

            // ── Buffered single-frame actions ──
            if (controls.Jump.WasPressedThisFrame()) jumpRequest = true;
            if (controls.Dive.WasPressedThisFrame()) diveRequest = true;
            if (controls.Special.WasPressedThisFrame()) specialRequest = true;

            // ── Tag (context-sensitive R2) ──
            // Keyboard: F is always tag
            if (controls.Tag.WasPressedThisFrame()) tagRequest = true;

            // Gamepad: R2 is tag only when NOT shuffling
            // (when shuffling, R2 is lean right — handled above)
            if (!shuffling && controls.LeanRight.WasPressedThisFrame())
            {
                tagRequest = true;
            }
        }

        // ── IInputSource outputs ──

        public Vector2 GetMoveInput() => moveInput;
        public bool IsSprinting() => sprinting;
        public bool IsShuffling() => shuffling;
        public float GetLeanInput() => leanInput;

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