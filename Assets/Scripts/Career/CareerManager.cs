using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Sportland.Career
{
    /// <summary>
    /// The career layer's persistent heart: the club, the calendar, the daily
    /// action budget (design/hub_actions.md), the league enrollment, and the
    /// lineup. State auto-saves to a JSON file on every change and reloads on
    /// startup, so a career survives closing the game.
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

        private static string SavePath => Path.Combine(Application.persistentDataPath, "sportland_career.json");

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            rng = new System.Random(generationSeed ^ Environment.TickCount);
            if (!LoadCareer())
                StartNewCareer();
        }

        /// <summary>Notify listeners and persist — every mutation funnels through here.</summary>
        private void Commit()
        {
            StateChanged?.Invoke();
            SaveCareer();
        }

        // ── Save / load ─────────────────────────────────────────────────

        [Serializable]
        private class CareerSaveData
        {
            public int day;
            public long currentDateTicks;
            public int actionsPerDay;
            public int actionsRemaining;
            public Club club;
            public List<CareerAthlete> freeAgents;
            public bool inLeague;
            public LeagueMembership league;
        }

        private void SaveCareer()
        {
            var data = new CareerSaveData
            {
                day = day,
                currentDateTicks = currentDate.Ticks,
                actionsPerDay = actionsPerDay,
                actionsRemaining = actionsRemaining,
                club = club,
                freeAgents = freeAgents,
                inLeague = league != null,
                league = league,
            };
            try
            {
                File.WriteAllText(SavePath, JsonUtility.ToJson(data));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Career save failed: {e.Message}");
            }
        }

        private bool LoadCareer()
        {
            try
            {
                if (!File.Exists(SavePath)) return false;
                var data = JsonUtility.FromJson<CareerSaveData>(File.ReadAllText(SavePath));
                if (data == null || data.club == null || data.club.pool.Count == 0) return false;

                day = data.day;
                currentDate = new DateTime(data.currentDateTicks);
                actionsPerDay = data.actionsPerDay;
                actionsRemaining = data.actionsRemaining;
                club = data.club;
                freeAgents = data.freeAgents ?? new List<CareerAthlete>();
                league = data.inLeague ? data.league : null;

                // Migration: saves from before rival rosters existed.
                if (league != null && (league.rivals == null || league.rivals.Count == 0))
                    PopulateRivalRosters();

                Debug.Log($"Career loaded: {club.clubName}, day {day}.");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Career load failed, starting fresh: {e.Message}");
                return false;
            }
        }

        /// <summary>Delete the save and start over (debug/testing affordance — F9 in the hub).</summary>
        public void ResetCareer()
        {
            try { if (File.Exists(SavePath)) File.Delete(SavePath); }
            catch (Exception e) { Debug.LogWarning($"Save delete failed: {e.Message}"); }

            day = 1;
            currentDate = new DateTime(2026, 9, 1);
            actionsPerDay = 3;
            StartNewCareer();
        }

        // ── Career setup ────────────────────────────────────────────────

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
            Commit();
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
            Commit();
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
                fixture.Date = gameDate;
                fixture.slot = slotBag[rng.Next(slotBag.Length)];
                league.fixtures.Add(fixture);
                gameDate = gameDate.AddDays(3);
            }

            PopulateRivalRosters();
            Commit();
        }

        /// <summary>
        /// Give every rival club a real roster: a captain (their
        /// manager-player, archetype and all) plus a squad of division-level
        /// journeymen. Id ranges are offset per club so poached players can
        /// join the pool without colliding with existing ids.
        /// </summary>
        private void PopulateRivalRosters()
        {
            league.rivals.Clear();
            for (int c = 0; c < OpponentNames.Length; c++)
            {
                var rival = new RivalClub { clubName = OpponentNames[c] };
                int idBase = 100 + c * 20;

                rival.roster.Add(AthleteGenerator.GenerateRivalCaptain(rng, idBase));
                int squadSize = 9 + rng.Next(3); // 10-12 including the captain
                for (int i = 1; i < squadSize; i++)
                    rival.roster.Add(AthleteGenerator.Generate(rng, idBase + i, 0f, 0.35f));

                league.rivals.Add(rival);
            }
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
            Commit();
            return true;
        }

        // ── Lineup ──────────────────────────────────────────────────────

        public CareerAthlete AthleteById(string id) => club.pool.Find(a => a.id == id);

        /// <summary>
        /// Put an athlete in a starter slot (0-2 infield, 3-5 outfield) or a
        /// reserve slot. They're pulled out of any slot they already hold, so
        /// assignment doubles as a move. Empty id clears the slot.
        /// </summary>
        public void AssignLineupSlot(bool starter, int slot, string athleteId)
        {
            if (league == null) return;

            if (!string.IsNullOrEmpty(athleteId))
                league.RemoveFromLineup(athleteId);

            if (starter) league.starterIds[slot] = athleteId ?? "";
            else league.reserveIds[slot] = athleteId ?? "";
            Commit();
        }

        /// <summary>
        /// Fill every open slot with the best unassigned athlete by a rough
        /// overall (average of the general ratings). Starters first, then
        /// reserves — Skip's F-grades put him last in line, as he'd insist.
        /// </summary>
        public void AutoFillLineup()
        {
            if (league == null) return;

            var candidates = new List<CareerAthlete>(club.pool);
            candidates.RemoveAll(a => league.LineupTagOf(a.id) != null);
            candidates.Sort((a, b) => Overall(b).CompareTo(Overall(a)));

            int next = 0;
            for (int i = 0; i < league.starterIds.Length && next < candidates.Count; i++)
                if (league.starterIds[i].Length == 0) league.starterIds[i] = candidates[next++].id;
            for (int i = 0; i < league.reserveIds.Length && next < candidates.Count; i++)
                if (league.reserveIds[i].Length == 0) league.reserveIds[i] = candidates[next++].id;

            Commit();
        }

        private static float Overall(CareerAthlete a)
        {
            float sum = 0f;
            foreach (var r in a.generalRatings) sum += r.value;
            return sum / a.generalRatings.Length;
        }

        // ── Day loop ────────────────────────────────────────────────────

        /// <summary>Spend one action. False (and no change) when the day is spent.</summary>
        public bool TrySpendAction()
        {
            if (actionsRemaining <= 0) return false;
            actionsRemaining--;
            Commit();
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
            Commit();
        }

        // ── Building actions ────────────────────────────────────────────

        /// <summary>Cafe one-on-one: reveal a random hidden trait on a random athlete.</summary>
        public string RevealSomethingOverDinner()
        {
            int count = club.pool.Count;
            int start = rng.Next(count);
            for (int i = 0; i < count; i++)
            {
                var athlete = club.pool[(start + i) % count];
                string learned = athlete.RevealRandomHiddenTrait(rng);
                if (learned != null)
                {
                    Commit();
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
            Commit();
        }

        /// <summary>Hospital: treat the most tired athlete. Returns who was treated.</summary>
        public CareerAthlete TreatMostTired(float relief = 50f)
        {
            CareerAthlete worst = null;
            foreach (var a in club.pool)
                if (worst == null || a.fatigue > worst.fatigue) worst = a;

            if (worst != null)
                worst.fatigue = Mathf.Max(0f, worst.fatigue - relief);
            Commit();
            return worst;
        }
    }
}
