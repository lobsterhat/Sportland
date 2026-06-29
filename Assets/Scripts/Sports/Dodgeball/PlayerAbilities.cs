using System.Collections.Generic;
using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Per-player Special Ability aggregator + runtime host. Holds the player's
    /// abilities (on = present in the list), ticks each one's trigger logic
    /// every frame, and reports the combined multiplier for a stat — which the
    /// *Attributes Effective*01 getters fold into the base rating.
    ///
    /// Composition is multiplicative: MultiplierFor(stat) is the product of
    /// every active ability's StackedMultiplier on that stat. With no abilities
    /// assigned the component is inert and returns 1 for every stat, so adding
    /// it to a player changes nothing until an ability is dropped in.
    /// </summary>
    public class PlayerAbilities : MonoBehaviour
    {
        [Tooltip("Special Abilities this player has. Each is a SpecialAbility asset (e.g. Hot Head); its trigger logic runs every frame while present.")]
        [SerializeField] private List<SpecialAbility> abilities = new List<SpecialAbility>();

        private readonly List<AbilityRuntime> runtimes = new List<AbilityRuntime>();
        private PlayerZoneTracker tracker;
        private Ball ball;
        private bool subscribed;

        // Damage signal: stamped when this player is the victim of a hit. The
        // timestamp lets event-latched abilities (Hot Head) spot a fresh hit by
        // comparing it against their own lastHandledDamageTime; the speed is
        // available for magnitude-scaled reactions later.
        public float LastDamageTime { get; private set; } = -1f;
        public float LastDamageSpeed { get; private set; }

        public PlayerZoneTracker Tracker => tracker;

        /// <summary>Read-only view of the per-ability runtime state (for HUD / debug).</summary>
        public IReadOnlyList<AbilityRuntime> Runtimes => runtimes;

        /// <summary>
        /// Count of this player's still-in-play teammates, including self.
        /// PlayerZoneTracker.All holds only active players (TakeOut disables an
        /// eliminated one, dropping it from the registry), so this is the live
        /// "remaining on my team" count. <paramref name="infieldersOnly"/>
        /// matches the elimination wipeout rule, which counts infielders.
        /// </summary>
        public int ActiveTeammates(bool infieldersOnly)
        {
            if (tracker == null) return 0;
            var team = tracker.Spawn.team;
            var all = PlayerZoneTracker.All;
            int n = 0;
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (t == null || t.Spawn.team != team) continue;
                if (infieldersOnly && t.Spawn.role != PlayerRole.Infielder) continue;
                n++;
            }
            return n;
        }

        private void Awake()
        {
            tracker = GetComponent<PlayerZoneTracker>();
            for (int i = 0; i < abilities.Count; i++)
            {
                if (abilities[i] == null) continue;
                runtimes.Add(new AbilityRuntime(abilities[i]));
            }
        }

        private void OnDestroy()
        {
            if (ball != null && subscribed) ball.OnHit -= OnBallHit;
        }

        private void Update()
        {
            if (runtimes.Count == 0) return;   // inert player: no abilities assigned
            EnsureSubscribed();
            for (int i = 0; i < runtimes.Count; i++)
                runtimes[i].ability.Tick(this, runtimes[i]);
        }

        // The ball may not exist at Awake, so subscription is retried from
        // Update until it takes (same pattern as DodgeballPlayerInput).
        private void EnsureSubscribed()
        {
            if (subscribed) return;
            if (ball == null) ball = FindAnyObjectByType<Ball>();
            if (ball == null) return;
            ball.OnHit += OnBallHit;
            subscribed = true;
        }

        private void OnBallHit(PlayerZoneTracker victim, float ballSpeed)
        {
            if (victim != tracker) return;
            LastDamageTime = Time.time;
            LastDamageSpeed = ballSpeed;
        }

        /// <summary>
        /// Combined multiplier for <paramref name="stat"/> across all active
        /// abilities (1 = no net effect). Consumed by
        /// DodgeballAttributes.Effective().
        /// </summary>
        public float MultiplierFor(AbilityStat stat)
        {
            float m = 1f;
            for (int i = 0; i < runtimes.Count; i++)
            {
                var rt = runtimes[i];
                if (rt.active) m *= rt.ability.StackedMultiplier(stat, rt.stacks);
            }
            return m;
        }

        /// <summary>
        /// Runtime ability assignment (used by CourtSetup's roster wiring).
        /// Replaces the current abilities and rebuilds the runtimes. Needed
        /// because AddComponent runs Awake before the caller can populate the
        /// serialized list, so the list is set through here instead.
        /// </summary>
        public void SetAbilities(IList<SpecialAbility> list)
        {
            abilities.Clear();
            runtimes.Clear();
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null) continue;
                abilities.Add(list[i]);
                runtimes.Add(new AbilityRuntime(list[i]));
            }
        }

        /// <summary>Clear every ability's active/stack state — call on a match reset.</summary>
        public void ResetRuntimes()
        {
            for (int i = 0; i < runtimes.Count; i++) runtimes[i].Deactivate();
        }
    }
}
