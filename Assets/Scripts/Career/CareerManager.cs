using System;
using System.Collections.Generic;
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

        [Header("League")]
        [Tooltip("The club's league enrollment. Null until the player signs up at the Office. One sport for now; becomes a list when the calendar wheel turns.")]
        public LeagueMembership league;

        [Tooltip("The signable free-agent pool. Bottom-division quality — low-skill journeymen, per the churn ecology.")]
        public List<CareerAthlete> freeAgents = new List<CareerAthlete>();

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

        /// <summary>
        /// Bootstraps a fresh career: the club is just you and Skip — the
        /// squad gets recruited from the free-agent pool after joining a
        /// league (design/hub_world.md §3, "build a team").
        /// </summary>
        public void StartNewCareer()
        {
            club = new Club { clubName = "Sportland FC" };
            club.pool.Add(AthleteGenerator.GeneratePlayerCharacter());
            club.pool.Add(AthleteGenerator.GenerateSkip());

            // Bottom-division free agents: a low talent band, always signable.
            freeAgents.Clear();
            for (int i = 0; i < 20; i++)
                freeAgents.Add(AthleteGenerator.Generate(rng, i, 0f, 0.35f));

            league = null;
            actionsRemaining = actionsPerDay;
            StateChanged?.Invoke();
        }

        // ── League ──────────────────────────────────────────────────────

        private static readonly string[] OpponentNames =
        {
            "Harbor Hawks", "Dockside Eels", "Northgate Owls", "Cannery Cats",
            "Redline Foxes", "Old Mill Bears", "Parkside Ravens",
        };

        /// <summary>
        /// Sign up for the (only, for now) league: Dodgeball, bottom division.
        /// Generates the season's fixtures — double round-robin against the
        /// division's seven AI clubs, one game every three days, each with a
        /// time slot.
        /// </summary>
        public void JoinDodgeballLeague()
        {
            league = new LeagueMembership
            {
                sport = "Dodgeball",
                leagueName = "Parks League",
                divisionName = "Division 4",
                rules = new RosterRules(), // dodgeball defaults: 6 court + 4 reserve + 2 inactive
            };

            // Double round-robin: every opponent twice, once home once away.
            var slate = new List<Fixture>();
            foreach (var name in OpponentNames)
            {
                slate.Add(new Fixture { opponent = name, home = true });
                slate.Add(new Fixture { opponent = name, home = false });
            }
            for (int i = slate.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (slate[i], slate[j]) = (slate[j], slate[i]);
            }

            // Slot weighting: league dodgeball is mostly an evening affair.
            TimeSlot[] slotBag =
            {
                TimeSlot.Morning,
                TimeSlot.Afternoon, TimeSlot.Afternoon,
                TimeSlot.Evening, TimeSlot.Evening, TimeSlot.Evening,
                TimeSlot.Night, TimeSlot.Night,
            };

            DateTime gameDate = currentDate.AddDays(7); // one week of preseason
            foreach (var fixture in slate)
            {
                fixture.date = gameDate;
                fixture.slot = slotBag[rng.Next(slotBag.Length)];
                league.fixtures.Add(fixture);
                gameDate = gameDate.AddDays(3);
            }

            StateChanged?.Invoke();
        }

        /// <summary>
        /// Fixtures that clash with the given one: same date and time slot in
        /// another enrollment, or a shared athlete booked elsewhere. With one
        /// league and one club this is always empty — the seam exists so the
        /// calendar can flag conflicts the moment a second sport arrives.
        /// </summary>
        public List<Fixture> ConflictsWith(Fixture fixture)
        {
            return new List<Fixture>();
        }

        // ── Recruiting ──────────────────────────────────────────────────

        /// <summary>
        /// Sign a free agent. Slice rule: everyone always accepts — the
        /// willingness/interview loop (design/hub_actions.md) comes later.
        /// Only the league's max-roster cap can refuse.
        /// </summary>
        public bool TryRecruit(CareerAthlete athlete, out string message)
        {
            int cap = league != null ? league.rules.MaxRoster : 12;
            if (club.pool.Count >= cap)
            {
                message = $"Roster full ({cap} max, including inactive spots).";
                return false;
            }
            if (!freeAgents.Remove(athlete))
            {
                message = "They're no longer available.";
                return false;
            }

            club.pool.Add(athlete);
            message = $"{athlete.FullName} signs with {club.clubName}!";
            StateChanged?.Invoke();
            return true;
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
