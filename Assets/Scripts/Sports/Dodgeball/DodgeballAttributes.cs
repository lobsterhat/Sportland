using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Dodgeball-specific player ratings. Cross-sport ratings (luck, etc.)
    /// live on GeneralAttributes.
    /// </summary>
    public class DodgeballAttributes : MonoBehaviour
    {
        [Tooltip("0..100. Higher = faster, harder-to-handle throws (raises catch difficulty for the target).")]
        [Range(0f, 100f)] public float throwing = 60f;

        [Tooltip("0..100. Higher = better odds of completing a catch.")]
        [Range(0f, 100f)] public float catching = 60f;

        public float Throwing01 => Mathf.Clamp01(throwing / 100f);
        public float Catching01 => Mathf.Clamp01(catching / 100f);
    }
}
