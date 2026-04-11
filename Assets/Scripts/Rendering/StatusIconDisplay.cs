using UnityEngine;
using Sportland.Movement;
using Sportland.Sports.Tag;
using Sportland.InputHandling;

namespace Sportland.Rendering
{
    /// <summary>
    /// Displays debug status icons above a character's head.
    /// Shows up to 3 icon slots:
    ///   Left:   Movement State (Idle, Jogging, Sprinting, etc.)
    ///   Center: AI Decision State (Chasing, Fleeing, Standoff, etc.)
    ///   Right:  Shuffle/Feint State (Leaning Left, Leaning Right, Burst)
    /// 
    /// For AI characters, all three slots are active.
    /// For player characters, Center slot shows "Player Controlled" or is hidden.
    /// 
    /// Setup:
    ///   Drop on the Player root GameObject.
    ///   Assign icon sprites in the Inspector, or leave empty for colored placeholders.
    ///   Positioning auto-adjusts above the StaminaBar if present.
    /// </summary>
    public class StatusIconDisplay : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        //  CONFIGURATION
        // ──────────────────────────────────────────────

        [Header("=== POSITIONING ===")]
        [Tooltip("Height above the character's anchor point.")]
        [SerializeField] private float heightOffset = 2.2f;

        [Tooltip("Horizontal spacing between icon slots.")]
        [SerializeField] private float iconSpacing = 0.35f;

        [Tooltip("Size of each icon in world units.")]
        [SerializeField] private float iconSize = 0.25f;

        [Header("=== SORTING ===")]
        [SerializeField] private int sortingOrder = 110;

        [Header("=== MOVEMENT STATE ICONS ===")]
        [SerializeField] private Sprite iconIdle;
        [SerializeField] private Sprite iconJogging;
        [SerializeField] private Sprite iconSprinting;
        [SerializeField] private Sprite iconShuffling;
        [SerializeField] private Sprite iconCuttingPlant;
        [SerializeField] private Sprite iconCuttingRecovery;
        [SerializeField] private Sprite iconAirborne;
        [SerializeField] private Sprite iconDiving;
        [SerializeField] private Sprite iconDiveRecovery;
        [SerializeField] private Sprite iconLandingRecovery;
        [SerializeField] private Sprite iconStunned;

        [Header("=== AI DECISION ICONS ===")]
        [SerializeField] private Sprite iconChasing;
        [SerializeField] private Sprite iconIntercepting;
        [SerializeField] private Sprite iconCuttingOff;
        [SerializeField] private Sprite iconFleeing;
        [SerializeField] private Sprite iconWandering;
        [SerializeField] private Sprite iconStandoff;
        [SerializeField] private Sprite iconStalking;
        [SerializeField] private Sprite iconFleeingToZone;
        [SerializeField] private Sprite iconRestingInZone;
        [SerializeField] private Sprite iconPlayerControlled;

        [Header("=== FEINT STATE ICONS ===")]
        [SerializeField] private Sprite iconLeanLeft;
        [SerializeField] private Sprite iconLeanRight;
        [SerializeField] private Sprite iconShuffleBurst;

        // ──────────────────────────────────────────────
        //  ENUMS FOR STATE TRACKING
        // ──────────────────────────────────────────────

        public enum AIDecisionState
        {
            None,
            Chasing,
            Intercepting,   // chasing predicted future position, not current
            CuttingOff,     // repositioning to block runner's path to a safe zone
            Fleeing,
            Wandering,
            Standoff,
            Stalking,
            FleeingToZone,
            RestingInZone,
            PlayerControlled
        }

        public enum FeintState
        {
            None,
            LeanLeft,
            LeanRight,
            ShuffleBurst
        }

        // ──────────────────────────────────────────────
        //  RUNTIME
        // ──────────────────────────────────────────────

        private BaseMovementController movement;
        private InputBroker inputBroker;

        private Transform iconRoot;
        private SpriteRenderer movementIcon;
        private SpriteRenderer aiDecisionIcon;
        private SpriteRenderer feintIcon;

        private Sprite placeholderSprite;

        // Current tracked states
        private BaseMovementController.MovementState lastMovementState;
        private AIDecisionState lastAIState;
        private FeintState lastFeintState;

        // ──────────────────────────────────────────────
        //  PLACEHOLDER COLORS (when no sprite assigned)
        // ──────────────────────────────────────────────

        // Movement
        private static readonly Color colorIdle = new Color(0.5f, 0.5f, 0.5f, 0.8f);
        private static readonly Color colorJogging = new Color(0.3f, 0.7f, 0.3f, 0.8f);
        private static readonly Color colorSprinting = new Color(0.2f, 1f, 0.2f, 0.8f);
        private static readonly Color colorShuffling = new Color(0.3f, 0.3f, 0.9f, 0.8f);
        private static readonly Color colorCuttingPlant = new Color(1f, 0.5f, 0f, 0.8f);
        private static readonly Color colorCuttingRecovery = new Color(1f, 0.7f, 0.3f, 0.8f);
        private static readonly Color colorAirborne = new Color(0.5f, 0.8f, 1f, 0.8f);
        private static readonly Color colorDiving = new Color(1f, 0.3f, 0.3f, 0.8f);
        private static readonly Color colorDiveRecovery = new Color(0.8f, 0.2f, 0.2f, 0.8f);
        private static readonly Color colorLandingRecovery = new Color(0.6f, 0.6f, 0.8f, 0.8f);
        private static readonly Color colorStunned = new Color(0.3f, 0.3f, 0.3f, 0.8f);

        // AI Decision
        private static readonly Color colorChasing = new Color(1f, 0.2f, 0.2f, 0.8f);
        private static readonly Color colorIntercepting = new Color(0.2f, 0.8f, 1f, 0.8f);   // cyan — predictive lock-on
        private static readonly Color colorCuttingOff = new Color(1f, 0.45f, 0f, 0.8f);       // orange — blocking escape
        private static readonly Color colorFleeing = new Color(1f, 1f, 0.2f, 0.8f);
        private static readonly Color colorWandering = new Color(0.6f, 0.8f, 0.6f, 0.8f);
        private static readonly Color colorStandoff = new Color(0.8f, 0.2f, 0.8f, 0.8f);
        private static readonly Color colorStalking = new Color(0.8f, 0.4f, 0.2f, 0.8f);
        private static readonly Color colorFleeingToZone = new Color(0.2f, 1f, 0.8f, 0.8f);
        private static readonly Color colorRestingInZone = new Color(0.2f, 0.6f, 0.4f, 0.8f);
        private static readonly Color colorPlayerControlled = new Color(1f, 1f, 1f, 0.5f);

        // Feint
        private static readonly Color colorLeanLeft = new Color(0.9f, 0.5f, 0.9f, 0.8f);
        private static readonly Color colorLeanRight = new Color(0.5f, 0.9f, 0.9f, 0.8f);
        private static readonly Color colorShuffleBurst = new Color(1f, 1f, 0.5f, 0.8f);
        private static readonly Color colorNone = new Color(0f, 0f, 0f, 0f); // invisible

        // ──────────────────────────────────────────────
        //  INITIALIZATION
        // ──────────────────────────────────────────────

        private void Awake()
        {
            movement = GetComponent<BaseMovementController>();
            inputBroker = GetComponent<InputBroker>();
            placeholderSprite = CreateSquareSprite();
            CreateIconObjects();
        }

        private void CreateIconObjects()
        {
            iconRoot = new GameObject("StatusIcons").transform;
            iconRoot.SetParent(transform);
            iconRoot.localPosition = new Vector3(0f, heightOffset, 0f);

            movementIcon = CreateIconSlot("MovementState", -iconSpacing);
            aiDecisionIcon = CreateIconSlot("AIDecision", 0f);
            feintIcon = CreateIconSlot("FeintState", iconSpacing);
        }

        private SpriteRenderer CreateIconSlot(string name, float xOffset)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(iconRoot);
            go.transform.localPosition = new Vector3(xOffset, 0f, 0f);
            go.transform.localScale = new Vector3(iconSize, iconSize, 1f);

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = placeholderSprite;
            sr.sortingOrder = sortingOrder;
            sr.color = colorNone;
            return sr;
        }

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

            UpdateMovementIcon();
            UpdateAIDecisionIcon();
            UpdateFeintIcon();
        }

        // ──────────────────────────────────────────────
        //  MOVEMENT STATE ICON
        // ──────────────────────────────────────────────

        private void UpdateMovementIcon()
        {
            var state = movement.CurrentState;
            if (state == lastMovementState) return;
            lastMovementState = state;

            // Check for cut recovery vs plant
            bool inCutRecovery = false;
            if (state == BaseMovementController.MovementState.Cutting)
            {
                // We can't directly access isInCutRecovery, so use speed as a proxy
                // Plant phase = speed is 0, Recovery phase = speed > 0
                inCutRecovery = movement.GetCurrentSpeed() > 0.1f;
            }

            Sprite icon = null;
            Color fallback = colorIdle;

            switch (state)
            {
                case BaseMovementController.MovementState.Idle:
                    icon = iconIdle; fallback = colorIdle; break;
                case BaseMovementController.MovementState.Jogging:
                    icon = iconJogging; fallback = colorJogging; break;
                case BaseMovementController.MovementState.Sprinting:
                    icon = iconSprinting; fallback = colorSprinting; break;
                case BaseMovementController.MovementState.Shuffling:
                    icon = iconShuffling; fallback = colorShuffling; break;
                case BaseMovementController.MovementState.Cutting:
                    if (inCutRecovery)
                    { icon = iconCuttingRecovery; fallback = colorCuttingRecovery; }
                    else
                    { icon = iconCuttingPlant; fallback = colorCuttingPlant; }
                    break;
                case BaseMovementController.MovementState.Airborne:
                    icon = iconAirborne; fallback = colorAirborne; break;
                case BaseMovementController.MovementState.Diving:
                    icon = iconDiving; fallback = colorDiving; break;
                case BaseMovementController.MovementState.DiveRecovery:
                    icon = iconDiveRecovery; fallback = colorDiveRecovery; break;
                case BaseMovementController.MovementState.LandingRecovery:
                    icon = iconLandingRecovery; fallback = colorLandingRecovery; break;
                case BaseMovementController.MovementState.Stunned:
                    icon = iconStunned; fallback = colorStunned; break;
            }

            ApplyIcon(movementIcon, icon, fallback);
        }

        // ──────────────────────────────────────────────
        //  AI DECISION ICON
        // ──────────────────────────────────────────────

        /// <summary>
        /// AI decision state must be set externally by the AIInputSource
        /// via SetAIDecisionState(), since the AI's internal decision
        /// isn't directly accessible from the rendering layer.
        /// </summary>
        private AIDecisionState currentAIState = AIDecisionState.None;

        public void SetAIDecisionState(AIDecisionState state)
        {
            currentAIState = state;
        }

        private void UpdateAIDecisionIcon()
        {
            if (currentAIState == lastAIState) return;
            lastAIState = currentAIState;

            Sprite icon = null;
            Color fallback = colorNone;

            switch (currentAIState)
            {
                case AIDecisionState.None:
                    icon = null; fallback = colorNone; break;
                case AIDecisionState.Chasing:
                    icon = iconChasing; fallback = colorChasing; break;
                case AIDecisionState.Intercepting:
                    icon = iconIntercepting; fallback = colorIntercepting; break;
                case AIDecisionState.CuttingOff:
                    icon = iconCuttingOff; fallback = colorCuttingOff; break;
                case AIDecisionState.Fleeing:
                    icon = iconFleeing; fallback = colorFleeing; break;
                case AIDecisionState.Wandering:
                    icon = iconWandering; fallback = colorWandering; break;
                case AIDecisionState.Standoff:
                    icon = iconStandoff; fallback = colorStandoff; break;
                case AIDecisionState.Stalking:
                    icon = iconStalking; fallback = colorStalking; break;
                case AIDecisionState.FleeingToZone:
                    icon = iconFleeingToZone; fallback = colorFleeingToZone; break;
                case AIDecisionState.RestingInZone:
                    icon = iconRestingInZone; fallback = colorRestingInZone; break;
                case AIDecisionState.PlayerControlled:
                    icon = iconPlayerControlled; fallback = colorPlayerControlled; break;
            }

            ApplyIcon(aiDecisionIcon, icon, fallback);
        }

        // ──────────────────────────────────────────────
        //  FEINT STATE ICON
        // ──────────────────────────────────────────────

        private void UpdateFeintIcon()
        {
            FeintState state = FeintState.None;

            if (movement.CurrentState == BaseMovementController.MovementState.Shuffling)
            {
                if (movement.IsLeaning)
                {
                    // Determine lean direction relative to facing
                    Vector2 right = Vector2.Perpendicular(movement.GetFacingDirection()) * -1f;
                    float dot = Vector2.Dot(movement.LeanDirection, right);
                    state = dot > 0f ? FeintState.LeanRight : FeintState.LeanLeft;
                }
            }

            // TODO: detect shuffle burst state when we expose it

            if (state == lastFeintState) return;
            lastFeintState = state;

            Sprite icon = null;
            Color fallback = colorNone;

            switch (state)
            {
                case FeintState.None:
                    icon = null; fallback = colorNone; break;
                case FeintState.LeanLeft:
                    icon = iconLeanLeft; fallback = colorLeanLeft; break;
                case FeintState.LeanRight:
                    icon = iconLeanRight; fallback = colorLeanRight; break;
                case FeintState.ShuffleBurst:
                    icon = iconShuffleBurst; fallback = colorShuffleBurst; break;
            }

            ApplyIcon(feintIcon, icon, fallback);
        }

        // ──────────────────────────────────────────────
        //  ICON APPLICATION
        // ──────────────────────────────────────────────

        private void ApplyIcon(SpriteRenderer renderer, Sprite icon, Color fallbackColor)
        {
            if (icon != null)
            {
                renderer.sprite = icon;
                renderer.color = Color.white;
            }
            else
            {
                renderer.sprite = placeholderSprite;
                renderer.color = fallbackColor;
            }
        }
    }
}