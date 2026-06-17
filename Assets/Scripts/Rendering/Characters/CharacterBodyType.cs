using UnityEngine;

namespace Sportland.Rendering.Characters
{
    /// <summary>
    /// Defines the base body art and scale rules for one gender + weight-class combination.
    ///
    /// Six combinations cover the full range:
    ///   Male   × Slim / Athletic / Heavy
    ///   Female × Slim / Athletic / Heavy
    ///
    /// The rig interpolates height scale between min/max based on
    /// CharacterAppearance.heightNormalized (0–1).
    ///
    /// Create Asset → Sportland/Characters/Body Type
    /// </summary>
    [CreateAssetMenu(fileName = "New Body Type",
                     menuName = "Sportland/Characters/Body Type")]
    public class CharacterBodyType : ScriptableObject
    {
        [Header("Identity")]
        public CharacterGender gender;
        public BodyWeightClass weightClass;

        [Header("Body Sprites")]
        [Tooltip("The animated torso+legs part for this body type.")]
        public CharacterPartVariant bodyPartVariant;

        [Header("Height Scale Range")]
        [Tooltip("Scale applied to the BodyRoot for the shortest athlete in this class.")]
        [Min(0.1f)] public float minHeightScale = 0.85f;

        [Tooltip("Scale applied to the BodyRoot for the tallest athlete in this class.")]
        [Min(0.1f)] public float maxHeightScale = 1.20f;

        [Header("Width Scale")]
        [Tooltip("Horizontal stretch multiplier for this weight class (1 = no change).")]
        [Min(0.1f)] public float widthScaleMultiplier = 1.0f;

        // ── API ───────────────────────────────────────────────────

        /// <param name="normalizedHeight">0–1 from CharacterAppearance.heightNormalized</param>
        public float GetHeightScale(float normalizedHeight) =>
            Mathf.Lerp(minHeightScale, maxHeightScale, Mathf.Clamp01(normalizedHeight));
    }
}
