using System;
using UnityEngine;

namespace Sportland.Career
{
    /// <summary>
    /// The career-layer athlete record: identity, rated general attributes,
    /// and the personality card (expectations, dispositions, volatility) with
    /// per-trait discovery state. This is plain data — match components read
    /// from it via a bridge (later); sports never special-case anyone.
    ///
    /// The player character and the mentor are ego-immune by rule
    /// (design/conflict_chemistry.md §2.1): their personality card is empty
    /// and nothing can write to it.
    /// </summary>
    [Serializable]
    public class CareerAthlete
    {
        public string id;
        public string firstName;
        public string lastName;
        public int age;

        [Tooltip("Marks the player's own character. Ego-immune by rule.")]
        public bool isPlayerCharacter;

        [Tooltip("Chosen archetype id — set for the player character (via the creator) and for rival captains (rolled at generation). Empty otherwise.")]
        public string archetypeId = "";

        [Tooltip("Marks a rival club's captain: their manager-player, built from the same parts as yours. Poachable in the offseason like any player.")]
        public bool isCaptain;

        [Tooltip("Marks the mentor (Skip). Ego-immune by rule; always willing to (re)join.")]
        public bool isMentor;

        [Tooltip("0-100. Recovered overnight and at the Hospital.")]
        [Range(0f, 100f)] public float fatigue;

        // Rated values, indexed by the matching enum. Arrays (not dictionaries)
        // so Unity serialization works without custom machinery.
        public TraitEntry[] generalRatings = new TraitEntry[4];    // GeneralRating
        public TraitEntry[] expectations = new TraitEntry[6];      // ExpectationTrait
        public TraitEntry[] dispositions = new TraitEntry[3];      // DispositionTrait
        public TraitEntry volatility;

        [Tooltip("Growth, peak, or decline. Set from age; DevelopSkills reads it.")]
        public DevelopmentPhase phase;

        [Tooltip("Dodgeball sheet: current / floor / ceiling per skill, indexed by DodgeballSkill.")]
        public SkillRating[] dodgeball = new SkillRating[DodgeballSkills.Count];

        [Tooltip("Special abilities this athlete has. Flags, fixed to the person.")]
        public AthleteAbility abilities;

        public string FullName => string.IsNullOrEmpty(lastName) ? firstName : $"{firstName} {lastName}";

        /// <summary>Ego immunity — the player character and the mentor.</summary>
        public bool IsEgoImmune => isPlayerCharacter || isMentor;

        public TraitEntry GetGeneral(GeneralRating r) => generalRatings[(int)r];
        public TraitEntry GetExpectation(ExpectationTrait t) => expectations[(int)t];
        public TraitEntry GetDisposition(DispositionTrait t) => dispositions[(int)t];

        public SkillRating GetDodgeball(DodgeballSkill skill) => dodgeball[(int)skill];

        /// <summary>Names of the abilities this athlete has, or empty.</summary>
        public string AbilityLabel
        {
            get
            {
                string label = "";
                if ((abilities & AthleteAbility.HotHead) != 0) label = "Hot Head";
                if ((abilities & AthleteAbility.SoleSurvivor) != 0)
                    label = label.Length == 0 ? "Sole Survivor" : label + ", Sole Survivor";
                return label;
            }
        }

        public static DevelopmentPhase PhaseForAge(int years)
        {
            if (years < 24) return DevelopmentPhase.Growth;
            if (years <= 30) return DevelopmentPhase.Peak;
            return DevelopmentPhase.Decline;
        }

        /// <summary>
        /// Stamp every dodgeball skill with the same band. Used when character
        /// creation locks an archetype template onto the player.
        /// </summary>
        public void SetUniformDodgeball(float current, float floor, float ceiling)
        {
            if (dodgeball == null || dodgeball.Length != DodgeballSkills.Count)
                dodgeball = new SkillRating[DodgeballSkills.Count];
            var skill = SkillRating.Make(current, floor, ceiling);
            for (int i = 0; i < dodgeball.Length; i++)
                dodgeball[i] = skill;
        }

        /// <summary>
        /// Old saves predate the dodgeball sheet. Fill it from the general
        /// ratings so a loaded career still has a band to read.
        /// </summary>
        public void EnsureDodgeballSkills()
        {
            phase = PhaseForAge(age);
            if (dodgeball != null && dodgeball.Length == DodgeballSkills.Count)
                return;

            float speed = GeneralOr(GeneralRating.Speed, 10f);
            float agility = GeneralOr(GeneralRating.Agility, 10f);
            float endurance = GeneralOr(GeneralRating.Endurance, 10f);
            float toughness = GeneralOr(GeneralRating.Toughness, 10f);

            dodgeball = new SkillRating[DodgeballSkills.Count];
            dodgeball[(int)DodgeballSkill.ThrowPower] = BandAround(toughness * 0.6f + speed * 0.4f);
            dodgeball[(int)DodgeballSkill.ThrowTechnique] = BandAround(agility);
            dodgeball[(int)DodgeballSkill.CatchTechnique] = BandAround((agility + endurance) * 0.5f);
            dodgeball[(int)DodgeballSkill.Anticipation] = BandAround(agility);
        }

        /// <summary>
        /// One development tick across the dodgeball sheet. Growth climbs toward
        /// each ceiling, decline slides toward each floor, peak holds. Training
        /// calls this; the overnight tick does not.
        /// </summary>
        public void DevelopSkills(float step)
        {
            EnsureDodgeballSkills();
            for (int i = 0; i < dodgeball.Length; i++)
                dodgeball[i].Develop(phase, step);
        }

        private float GeneralOr(GeneralRating rating, float fallback)
        {
            if (generalRatings == null || (int)rating >= generalRatings.Length) return fallback;
            return generalRatings[(int)rating].value;
        }

        private static SkillRating BandAround(float current)
        {
            return SkillRating.Make(current, current - 3f, current + 3f);
        }

        /// <summary>
        /// Reveal one hidden personality trait, if any remain. Returns a
        /// human-readable description of what was learned, or null when the
        /// athlete has nothing left to discover (or is ego-immune).
        /// </summary>
        public string RevealRandomHiddenTrait(System.Random rng)
        {
            if (IsEgoImmune) return null;

            // Collect indices of hidden traits: 0-5 expectations, 6-8 dispositions, 9 volatility.
            var hidden = new System.Collections.Generic.List<int>();
            for (int i = 0; i < expectations.Length; i++)
                if (!expectations[i].revealed) hidden.Add(i);
            for (int i = 0; i < dispositions.Length; i++)
                if (!dispositions[i].revealed) hidden.Add(6 + i);
            if (!volatility.revealed) hidden.Add(9);

            if (hidden.Count == 0) return null;

            int pick = hidden[rng.Next(hidden.Count)];
            if (pick < 6)
            {
                expectations[pick].revealed = true;
                var t = (ExpectationTrait)pick;
                return $"{FullName} — {t}: {expectations[pick].Grade}";
            }
            if (pick < 9)
            {
                int d = pick - 6;
                dispositions[d].revealed = true;
                var t = (DispositionTrait)d;
                return $"{FullName} — {t}: {dispositions[d].Grade}";
            }
            volatility.revealed = true;
            return $"{FullName} — Volatility: {volatility.Grade}";
        }
    }
}
