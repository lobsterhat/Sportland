using UnityEngine;

namespace Sportland.Rendering.Characters
{
    /// <summary>
    /// One interchangeable visual option for a single body part.
    ///
    /// Each variant holds animation data per Animator state (Idle, Walk, Run …).
    /// The rig picks the right clip at runtime — no animation asset needs to be
    /// re-authored when a part is swapped.
    ///
    /// Setup:
    ///   Create Asset → Sportland/Characters/Part Variant
    ///   Set category, gender compatibility, then add one AnimationClip entry per
    ///   Animator state and fill its Frames array with the pixel-art sprite frames.
    /// </summary>
    [CreateAssetMenu(fileName = "New Character Part",
                     menuName = "Sportland/Characters/Part Variant")]
    public class CharacterPartVariant : ScriptableObject
    {
        [Header("Identity")]
        public string partName;
        public CharacterPartCategory category;

        [Header("Compatibility")]
        [Tooltip("Which gender this part is intended for. 'Any' works for both.")]
        public CharacterGender compatibleGender = CharacterGender.Any;

        [Tooltip("Which weight classes this part has art for. Parts without a matching class fall back to Athletic.")]
        public BodyWeightClass[] compatibleWeightClasses = new[]
        {
            BodyWeightClass.Slim,
            BodyWeightClass.Athletic,
            BodyWeightClass.Heavy
        };

        [Header("Animation Clips")]
        [Tooltip("One entry per Animator state. The state name must match exactly " +
                 "(e.g. 'Idle', 'Walk'). Frames are played at the specified FPS.")]
        public PartAnimationClip[] clips;

        [Header("Placement")]
        [Tooltip("Pixel-offset from the body anchor, in world units.")]
        public Vector2 pivotOffset;

        [Tooltip("Added on top of the layer's base sorting order.")]
        public int sortingOrderBump;

        [Header("Tinting")]
        [Tooltip("If true the rig applies the skin/hair/eye color from CharacterAppearance.")]
        public bool receivesTint;
        public TintTarget tintTarget = TintTarget.None;

        public enum TintTarget { None, Skin, Hair, Eyes }

        // ── Lookup ─────────────────────────────────────────────────

        /// <summary>Returns the clip for <paramref name="stateName"/>, falling back to Idle.</summary>
        public PartAnimationClip GetClip(string stateName)
        {
            if (clips == null) return null;

            PartAnimationClip idleFallback = null;

            foreach (var clip in clips)
            {
                if (clip.stateName == stateName) return clip;
                if (clip.stateName == AnimationStates.Idle) idleFallback = clip;
            }

            return idleFallback;
        }
    }

    [System.Serializable]
    public class PartAnimationClip
    {
        [Tooltip("Must match the Animator state name exactly.")]
        public string stateName = AnimationStates.Idle;

        [Tooltip("Frames in playback order.")]
        public Sprite[] frames;

        [Tooltip("Playback speed in frames-per-second.")]
        [Min(0.1f)] public float fps = 12f;

        [Tooltip("If true the clip loops; otherwise it holds the last frame.")]
        public bool loop = true;

        public float FrameDuration => 1f / fps;
    }
}
