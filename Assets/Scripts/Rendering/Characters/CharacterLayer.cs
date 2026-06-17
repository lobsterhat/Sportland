using UnityEngine;

namespace Sportland.Rendering.Characters
{
    /// <summary>
    /// Manages one sprite layer in the modular character rig.
    ///
    /// Responsibilities:
    ///   • Holds a SpriteRenderer for its category (Head, Hair, etc.)
    ///   • Tracks which CharacterPartVariant is currently active
    ///   • Advances frame counters based on state elapsed time
    ///   • Applies color tinting from CharacterAppearance
    ///
    /// Driven by ModularCharacterRig — do not add this component manually.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class CharacterLayer : MonoBehaviour
    {
        public CharacterPartCategory Category { get; private set; }

        private SpriteRenderer sr;
        private CharacterPartVariant variant;
        private PartAnimationClip activeClip;
        private int currentFrame;
        private float frameTimer;

        // ── Initialization ────────────────────────────────────────

        public void Initialize(CharacterPartCategory category, int baseSortingOrder)
        {
            Category = category;
            sr = GetComponent<SpriteRenderer>();
            sr.sortingOrder = baseSortingOrder;
            sr.sprite = null;
        }

        // ── Part Swapping ─────────────────────────────────────────

        /// <summary>
        /// Swap to a new part variant. Pass null to hide this layer.
        /// Refreshes immediately without resetting animation timing.
        /// </summary>
        public void SetVariant(CharacterPartVariant newVariant, string currentStateName,
                               Color tintColor)
        {
            variant = newVariant;

            if (variant == null)
            {
                sr.sprite = null;
                sr.enabled = false;
                return;
            }

            sr.enabled = true;
            sr.sortingOrder += variant.sortingOrderBump;
            transform.localPosition = new Vector3(variant.pivotOffset.x,
                                                  variant.pivotOffset.y, 0f);

            SetState(currentStateName ?? AnimationStates.Idle);
            ApplyTint(tintColor);
        }

        // ── Animation State ───────────────────────────────────────

        public void SetState(string stateName)
        {
            activeClip = variant?.GetClip(stateName);
            currentFrame = 0;
            frameTimer = 0f;
            UpdateSprite();
        }

        public void Tick(float deltaTime)
        {
            if (activeClip == null || activeClip.frames == null ||
                activeClip.frames.Length <= 1) return;

            frameTimer += deltaTime;

            if (frameTimer >= activeClip.FrameDuration)
            {
                frameTimer -= activeClip.FrameDuration;

                if (activeClip.loop)
                {
                    currentFrame = (currentFrame + 1) % activeClip.frames.Length;
                }
                else
                {
                    currentFrame = Mathf.Min(currentFrame + 1, activeClip.frames.Length - 1);
                }

                UpdateSprite();
            }
        }

        // ── Tinting ───────────────────────────────────────────────

        public void ApplyTint(Color color)
        {
            if (sr == null) return;
            sr.color = (variant != null && variant.receivesTint) ? color : Color.white;
        }

        // ── Internal ──────────────────────────────────────────────

        private void UpdateSprite()
        {
            if (sr == null) return;
            if (activeClip == null || activeClip.frames == null || activeClip.frames.Length == 0)
            {
                sr.sprite = null;
                return;
            }
            sr.sprite = activeClip.frames[Mathf.Clamp(currentFrame, 0, activeClip.frames.Length - 1)];
        }
    }
}
