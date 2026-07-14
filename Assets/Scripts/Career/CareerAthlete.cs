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

        [Tooltip("Player character only: chosen archetype id. Empty until character creation.")]
        public string archetypeId = "";

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

        public string FullName => string.IsNullOrEmpty(lastName) ? firstName : $"{firstName} {lastName}";

        /// <summary>Ego immunity — the player character and the mentor.</summary>
        public bool IsEgoImmune => isPlayerCharacter || isMentor;

        public TraitEntry GetGeneral(GeneralRating r) => generalRatings[(int)r];
        public TraitEntry GetExpectation(ExpectationTrait t) => expectations[(int)t];
        public TraitEntry GetDisposition(DispositionTrait t) => dispositions[(int)t];

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
