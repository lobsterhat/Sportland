using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Mutable per-(player, ability) state for a Special Ability. One per
    /// ability per player, owned by PlayerAbilities. The shared SpecialAbility
    /// asset reads and drives this through the methods below so it can stay
    /// stateless.
    /// </summary>
    public class AbilityRuntime
    {
        public readonly SpecialAbility ability;
        public bool active;
        public int stacks;
        public float activeUntil = -1f;            // latched-duration expiry; <0 = no auto-expiry
        public float lastHandledDamageTime = -1f;  // last damage pulse this runtime reacted to

        public AbilityRuntime(SpecialAbility ability) { this.ability = ability; }

        /// <summary>
        /// Turn the ability on. duration &gt; 0 latches an auto-expiry window
        /// (refreshed each call). Ensures at least one stack so on/off abilities
        /// that never call AddStack still apply their modifiers.
        /// </summary>
        public void Activate(float duration)
        {
            active = true;
            if (stacks < 1) stacks = 1;
            activeUntil = duration > 0f ? Time.time + duration : -1f;
        }

        /// <summary>Add one stack, capped at the ability's maxStacks.</summary>
        public void AddStack()
        {
            stacks = Mathf.Min(stacks + 1, Mathf.Max(1, ability.maxStacks));
        }

        /// <summary>Turn the ability off and clear stacks.</summary>
        public void Deactivate()
        {
            active = false;
            stacks = 0;
            activeUntil = -1f;
        }
    }
}
