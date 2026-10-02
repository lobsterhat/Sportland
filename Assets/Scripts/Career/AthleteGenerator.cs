using System;

namespace Sportland.Career
{
    /// <summary>
    /// Procedural athlete generation for the career layer. Produces the club's
    /// starting pool plus the two fixed characters (the player and Skip).
    ///
    /// Generation follows the design tenets: general ratings cluster in the
    /// journeyman D-C band for a bottom-division pool, and higher-talent
    /// athletes tend to carry bigger egos (design/conflict_chemistry.md §3 —
    /// stacking stars has a hidden cost). All personality traits start hidden.
    /// </summary>
    public static class AthleteGenerator
    {
        private static readonly string[] FirstNames =
        {
            "Marcus", "Dana", "Theo", "Priya", "Jonah", "Kai", "Rosa", "Miles",
            "Imani", "Victor", "Sana", "Dmitri", "June", "Andre", "Bea", "Hollis",
        };

        private static readonly string[] LastNames =
        {
            "Johnson", "Okafor", "Reyes", "Tanaka", "Novak", "Whitfield", "Ma",
            "Costa", "Baptiste", "Lindqvist", "Ortiz", "Grady", "Ellison", "Vance",
        };

        /// <summary>Roll one athlete with talent anywhere on the 0..1 range.</summary>
        public static CareerAthlete Generate(System.Random rng, int index)
            => Generate(rng, index, 0f, 1f);

        /// <summary>
        /// Roll one athlete. Talent (bounded by min/max) skews both ratings
        /// and egos — a bottom-division free-agent pool passes a low band
        /// (design/athlete_development.md §2: churn is the lower divisions'
        /// weather).
        /// </summary>
        public static CareerAthlete Generate(System.Random rng, int index, float talentMin, float talentMax)
        {
            float talent = talentMin + (float)rng.NextDouble() * (talentMax - talentMin);

            var a = new CareerAthlete
            {
                id = $"athlete_{index:D3}",
                firstName = FirstNames[rng.Next(FirstNames.Length)],
                lastName = LastNames[rng.Next(LastNames.Length)],
                age = 18 + rng.Next(15),
                fatigue = 0f,
            };

            // General ratings: journeyman band (roughly E..B) lifted by talent.
            for (int i = 0; i < a.generalRatings.Length; i++)
                a.generalRatings[i] = new TraitEntry(RollRating(rng, 4f + talent * 8f, 3f), revealed: true);

            // Egos correlate loosely with talent; every trait starts hidden.
            for (int i = 0; i < a.expectations.Length; i++)
                a.expectations[i] = new TraitEntry(RollRating(rng, 3f + talent * 9f, 4f));

            for (int i = 0; i < a.dispositions.Length; i++)
                a.dispositions[i] = new TraitEntry(RollRating(rng, 10f, 5f));

            a.volatility = new TraitEntry(RollRating(rng, 8f, 5f));
            StampDodgeball(a, rng, talent);
            return a;
        }

        /// <summary>The player's character. Ego-immune; ratings modest for now (creator comes later).</summary>
        public static CareerAthlete GeneratePlayerCharacter()
        {
            var a = new CareerAthlete
            {
                id = "player",
                firstName = "You",
                lastName = "",
                age = 28,
                isPlayerCharacter = true,
            };
            for (int i = 0; i < a.generalRatings.Length; i++)
                a.generalRatings[i] = new TraitEntry(10f, revealed: true); // C-grade all-rounder pending the creator
            a.phase = CareerAthlete.PhaseForAge(a.age);
            a.abilities = AthleteAbility.None;
            a.SetUniformDodgeball(10f, 6f, 14f); // C now, D floor, B ceiling — the creator overwrites this
            return a;
        }

        /// <summary>Skip: the mentor. Very poor skills, zero ego, always happy to be here.</summary>
        public static CareerAthlete GenerateSkip()
        {
            var a = new CareerAthlete
            {
                id = "skip",
                firstName = "Skip",
                lastName = "",
                age = 52,
                isMentor = true,
            };
            for (int i = 0; i < a.generalRatings.Length; i++)
                a.generalRatings[i] = new TraitEntry(2f, revealed: true); // F across the board — and fieldable anyway
            a.phase = DevelopmentPhase.Decline;
            a.abilities = AthleteAbility.None;
            a.SetUniformDodgeball(2f, 0f, 4f); // F now, a sliver of E left in the ceiling
            return a;
        }

        /// <summary>
        /// A rival club's captain: the manager-player running that club,
        /// rolled from the same parts as everyone else plus a random
        /// archetype (design/rival_managers.md). Better than the division's
        /// journeymen — the best player on most bottom-division rosters.
        /// </summary>
        public static CareerAthlete GenerateRivalCaptain(System.Random rng, int index)
        {
            var captain = Generate(rng, index, 0.35f, 0.65f);
            captain.isCaptain = true;
            captain.age = 26 + rng.Next(12); // captains skew veteran
            captain.archetypeId = Archetypes.All[rng.Next(Archetypes.All.Length)].id;
            // Age moved after the first stamp, so the sheet has to match the veteran.
            StampDodgeball(captain, rng, 0.5f);
            return captain;
        }

        /// <summary>
        /// Dodgeball sheet for one athlete. Ceiling rises with talent. Current
        /// sits low in the band while growing, near the ceiling at the peak,
        /// and partway back down in decline. At most one ability flag.
        /// </summary>
        private static void StampDodgeball(CareerAthlete athlete, System.Random rng, float talent)
        {
            athlete.phase = CareerAthlete.PhaseForAge(athlete.age);
            athlete.dodgeball = new SkillRating[DodgeballSkills.Count];
            for (int i = 0; i < athlete.dodgeball.Length; i++)
                athlete.dodgeball[i] = RollSkill(rng, talent, athlete.phase);

            float hot = 0.08f + talent * 0.20f;
            float sole = 0.05f + talent * 0.12f;
            double roll = rng.NextDouble();
            if (roll < hot) athlete.abilities = AthleteAbility.HotHead;
            else if (roll < hot + sole) athlete.abilities = AthleteAbility.SoleSurvivor;
            else athlete.abilities = AthleteAbility.None;
        }

        private static SkillRating RollSkill(System.Random rng, float talent, DevelopmentPhase phase)
        {
            float ceiling = RollRating(rng, 6f + talent * 10f, 2.5f);
            float floor = RollRating(rng, 1f + talent * 4f, 1.5f);
            if (floor > ceiling - 1f) floor = ceiling - 1f;
            if (floor < 0f) floor = 0f;

            float along = phase == DevelopmentPhase.Growth ? 0.35f
                : phase == DevelopmentPhase.Peak ? 0.90f
                : 0.55f;
            float current = RollRating(rng, floor + (ceiling - floor) * along, 1.2f);
            return SkillRating.Make(current, floor, ceiling);
        }

        /// <summary>
        /// Rough bell around <paramref name="mean"/>: average of two uniform
        /// rolls, clamped to the 0-20 rating scale.
        /// </summary>
        private static float RollRating(System.Random rng, float mean, float spread)
        {
            float noise = ((float)rng.NextDouble() + (float)rng.NextDouble() - 1f) * spread;
            float v = mean + noise;
            return v < 0f ? 0f : (v > 20f ? 20f : v);
        }
    }
}
