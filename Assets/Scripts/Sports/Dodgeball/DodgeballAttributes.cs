using UnityEngine;
using Sportland.Core;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Dodgeball-specific player ratings. Cross-sport ratings (luck, etc.)
    /// live on GeneralAttributes.
    /// </summary>
    public class DodgeballAttributes : MonoBehaviour
    {
        [Tooltip("Throwing Speed — RATING 0-20 (hidden; shown to players as an F-S grade). " +
                 "How fast the ball leaves the hand; maps to the release speed (u/s). " +
                 "The other ratings below are still 0-100 until they're converted too.")]
        [Range(0f, 20f)] public float throwSpeedRating = 12f;

        [Tooltip("Throwing Accuracy — RATING 0-20 (hidden; shown to players as an F-S grade). " +
                 "How close the ball lands to its target: higher = a tighter aim-scatter envelope, " +
                 "so the throw is on target more often AND closer when it does miss. " +
                 "The other ratings below are still 0-100 until they're converted too.")]
        [Range(0f, 20f)] public float throwAccuracyRating = 12f;

        [Tooltip("0..100. Leads a moving target: 0 aims where the target is now, " +
                 "100 aims where it will be when the ball arrives.")]
        [Range(0f, 100f)] public float anticipation = 60f;

        [Tooltip("Catch Technique — RATING 0-20 (hidden; shown to players as an F-S grade). " +
                 "How well you receive the ball; sizes the catch timing window (see design/defense.md). " +
                 "anticipation above is the last stat still on the old 0-100 scale.")]
        [Range(0f, 20f)] public float catchTechniqueRating = 12f;

        public float ThrowSpeed01 => Rating.To01(throwSpeedRating);   // 0-20 rating scale
        /// <summary>Player-facing F-S letter grade for Throwing Speed.</summary>
        public string ThrowSpeedGrade => Rating.Grade(throwSpeedRating);
        public float ThrowAccuracy01 => Rating.To01(throwAccuracyRating);   // 0-20 rating scale
        /// <summary>Player-facing F-S letter grade for Throwing Accuracy.</summary>
        public string ThrowAccuracyGrade => Rating.Grade(throwAccuracyRating);
        public float Anticipation01 => Mathf.Clamp01(anticipation / 100f);
        public float Catching01 => Rating.To01(catchTechniqueRating);   // 0-20 rating scale
        /// <summary>Player-facing F-S letter grade for Catch Technique.</summary>
        public string CatchTechniqueGrade => Rating.Grade(catchTechniqueRating);

        // ── Effective ratings (base × active Special Ability multipliers) ──
        // Gameplay reads these, never the raw *01 getters above, so a Special
        // Ability that modifies a stat is felt everywhere with no call-site
        // changes. Today Effective() is an identity passthrough — no abilities
        // are authored — so each Effective*01 equals its base. When the ability
        // system lands, Effective() consults the player's ability aggregator
        // for the multiplier on `stat` and returns Clamp01(base01 × mult).
        public float EffectiveThrowSpeed01    => Effective(ThrowSpeed01,    AbilityStat.ThrowSpeed);
        public float EffectiveThrowAccuracy01 => Effective(ThrowAccuracy01, AbilityStat.ThrowAccuracy);
        public float EffectiveAnticipation01  => Effective(Anticipation01,  AbilityStat.Anticipation);
        public float EffectiveCatching01      => Effective(Catching01,      AbilityStat.Catching);

        // Effective-stat hook. Folds the player's active-ability multiplier AND
        // their stamina/fatigue multiplier for `stat` into the base rating. Each
        // returns 1 when absent, so this is the plain base when neither sibling
        // exists. Both lookups are cached once (the components are added at
        // spawn when present).
        private PlayerAbilities abilities;
        private PlayerStamina stamina;
        private bool modifiersResolved;

        private float Effective(float base01, AbilityStat stat)
        {
            if (!modifiersResolved)
            {
                abilities = GetComponent<PlayerAbilities>();
                stamina = GetComponent<PlayerStamina>();
                modifiersResolved = true;
            }
            float mult = (abilities != null ? abilities.MultiplierFor(stat) : 1f)
                       * (stamina != null ? stamina.MultiplierFor(stat) : 1f);
            return Mathf.Clamp01(base01 * mult);
        }

        /// <summary>
        /// Composite "how well will this player convert a possession into
        /// points?" score, 0..1. Used by the AI to route the ball to the best
        /// shooter. Accuracy gets the heaviest weight (a wild throw doesn't
        /// score regardless of speed), then speed (faster ball is harder to
        /// catch), then anticipation (better lead on a moving target).
        /// </summary>
        public float ScorePotential01 => 0.55f * EffectiveThrowAccuracy01
                                       + 0.35f * EffectiveThrowSpeed01
                                       + 0.10f * EffectiveAnticipation01;
    }
}
