using UnityEngine;

namespace Sportland.World
{
    /// <summary>
    /// Defines all movement and action modifiers for a surface type.
    ///
    /// Create instances via Assets → Create → Sportland → Surface Definition
    /// to tune values per-project, or rely on the built-in defaults via
    /// SurfaceDefinition.CreateDefault(SurfaceType).
    /// </summary>
    [CreateAssetMenu(menuName = "Sportland/Surface Definition", fileName = "NewSurface")]
    public class SurfaceDefinition : ScriptableObject
    {
        // ──────────────────────────────────────────────
        //  IDENTITY
        // ──────────────────────────────────────────────

        [Header("Identity")]
        public string surfaceName = "Surface";
        public SurfaceType surfaceType = SurfaceType.Asphalt;

        [Tooltip("Higher-priority surfaces win when multiple zones overlap. " +
                 "Hard/paved = 0, natural = 1, sand/mud = 3, water = 5.")]
        [Range(0, 10)]
        public int priority = 0;

        [Tooltip("Color used to tint the zone gizmo in the editor.")]
        public Color gizmoColor = new Color(0.5f, 0.5f, 0.5f, 0.4f);

        // ──────────────────────────────────────────────
        //  MOVEMENT MODIFIERS
        // ──────────────────────────────────────────────

        [Header("Movement Modifiers")]
        [Tooltip("Top-speed multiplier. 1.0 = no change.")]
        [Range(0.1f, 2f)] public float speedMultiplier = 1f;

        [Tooltip("Acceleration multiplier.")]
        [Range(0.1f, 2f)] public float accelerationMultiplier = 1f;

        [Tooltip("Deceleration multiplier.")]
        [Range(0.1f, 2f)] public float decelerationMultiplier = 1f;

        [Tooltip("Traction multiplier — scales turn speed and reduces hard-cut plant time " +
                 "when high, increases it when low.")]
        [Range(0.1f, 2f)] public float tractionMultiplier = 1f;

        // ──────────────────────────────────────────────
        //  SLOPE (Slope type only)
        // ──────────────────────────────────────────────

        [Header("Slope (SurfaceType.Slope only)")]
        [Tooltip("World-space direction that points downhill. " +
                 "Moving with this direction gains speed; against it loses speed.")]
        public Vector2 slopeDirection = Vector2.down;

        [Tooltip("Max speed delta from slope. 0.25 = ±25% depending on movement direction.")]
        [Range(0f, 0.6f)] public float slopeSpeedDelta = 0.25f;

        // ──────────────────────────────────────────────
        //  STAMINA
        // ──────────────────────────────────────────────

        [Header("Stamina")]
        [Tooltip(">1 = more tiring, <1 = less tiring.")]
        [Range(0f, 3f)] public float staminaDrainMultiplier = 1f;

        // ──────────────────────────────────────────────
        //  ACTION RESTRICTIONS
        // ──────────────────────────────────────────────

        [Header("Action Restrictions")]
        public bool allowSprint     = true;
        public bool allowJump       = true;
        public bool allowDive       = true;
        public bool allowLunge      = true;  // Tag: It lunge
        public bool allowBarge      = true;  // Tag: Runner barge

        // ──────────────────────────────────────────────
        //  SLOPE HELPER
        // ──────────────────────────────────────────────

        /// <summary>
        /// Returns a speed multiplier based on how aligned the movement direction
        /// is with the slope. Only meaningful for Slope surfaces.
        /// Returns 1.0 for all other surface types.
        /// </summary>
        public float GetSlopeSpeedMultiplier(Vector2 movementDirection)
        {
            if (surfaceType != SurfaceType.Slope) return 1f;
            if (slopeSpeedDelta <= 0f || slopeDirection.sqrMagnitude < 0.01f) return 1f;

            float dot = Vector2.Dot(movementDirection.normalized, slopeDirection.normalized);
            return 1f + dot * slopeSpeedDelta;
        }

        // ──────────────────────────────────────────────
        //  DEFAULT FACTORY
        // ──────────────────────────────────────────────

        /// <summary>
        /// Creates a runtime SurfaceDefinition instance with sensible defaults
        /// for the given surface type. Used by SurfaceZone when no custom
        /// ScriptableObject is assigned.
        /// </summary>
        public static SurfaceDefinition CreateDefault(SurfaceType type)
        {
            var def = CreateInstance<SurfaceDefinition>();
            ApplyDefaults(def, type);
            return def;
        }

        /// <summary>
        /// Returns the editor gizmo colour for a surface type without
        /// allocating a full SurfaceDefinition instance.
        /// </summary>
        public static Color GetDefaultGizmoColor(SurfaceType type)
        {
            return type switch
            {
                SurfaceType.Asphalt     => new Color(0.25f, 0.25f, 0.25f, 0.45f),
                SurfaceType.Concrete    => new Color(0.72f, 0.72f, 0.72f, 0.45f),
                SurfaceType.SportCourt  => new Color(0.90f, 0.60f, 0.10f, 0.45f),
                SurfaceType.Grass       => new Color(0.20f, 0.70f, 0.20f, 0.45f),
                SurfaceType.Gravel      => new Color(0.60f, 0.55f, 0.40f, 0.45f),
                SurfaceType.Sand        => new Color(0.95f, 0.87f, 0.50f, 0.45f),
                SurfaceType.Mud         => new Color(0.50f, 0.35f, 0.10f, 0.50f),
                SurfaceType.Water       => new Color(0.20f, 0.50f, 0.90f, 0.45f),
                SurfaceType.Slope       => new Color(0.80f, 0.60f, 0.30f, 0.45f),
                SurfaceType.Undergrowth => new Color(0.10f, 0.45f, 0.10f, 0.55f),
                _                       => new Color(0.50f, 0.50f, 0.50f, 0.45f),
            };
        }

        private static void ApplyDefaults(SurfaceDefinition def, SurfaceType type)
        {
            def.surfaceType = type;
            def.gizmoColor  = GetDefaultGizmoColor(type);

            switch (type)
            {
                case SurfaceType.Asphalt:
                    def.surfaceName = "Asphalt";
                    def.priority    = 0;
                    // All multipliers already 1.0 from field initialisers
                    break;

                case SurfaceType.Concrete:
                    def.surfaceName             = "Concrete";
                    def.priority                = 0;
                    def.accelerationMultiplier  = 1.05f;
                    def.tractionMultiplier      = 1.05f;
                    def.staminaDrainMultiplier  = 0.95f;
                    break;

                case SurfaceType.SportCourt:
                    def.surfaceName             = "Sport Court";
                    def.priority                = 0;
                    def.accelerationMultiplier  = 1.15f;
                    def.tractionMultiplier      = 1.20f;
                    def.staminaDrainMultiplier  = 0.90f;
                    break;

                case SurfaceType.Grass:
                    def.surfaceName             = "Grass";
                    def.priority                = 1;
                    def.speedMultiplier         = 0.93f;
                    def.accelerationMultiplier  = 0.90f;
                    def.tractionMultiplier      = 0.90f;
                    break;

                case SurfaceType.Gravel:
                    def.surfaceName             = "Gravel";
                    def.priority                = 1;
                    def.speedMultiplier         = 0.85f;
                    def.accelerationMultiplier  = 0.85f;
                    def.tractionMultiplier      = 0.80f;
                    def.staminaDrainMultiplier  = 1.15f;
                    break;

                case SurfaceType.Sand:
                    def.surfaceName             = "Sand";
                    def.priority                = 3;
                    def.speedMultiplier         = 0.65f;
                    def.accelerationMultiplier  = 0.65f;
                    def.tractionMultiplier      = 0.60f;
                    def.staminaDrainMultiplier  = 1.60f;
                    def.allowSprint             = false;
                    break;

                case SurfaceType.Mud:
                    def.surfaceName             = "Mud";
                    def.priority                = 4;
                    def.speedMultiplier         = 0.55f;
                    def.accelerationMultiplier  = 0.60f;
                    def.tractionMultiplier      = 0.50f;
                    def.staminaDrainMultiplier  = 1.50f;
                    def.allowSprint             = false;
                    break;

                case SurfaceType.Water:
                    def.surfaceName             = "Water";
                    def.priority                = 5;
                    def.speedMultiplier         = 0.45f;
                    def.accelerationMultiplier  = 0.50f;
                    def.tractionMultiplier      = 0.40f;
                    def.staminaDrainMultiplier  = 2.00f;
                    def.allowSprint             = false;
                    def.allowDive               = false;
                    def.allowJump               = false;
                    def.allowLunge              = false;
                    def.allowBarge              = false;
                    break;

                case SurfaceType.Slope:
                    def.surfaceName             = "Slope";
                    def.priority                = 2;
                    def.tractionMultiplier      = 0.90f;
                    def.slopeSpeedDelta         = 0.25f;
                    // slopeDirection must be set per-instance on the SurfaceZone
                    break;

                case SurfaceType.Undergrowth:
                    def.surfaceName             = "Undergrowth";
                    def.priority                = 4;
                    def.speedMultiplier         = 0.40f;
                    def.accelerationMultiplier  = 0.50f;
                    def.tractionMultiplier      = 0.70f;
                    def.staminaDrainMultiplier  = 1.80f;
                    def.allowSprint             = false;
                    def.allowDive               = false;
                    def.allowLunge              = false;
                    break;
            }
        }
    }
}
