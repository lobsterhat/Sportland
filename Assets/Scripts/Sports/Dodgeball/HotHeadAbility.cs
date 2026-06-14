using System.Collections.Generic;
using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Hot Head: taking a hit makes the player reckless for a few seconds —
    /// aggressive stats up, skill stats down. Repeated hits inside the window
    /// stack the effect (up to maxStacks) and refresh the timer. A "both"
    /// ability: its modifier list mixes a boost (throwSpeed) with penalties
    /// (throwAccuracy, catching).
    ///
    /// Vertical slice for the ability engine — exercises the event-latch
    /// (damage pulse), the duration window, stacking, mixed modifiers, and the
    /// StackedMultiplier seam in one ability.
    /// </summary>
    [CreateAssetMenu(menuName = "Sportland/Dodgeball/Abilities/Hot Head", fileName = "HotHead")]
    public class HotHeadAbility : SpecialAbility
    {
        [Header("Hot Head")]
        [Tooltip("Seconds the effect lasts after the most recent hit. Each new hit refreshes this window.")]
        [SerializeField] private float activeSeconds = 5f;

        public override void Tick(PlayerAbilities owner, AbilityRuntime rt)
        {
            // Fresh hit since we last reacted → (re)trigger.
            if (owner.LastDamageTime > rt.lastHandledDamageTime)
            {
                rt.lastHandledDamageTime = owner.LastDamageTime;
                if (rt.active) rt.AddStack();   // already raging → escalate (capped)
                rt.Activate(activeSeconds);      // (re)latch the window; first hit → 1 stack
            }

            // Latched window elapsed → cool off.
            if (rt.active && rt.activeUntil >= 0f && Time.time >= rt.activeUntil)
                rt.Deactivate();
        }

        // Seed Hot Head's signature values on a freshly created asset (the
        // editor calls Reset on creation). Tune in the Inspector afterward.
        private void Reset()
        {
            id = "hot_head";
            displayName = "Hot Head";
            description = "After taking a hit, aggression spikes and finesse drops " +
                          "for a few seconds. Repeated hits stack the frenzy.";
            activeSeconds = 5f;
            maxStacks = 3;
            modifiers = new List<StatModifier>
            {
                new StatModifier { stat = AbilityStat.ThrowSpeed,    multiplier = 1.25f },
                new StatModifier { stat = AbilityStat.ThrowAccuracy, multiplier = 0.85f },
                new StatModifier { stat = AbilityStat.Catching,      multiplier = 0.80f },
            };
        }
    }
}
