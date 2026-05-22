using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Dodgeball-specific player ratings. Cross-sport ratings (luck, etc.)
    /// live on GeneralAttributes.
    /// </summary>
    public class DodgeballAttributes : MonoBehaviour
    {
        [Tooltip("0..100. How fast the ball leaves the hand (release speed). " +
                 "Only affects catches indirectly — a faster ball is harder to catch.")]
        [Range(0f, 100f)] public float throwSpeed = 60f;

        [Tooltip("0..100. How close the ball lands to its intended target. " +
                 "Low = wild throws that may miss or hit the wrong player.")]
        [Range(0f, 100f)] public float throwAccuracy = 60f;

        [Tooltip("0..100. Higher = better odds of completing a catch.")]
        [Range(0f, 100f)] public float catching = 60f;

        public float ThrowSpeed01 => Mathf.Clamp01(throwSpeed / 100f);
        public float ThrowAccuracy01 => Mathf.Clamp01(throwAccuracy / 100f);
        public float Catching01 => Mathf.Clamp01(catching / 100f);
    }
}
