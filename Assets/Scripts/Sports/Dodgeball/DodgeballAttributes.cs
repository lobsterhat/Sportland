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

        [Tooltip("0..100. Leads a moving target: 0 aims where the target is now, " +
                 "100 aims where it will be when the ball arrives.")]
        [Range(0f, 100f)] public float anticipation = 60f;

        [Tooltip("0..100. Higher = better odds of completing a catch.")]
        [Range(0f, 100f)] public float catching = 60f;

        public float ThrowSpeed01 => Mathf.Clamp01(throwSpeed / 100f);
        public float ThrowAccuracy01 => Mathf.Clamp01(throwAccuracy / 100f);
        public float Anticipation01 => Mathf.Clamp01(anticipation / 100f);
        public float Catching01 => Mathf.Clamp01(catching / 100f);

        /// <summary>
        /// Composite "how well will this player convert a possession into
        /// points?" score, 0..1. Used by the AI to route the ball to the best
        /// shooter. Accuracy gets the heaviest weight (a wild throw doesn't
        /// score regardless of speed), then speed (faster ball is harder to
        /// catch), then anticipation (better lead on a moving target).
        /// </summary>
        public float ScorePotential01 => 0.55f * ThrowAccuracy01
                                       + 0.35f * ThrowSpeed01
                                       + 0.10f * Anticipation01;
    }
}
