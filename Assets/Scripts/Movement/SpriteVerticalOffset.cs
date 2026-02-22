using UnityEngine;

/// <summary>
/// Manages the visual separation between the ground-plane collider (parent)
/// and the character sprite (child). When the character jumps, the sprite
/// moves up while the shadow/collider stays grounded.
/// 
/// Setup:
///   Parent GameObject — Rigidbody2D, Collider2D, MovementController
///     └── Sprite Child — SpriteRenderer (this is what gets offset)
///     └── Shadow Child  — SpriteRenderer (optional, stays at y=0)
///     
/// Attach this to the PARENT alongside the movement controller.
/// Assign the sprite child in the inspector.
/// </summary>
public class SpriteVerticalOffset : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The child transform that holds the character sprite. " +
             "This gets moved up/down to fake vertical movement.")]
    [SerializeField] private Transform spriteTransform;

    [Tooltip("Optional shadow transform. Stays grounded, scales with height " +
             "to sell the illusion.")]
    [SerializeField] private Transform shadowTransform;

    [Header("Shadow Settings")]
    [Tooltip("Shadow scale at ground level (height = 0).")]
    [SerializeField] private Vector3 shadowBaseScale = Vector3.one;

    [Tooltip("Shadow scale at max jump height. Shrinks to imply distance.")]
    [SerializeField] private Vector3 shadowMinScale = new Vector3(0.6f, 0.4f, 1f);

    [Tooltip("Shadow alpha at ground level.")]
    [SerializeField] private float shadowBaseAlpha = 0.5f;

    [Tooltip("Shadow alpha at max height.")]
    [SerializeField] private float shadowMinAlpha = 0.2f;

    [Tooltip("Reference height for shadow scaling (use jump height from profile).")]
    [SerializeField] private float referenceHeight = 1.5f;

    // Cached
    private SpriteRenderer shadowRenderer;
    private float currentOffset;

    private void Awake()
    {
        if (shadowTransform != null)
        {
            shadowRenderer = shadowTransform.GetComponent<SpriteRenderer>();
        }
    }

    /// <summary>
    /// Set the visual vertical offset. Called by the movement controller.
    /// </summary>
    public void SetOffset(float height)
    {
        currentOffset = Mathf.Max(0f, height);

        // Move sprite up
        if (spriteTransform != null)
        {
            Vector3 pos = spriteTransform.localPosition;
            pos.y = currentOffset;
            spriteTransform.localPosition = pos;
        }

        // Update shadow
        if (shadowTransform != null)
        {
            float t = Mathf.Clamp01(currentOffset / referenceHeight);

            // Scale shadow
            shadowTransform.localScale = Vector3.Lerp(shadowBaseScale, shadowMinScale, t);

            // Fade shadow
            if (shadowRenderer != null)
            {
                Color c = shadowRenderer.color;
                c.a = Mathf.Lerp(shadowBaseAlpha, shadowMinAlpha, t);
                shadowRenderer.color = c;
            }
        }
    }

    public float GetOffset() => currentOffset;
}
