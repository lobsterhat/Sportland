using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Per-player Special Ability aggregator. Holds the player's active
    /// abilities and reports the combined multiplier for a given stat, which
    /// the *Attributes Effective*01 getters fold into the base rating.
    ///
    /// Composition is multiplicative: MultiplierFor(stat) is the product of
    /// every currently-active ability's multiplier on that stat (an ability
    /// that doesn't touch the stat contributes 1). Per-ability stacking is
    /// baseMult^stacks, kept inside SpecialAbility so explicit per-level
    /// tables can replace that formula later without touching this class.
    ///
    /// Skeleton stage: the ability list and per-frame rule evaluation
    /// (Activate / Deactivate / Increase, durations, stacks) arrive with the
    /// SpecialAbility ScriptableObject and the rules engine. Until then — and
    /// whenever a player simply has no abilities — MultiplierFor returns 1 for
    /// every stat, so wiring this into Effective() changes no behavior.
    /// </summary>
    public class PlayerAbilities : MonoBehaviour
    {
        /// <summary>
        /// Combined multiplier for <paramref name="stat"/> across all active
        /// abilities (1 = no net effect). Consumed by
        /// DodgeballAttributes.Effective() to produce the effective rating.
        /// </summary>
        public float MultiplierFor(AbilityStat stat)
        {
            // No abilities authored yet → identity. When the engine lands this
            // becomes:
            //     float m = 1f;
            //     for (int i = 0; i < active.Count; i++)
            //         m *= active[i].StackedMultiplier(stat);
            //     return m;
            return 1f;
        }
    }
}
