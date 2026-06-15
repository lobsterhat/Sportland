using System.Collections.Generic;
using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Sole Survivor: while this player is the last one left on their team,
    /// every stat gets a big lift. A pure-positive ability (all modifiers >1),
    /// and the continuous-predicate counterpart to Hot Head's event latch —
    /// it's active exactly while the predicate holds and switches off the
    /// instant a teammate returns (e.g. a Mode 4 catch-revive).
    ///
    /// No stacking (maxStacks = 1): it's on or off, no duration. Exercises the
    /// Activate(0)/Deactivate predicate path of the engine.
    /// </summary>
    [CreateAssetMenu(menuName = "Sportland/Dodgeball/Abilities/Sole Survivor", fileName = "SoleSurvivor")]
    public class SoleSurvivorAbility : SpecialAbility
    {
        [Header("Sole Survivor")]
        [Tooltip("Count only infielders as 'teammates remaining' (matches the elimination wipeout rule, which ends the game when a team's infielders are gone). Off counts all roles, so outfielders keep the ability from triggering until they're out too.")]
        [SerializeField] private bool infieldersOnly = true;

        public override void Tick(PlayerAbilities owner, AbilityRuntime rt)
        {
            // Active exactly while I'm the only one of my team left in play.
            // PlayerZoneTracker.All excludes eliminated players, and an out
            // player's PlayerAbilities doesn't tick — so reaching here means
            // I'm in, and a count of 1 is me alone.
            bool lastStanding = owner.ActiveTeammates(infieldersOnly) <= 1;

            if (lastStanding && !rt.active) rt.Activate(0f);        // 0 → no auto-expiry, on while true
            else if (!lastStanding && rt.active) rt.Deactivate();
        }

        // Seed Sole Survivor's signature values on a freshly created asset.
        // Tune in the Inspector afterward.
        private void Reset()
        {
            id = "sole_survivor";
            displayName = "Sole Survivor";
            description = "Last one standing on the team — everything sharpens " +
                          "under the spotlight. A big across-the-board boost while alone.";
            infieldersOnly = true;
            maxStacks = 1;
            modifiers = new List<StatModifier>
            {
                new StatModifier { stat = AbilityStat.ThrowSpeed,    multiplier = 1.40f },
                new StatModifier { stat = AbilityStat.ThrowAccuracy, multiplier = 1.30f },
                new StatModifier { stat = AbilityStat.Anticipation,  multiplier = 1.30f },
                new StatModifier { stat = AbilityStat.Catching,      multiplier = 1.30f },
            };
        }
    }
}
