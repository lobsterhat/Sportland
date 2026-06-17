using UnityEngine;

namespace Sportland.Rendering.Characters
{
    /// <summary>
    /// The visual blueprint for one character.
    ///
    /// Assign this to an Athlete asset (or create it inline) then give it to
    /// ModularCharacterRig.ApplyAppearance() to instantly re-dress the character.
    ///
    /// All fields are public so a character-creation UI can write directly to them,
    /// then call ApplyAppearance to refresh the rig.
    ///
    /// Create Asset → Sportland/Characters/Appearance
    /// </summary>
    [CreateAssetMenu(fileName = "New Character Appearance",
                     menuName = "Sportland/Characters/Appearance")]
    public class CharacterAppearance : ScriptableObject
    {
        // ── Body ──────────────────────────────────────────────────

        [Header("Body")]
        public CharacterGender gender = CharacterGender.Male;

        [Range(0f, 1f)]
        [Tooltip("0 = minimum height for this gender, 1 = maximum.")]
        public float heightNormalized = 0.5f;

        [Range(0f, 1f)]
        [Tooltip("0 = slim build, 1 = heavy build.")]
        public float weightNormalized = 0.5f;

        // ── Part Slots ────────────────────────────────────────────

        [Header("Face")]
        public CharacterPartVariant headVariant;
        public CharacterPartVariant eyebrowsVariant;
        public CharacterPartVariant eyesVariant;
        public CharacterPartVariant noseVariant;
        public CharacterPartVariant mouthVariant;

        [Tooltip("Leave null for clean-shaven or female characters.")]
        public CharacterPartVariant facialHairVariant;

        [Header("Hair")]
        public CharacterPartVariant hairVariant;

        // ── Colors ────────────────────────────────────────────────

        [Header("Colors")]
        public Color skinColor  = new Color(1.00f, 0.82f, 0.70f, 1f);
        public Color hairColor  = new Color(0.20f, 0.15f, 0.10f, 1f);
        public Color eyeColor   = new Color(0.30f, 0.40f, 0.80f, 1f);

        // ── Helpers ───────────────────────────────────────────────

        public BodyWeightClass GetWeightClass()
        {
            if (weightNormalized < 0.33f) return BodyWeightClass.Slim;
            if (weightNormalized < 0.67f) return BodyWeightClass.Athletic;
            return BodyWeightClass.Heavy;
        }

        public Color GetTintForTarget(CharacterPartVariant.TintTarget target)
        {
            return target switch
            {
                CharacterPartVariant.TintTarget.Skin => skinColor,
                CharacterPartVariant.TintTarget.Hair => hairColor,
                CharacterPartVariant.TintTarget.Eyes => eyeColor,
                _                                    => Color.white
            };
        }
    }
}
