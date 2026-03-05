using UnityEngine;
using Sportland.Movement;

namespace Sportland.Rendering
{
    /// <summary>
    /// Shifts the character's sprite child based on the lean offset
    /// from BaseMovementController. Creates the visual feint effect
    /// where the character's body shifts while their feet stay planted.
    /// 
    /// Works alongside SpriteVerticalOffset — lean is horizontal,
    /// vertical offset handles jumps. Both modify the sprite child's
    /// local position independently.
    /// 
    /// Setup:
    ///   Drop on the Player root GameObject (same as SpriteVerticalOffset).
    ///   It auto-finds the Sprite child and movement controller.
    /// </summary>
    public class SpriteLeanOffset : MonoBehaviour
    {
        [Header("=== REFERENCES ===")]
        [Tooltip("The sprite child to offset. Auto-found if left empty.")]
        [SerializeField] private Transform spriteChild;

        [Header("=== VISUAL TUNING ===")]
        [Tooltip("Additional visual multiplier on lean distance. " +
                 "Use to exaggerate or subtle-ize the visual lean.")]
        [SerializeField] private float visualMultiplier = 1f;

        [Tooltip("Smoothing on the sprite shift (lower = snappier, higher = floatier).")]
        [Range(0f, 0.1f)]
        [SerializeField] private float smoothTime = 0.03f;

        // ──────────────────────────────────────────────
        //  RUNTIME
        // ──────────────────────────────────────────────

        private BaseMovementController movement;
        private Vector2 currentOffset;
        private Vector2 offsetVelocity;
        private float baseSpriteX; // original local X position of sprite child

        // ──────────────────────────────────────────────
        //  INITIALIZATION
        // ──────────────────────────────────────────────

        private void Awake()
        {
            movement = GetComponent<BaseMovementController>();

            if (spriteChild == null)
            {
                // Auto-find — look for a child named "Sprite"
                var spriteTransform = transform.Find("Sprite");
                if (spriteTransform != null)
                {
                    spriteChild = spriteTransform;
                }
            }

            if (spriteChild != null)
            {
                baseSpriteX = spriteChild.localPosition.x;
            }
        }

        // ──────────────────────────────────────────────
        //  UPDATE
        // ──────────────────────────────────────────────

        private void LateUpdate()
        {
            if (movement == null || spriteChild == null) return;

            // Get lean offset from the movement controller
            Vector2 targetOffset = movement.GetLeanOffset() * visualMultiplier;

            // Smooth the transition
            currentOffset.x = Mathf.SmoothDamp(currentOffset.x, targetOffset.x,
                ref offsetVelocity.x, smoothTime);
            currentOffset.y = Mathf.SmoothDamp(currentOffset.y, targetOffset.y,
                ref offsetVelocity.y, smoothTime);

            // Apply to sprite — only affects X (horizontal lean)
            // Y is managed by SpriteVerticalOffset for jumps
            Vector3 pos = spriteChild.localPosition;
            pos.x = baseSpriteX + currentOffset.x;
            spriteChild.localPosition = pos;
        }
    }
}