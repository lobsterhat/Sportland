using UnityEngine;
using System.Collections.Generic;

namespace Sportland.Rendering.Characters
{
    /// <summary>
    /// Master catalog of all available character parts and body types.
    ///
    /// Keep one instance in Resources/Characters/ so any system can load it.
    /// The character-creation UI reads from this to populate its picker lists.
    ///
    /// Create Asset → Sportland/Characters/Part Library
    /// </summary>
    [CreateAssetMenu(fileName = "Character Part Library",
                     menuName = "Sportland/Characters/Part Library")]
    public class CharacterPartLibrary : ScriptableObject
    {
        [Header("Body Types (one per gender × weight class)")]
        public List<CharacterBodyType> bodyTypes = new List<CharacterBodyType>();

        [Header("Heads")]
        public List<CharacterPartVariant> heads = new List<CharacterPartVariant>();

        [Header("Hair")]
        public List<CharacterPartVariant> hairstyles = new List<CharacterPartVariant>();

        [Header("Eyes")]
        public List<CharacterPartVariant> eyes = new List<CharacterPartVariant>();

        [Header("Eyebrows")]
        public List<CharacterPartVariant> eyebrows = new List<CharacterPartVariant>();

        [Header("Mouths")]
        public List<CharacterPartVariant> mouths = new List<CharacterPartVariant>();

        [Header("Noses")]
        public List<CharacterPartVariant> noses = new List<CharacterPartVariant>();

        [Header("Facial Hair (male)")]
        public List<CharacterPartVariant> facialHair = new List<CharacterPartVariant>();

        // ── Body Type Lookup ──────────────────────────────────────

        public CharacterBodyType GetBodyType(CharacterGender gender, BodyWeightClass weightClass)
        {
            foreach (var bt in bodyTypes)
            {
                if (bt.gender == gender && bt.weightClass == weightClass)
                    return bt;
            }
            // Fallback: same gender any weight class
            foreach (var bt in bodyTypes)
            {
                if (bt.gender == gender)
                    return bt;
            }
            return bodyTypes.Count > 0 ? bodyTypes[0] : null;
        }

        // ── Part List Lookup ──────────────────────────────────────

        /// <summary>
        /// Returns all parts in <paramref name="category"/> compatible with <paramref name="gender"/>.
        /// Pass <see cref="CharacterGender.Any"/> to skip gender filtering.
        /// </summary>
        public List<CharacterPartVariant> GetParts(CharacterPartCategory category,
                                                   CharacterGender gender = CharacterGender.Any)
        {
            var source = GetRawList(category);
            if (gender == CharacterGender.Any) return new List<CharacterPartVariant>(source);

            var result = new List<CharacterPartVariant>(source.Count);
            foreach (var part in source)
            {
                if (part != null &&
                    (part.compatibleGender == CharacterGender.Any ||
                     part.compatibleGender == gender))
                {
                    result.Add(part);
                }
            }
            return result;
        }

        private List<CharacterPartVariant> GetRawList(CharacterPartCategory category) =>
            category switch
            {
                CharacterPartCategory.Head       => heads,
                CharacterPartCategory.Hair       => hairstyles,
                CharacterPartCategory.Eyes       => eyes,
                CharacterPartCategory.Eyebrows   => eyebrows,
                CharacterPartCategory.Mouth      => mouths,
                CharacterPartCategory.Nose       => noses,
                CharacterPartCategory.FacialHair => facialHair,
                _                                => new List<CharacterPartVariant>()
            };
    }
}
