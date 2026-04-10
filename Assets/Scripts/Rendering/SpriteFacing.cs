using UnityEngine;
using Sportland.Movement;

namespace Sportland.Rendering
{
    /// <summary>
    /// Flips or swaps the character sprite to face the movement direction.
    /// 
    /// Current: Left/right flip using SpriteRenderer.flipX.
    /// Future: Multi-directional sprite swapping (up, down, diagonals)
    ///         by assigning sprites per direction.
    /// 
    /// Reads facing direction from BaseMovementController.
    /// During shuffle, uses the locked facing direction so the sprite
    /// doesn't flip while shuffling laterally.
    /// 
    /// Setup:
    ///   Drop on the Player root GameObject.
    ///   Auto-finds the Sprite child's SpriteRenderer.
    /// </summary>
    public class SpriteFacing : MonoBehaviour
    {
        [Header("=== REFERENCES ===")]
        [Tooltip("The SpriteRenderer to flip. Auto-found on Sprite child if left empty.")]
        [SerializeField] private SpriteRenderer spriteRenderer;

        [Header("=== CONFIGURATION ===")]
        [Tooltip("If true, the sprite's default art faces right. " +
                 "If false, the default art faces left.")]
        [SerializeField] private bool defaultFacesRight = true;

        [Tooltip("Minimum horizontal speed before the sprite flips. " +
                 "Prevents flickering when nearly stationary.")]
        [SerializeField] private float flipDeadzone = 0.1f;

        // Future: assign directional sprites here
        // [Header("=== DIRECTIONAL SPRITES (future) ===")]
        // [SerializeField] private Sprite spriteRight;
        // [SerializeField] private Sprite spriteLeft;
        // [SerializeField] private Sprite spriteUp;
        // [SerializeField] private Sprite spriteDown;
        // [SerializeField] private Sprite spriteDiagUpRight;
        // [SerializeField] private Sprite spriteDiagUpLeft;
        // [SerializeField] private Sprite spriteDiagDownRight;
        // [SerializeField] private Sprite spriteDiagDownLeft;

        // ──────────────────────────────────────────────
        //  RUNTIME
        // ──────────────────────────────────────────────

        private BaseMovementController movement;
        private bool isFacingRight;

        // ──────────────────────────────────────────────
        //  INITIALIZATION
        // ──────────────────────────────────────────────

        private void Awake()
        {
            movement = GetComponent<BaseMovementController>();

            if (spriteRenderer == null)
            {
                var spriteChild = transform.Find("Sprite");
                if (spriteChild != null)
                {
                    spriteRenderer = spriteChild.GetComponent<SpriteRenderer>();
                }
            }

            isFacingRight = defaultFacesRight;
        }

        // ──────────────────────────────────────────────
        //  UPDATE
        // ──────────────────────────────────────────────

        private void LateUpdate()
        {
            if (movement == null || spriteRenderer == null) return;

            Vector2 facing = movement.GetFacingDirection();

            // Only flip if there's meaningful horizontal direction
            if (Mathf.Abs(facing.x) > flipDeadzone)
            {
                bool shouldFaceRight = facing.x > 0f;

                if (shouldFaceRight != isFacingRight)
                {
                    isFacingRight = shouldFaceRight;
                    UpdateFlip();
                }
            }

            // Future: multi-directional sprite selection would go here
            // float angle = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg;
            // SelectDirectionalSprite(angle);
        }

        // ──────────────────────────────────────────────
        //  FLIP LOGIC
        // ──────────────────────────────────────────────

        private void UpdateFlip()
        {
            if (defaultFacesRight)
            {
                spriteRenderer.flipX = !isFacingRight;
            }
            else
            {
                spriteRenderer.flipX = isFacingRight;
            }
        }
    }
}