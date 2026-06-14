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

        // Special Ability hook. Folds the player's active-ability multiplier
        // for `stat` into the base rating. With no PlayerAbilities sibling, or
        // no abilities authored on it, MultiplierFor returns 1 → the base is
        // returned unchanged, so this is behavior-neutral until abilities
        // exist. The sibling lookup is cached once (PlayerAbilities lives on
        // the prefab from Awake when present).
        private PlayerAbilities abilities;
        private bool abilitiesResolved;

        private float Effective(float base01, AbilityStat stat)
        {
            if (!abilitiesResolved) { abilities = GetComponent<PlayerAbilities>(); abilitiesResolved = true; }
            float mult = abilities != null ? abilities.MultiplierFor(stat) : 1f;
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
