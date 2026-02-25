using UnityEngine;
using Sportland.Movement;

namespace Sportland.Rendering
{
    /// <summary>
    /// Floating stamina bar that appears above a character's head.
    /// Uses world-space sprites so it moves with the character.
    /// 
    /// Color shifts from green (full) → yellow (half) → red (empty).
    /// 
    /// Setup: Drop this on the Player root GameObject.
    /// It creates its own child objects automatically.
    /// </summary>
    public class StaminaBar : MonoBehaviour
    {
        [Header("=== POSITIONING ===")]
        [Tooltip("Height above the character's sprite anchor.")]
        [SerializeField] private float heightOffset = 1.8f;

        [Header("=== SIZING ===")]
        [Tooltip("Full width of the bar in world units.")]
        [SerializeField] private float barWidth = 1.2f;

        [Tooltip("Height of the bar in world units.")]
        [SerializeField] private float barHeight = 0.12f;

        [Header("=== COLORS ===")]
        [SerializeField] private Color fullColor = new Color(0.2f, 0.9f, 0.2f, 0.9f);
        [SerializeField] private Color midColor = new Color(0.9f, 0.9f, 0.2f, 0.9f);
        [SerializeField] private Color lowColor = new Color(0.9f, 0.2f, 0.2f, 0.9f);
        [SerializeField] private Color backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.7f);

        [Header("=== VISIBILITY ===")]
        [Tooltip("Hide the bar when stamina is full after this delay (seconds). 0 = always visible.")]
        [SerializeField] private float hideWhenFullDelay = 2f;

        [Tooltip("Sorting order for the bar (should be above character sprites).")]
        [SerializeField] private int sortingOrder = 100;

        // ──────────────────────────────────────────────
        //  RUNTIME
        // ──────────────────────────────────────────────

        private BaseMovementController movement;
        private Transform barRoot;
        private Transform fillTransform;
        private SpriteRenderer fillRenderer;
        private SpriteRenderer bgRenderer;
        private float hideTimer;
        private float lastStamina = 1f;

        // ──────────────────────────────────────────────
        //  INITIALIZATION
        // ──────────────────────────────────────────────

        private void Awake()
        {
            movement = GetComponent<BaseMovementController>();
            CreateBarObjects();
        }

        private void CreateBarObjects()
        {
            // Root object that follows the character
            barRoot = new GameObject("StaminaBar").transform;
            barRoot.SetParent(transform);
            barRoot.localPosition = new Vector3(0f, heightOffset, 0f);

            // Background bar
            GameObject bg = new GameObject("Background");
            bg.transform.SetParent(barRoot);
            bg.transform.localPosition = Vector3.zero;
            bg.transform.localScale = new Vector3(barWidth, barHeight, 1f);
            bgRenderer = bg.AddComponent<SpriteRenderer>();
            bgRenderer.sprite = CreateSquareSprite();
            bgRenderer.color = backgroundColor;
            bgRenderer.sortingOrder = sortingOrder;

            // Fill bar
            GameObject fill = new GameObject("Fill");
            fill.transform.SetParent(barRoot);
            fill.transform.localPosition = Vector3.zero;
            fill.transform.localScale = new Vector3(barWidth, barHeight, 1f);
            fillRenderer = fill.AddComponent<SpriteRenderer>();
            fillRenderer.sprite = CreateSquareSprite();
            fillRenderer.color = fullColor;
            fillRenderer.sortingOrder = sortingOrder + 1;
            fillTransform = fill.transform;
        }

        /// <summary>
        /// Creates a simple 1x1 white pixel sprite at runtime.
        /// No external sprite asset needed.
        /// </summary>
        private Sprite CreateSquareSprite()
        {
            Texture2D tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }

        // ──────────────────────────────────────────────
        //  UPDATE
        // ──────────────────────────────────────────────

        private void LateUpdate()
        {
            if (movement == null) return;

            float stamina = movement.GetNormalizedStamina();

            // Update fill width — scale X from the left edge
            float fillScale = Mathf.Clamp01(stamina) * barWidth;
            fillTransform.localScale = new Vector3(fillScale, barHeight, 1f);

            // Offset fill so it shrinks from right to left
            float offset = (fillScale - barWidth) * 0.5f;
            fillTransform.localPosition = new Vector3(offset, 0f, 0f);

            // Color gradient: full → mid → low
            Color barColor;
            if (stamina > 0.5f)
            {
                barColor = Color.Lerp(midColor, fullColor, (stamina - 0.5f) * 2f);
            }
            else
            {
                barColor = Color.Lerp(lowColor, midColor, stamina * 2f);
            }
            fillRenderer.color = barColor;

            // Visibility — hide when full after delay
            if (hideWhenFullDelay > 0f)
            {
                if (stamina >= 0.99f)
                {
                    hideTimer += Time.deltaTime;
                    if (hideTimer >= hideWhenFullDelay)
                    {
                        SetBarVisible(false);
                    }
                }
                else
                {
                    hideTimer = 0f;
                    SetBarVisible(true);
                }
            }

            lastStamina = stamina;
        }

        private void SetBarVisible(bool visible)
        {
            if (bgRenderer != null) bgRenderer.enabled = visible;
            if (fillRenderer != null) fillRenderer.enabled = visible;
        }
    }
}