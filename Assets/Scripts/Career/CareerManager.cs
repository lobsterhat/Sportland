using System;
using UnityEngine;

namespace Sportland.Career
{
    /// <summary>
    /// The career layer's persistent heart: the club, the calendar, and the
    /// daily action budget (design/hub_actions.md). Survives scene loads.
    ///
    /// Slice scope: one club, day/action loop, overnight tick. Leagues,
    /// training results, and conflicts consume this later.
    /// </summary>
    public class CareerManager : MonoBehaviour
    {
        public static CareerManager Instance { get; private set; }

        [Header("Calendar")]
        public int day = 1;
        public DateTime currentDate = new DateTime(2026, 9, 1);

        [Header("Actions (design/hub_actions.md §3)")]
        [Tooltip("Base daily action budget. Archetype modifiers arrive with the creator.")]
        public int actionsPerDay = 3;
        public int actionsRemaining = 3;

        [Header("Club")]
        public Club club;

        [Tooltip("Seed for the starting pool, so a career is reproducible while there's no save system.")]
        public int generationSeed = 20260901;

        /// <summary>Raised whenever day/actions/club state changes; HUD listens.</summary>
        public event Action StateChanged;

        private System.Random rng;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            rng = new System.Random(generationSeed);
            if (club == null || club.pool.Count == 0)
                StartNewCareer();
        }

        /// <summary>Bootstraps a fresh club: you, Skip, and a starting pool.</summary>
        public void StartNewCareer()
        {
            club = new Club { clubName = "Sportland FC" };
            club.pool.Add(AthleteGenerator.GeneratePlayerCharacter());
            club.pool.Add(AthleteGenerator.GenerateSkip());
            for (int i = 0; i < 8; i++)
                club.pool.Add(AthleteGenerator.Generate(rng, i));

            actionsRemaining = actionsPerDay;
            StateChanged?.Invoke();
        }

        /// <summary>Has the player been through character creation yet?</summary>
        public bool PlayerCreated
        {
            get
            {
                var pc = club?.PlayerCharacter;
                return pc != null && !string.IsNullOrEmpty(pc.archetypeId);
            }
        }

        /// <summary>
        /// Character creation: lock in an archetype. Applies the rating
        /// template and the archetype's daily action budget, and refreshes
        /// today's remaining actions to the new budget.
        /// </summary>
        public void ApplyArchetype(ArchetypeDefinition archetype)
        {
            var pc = club.PlayerCharacter;
            pc.archetypeId = archetype.id;
            for (int i = 0; i < pc.generalRatings.Length; i++)
                pc.generalRatings[i] = new TraitEntry(archetype.generalRatingValue, revealed: true);

            actionsPerDay = archetype.actionsPerDay;
            actionsRemaining = actionsPerDay;
            StateChanged?.Invoke();
        }

        /// <summary>Spend one action. False (and no change) when the day is spent.</summary>
        public bool TrySpendAction()
        {
            if (actionsRemaining <= 0) return false;
            actionsRemaining--;
            StateChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// End the day: overnight tick (design/hub_actions.md §2 step 4) —
        /// fatigue recovery, calendar advance, fresh budget.
        /// </summary>
        public void EndDay()
        {
            foreach (var a in club.pool)
                a.fatigue = Mathf.Max(0f, a.fatigue - 30f);

            day++;
            currentDate = currentDate.AddDays(1);
            actionsRemaining = actionsPerDay;
            StateChanged?.Invoke();
        }

        /// <summary>Cafe one-on-one: reveal a random hidden trait on a random athlete.</summary>
        public string RevealSomethingOverDinner()
        {
            // Shuffle-free random start point, then scan for someone with secrets left.
            int count = club.pool.Count;
            int start = rng.Next(count);
            for (int i = 0; i < count; i++)
            {
                var athlete = club.pool[(start + i) % count];
                string learned = athlete.RevealRandomHiddenTrait(rng);
                if (learned != null)
                {
                    StateChanged?.Invoke();
                    return learned;
                }
            }
            return null; // the whole pool is an open book
        }

        /// <summary>Practice: everyone works, everyone tires. Training gains arrive later.</summary>
        public void RunPractice(float fatigueCost = 12f)
        {
            foreach (var a in club.pool)
                a.fatigue = Mathf.Min(100f, a.fatigue + fatigueCost);
            StateChanged?.Invoke();
        }

        /// <summary>Hospital: treat the most tired athlete. Returns who was treated.</summary>
        public CareerAthlete TreatMostTired(float relief = 50f)
        {
            CareerAthlete worst = null;
            foreach (var a in club.pool)
                if (worst == null || a.fatigue > worst.fatigue) worst = a;

            if (worst != null)
                worst.fatigue = Mathf.Max(0f, worst.fatigue - relief);
            StateChanged?.Invoke();
            return worst;
        }
    }
}
