using System.Collections.Generic;
using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Abstract base for a Special Ability. The ASSET holds the stateless
    /// definition — identity, the per-stat multipliers, the stack cap — and the
    /// trigger LOGIC (overridden Tick). One asset is shared by every player who
    /// has the ability, so it must hold NO per-player state: that lives on the
    /// AbilityRuntime the owning PlayerAbilities passes into Tick.
    ///
    /// Composition is multiplicative. A stack multiplies the modifier in again
    /// (baseMult^stacks), confined to StackedMultiplier so an explicit per-level
    /// table can replace the formula later with no call-site changes.
    /// </summary>
    public abstract class SpecialAbility : ScriptableObject
    {
        /// <summary>One stat's multiplier at a single stack. >1 boosts, &lt;1 penalizes.</summary>
        [System.Serializable]
        public struct StatModifier
        {
            public AbilityStat stat;
            [Tooltip("Multiplier at ONE stack. >1 boosts, <1 penalizes. Stacks raise it to the power of the stack count.")]
            public float multiplier;
        }

        [Header("Identity")]
        public string id;
        public string displayName;
        [TextArea] public string description;

        [Header("Effect")]
        [Tooltip("Per-stat multipliers applied while active. May mix boosts (>1) and penalties (<1) — that's how a 'both' ability like Hot Head works.")]
        [SerializeField] protected List<StatModifier> modifiers = new List<StatModifier>();
        [Tooltip("Max stack count. Each Increase adds a stack; the modifier is raised to the power of the current stacks.")]
        [Min(1)] public int maxStacks = 1;

        /// <summary>Read-only view of the per-stat modifiers (for HUD / debug).</summary>
        public IReadOnlyList<StatModifier> Modifiers => modifiers;

        /// <summary>
        /// This ability's multiplier for <paramref name="stat"/> at the given
        /// stack count. baseMult^stacks; 1 if the ability doesn't touch the
        /// stat. Single seam — swap to an explicit per-level table here later.
        /// </summary>
        public float StackedMultiplier(AbilityStat stat, int stacks)
        {
            for (int i = 0; i < modifiers.Count; i++)
            {
                if (modifiers[i].stat != stat) continue;
                float m = modifiers[i].multiplier;
                return stacks <= 1 ? m : Mathf.Pow(m, stacks);
            }
            return 1f;
        }

        /// <summary>
        /// Per-frame rule evaluation for ONE player. Read signals off
        /// <paramref name="owner"/> (e.g. LastDamageTime) and drive activation
        /// through <paramref name="rt"/> (Activate / AddStack / Deactivate).
        /// Must not mutate the asset — all per-player state is on rt.
        /// </summary>
        public abstract void Tick(PlayerAbilities owner, AbilityRuntime rt);
    }
}
