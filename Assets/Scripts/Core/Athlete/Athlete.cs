using UnityEngine;
using System;
using System.Collections.Generic;
using Sportland.Core.GameManagement;
using Sportland.Rendering.Characters;

namespace Sportland.Core.Athletes
{
    /// <summary>
    /// Athlete - The core character class that persists across all sports
    /// This is a ScriptableObject so athletes exist as data assets
    /// </summary>
    [CreateAssetMenu(fileName = "New Athlete", menuName = "Sportland/Athlete")]
    public class Athlete : ScriptableObject
    {
        [Header("Identity")]
        public string athleteID; // Unique identifier
        public string firstName;
        public string lastName;

        [Header("Universal Physical Stats")]
        public float height = 1.830f; // Height in units (default: 6'0")
        [Range(0, 100)] public float speed = 50f;
        [Range(0, 100)] public float strength = 50f;
        [Range(0, 100)] public float agility = 50f;
        [Range(0, 100)] public float reactionTime = 50f;

        [Header("Condition")]
        [Range(0, 100)] public float fatigue = 0f;
        [Range(0, 100)] public float stamina = 100f;

        [Header("Sport-Specific Stats")]
        public List<SportStats> sportStats = new List<SportStats>();

        [Header("Appearance")]
        [Tooltip("Visual configuration used by ModularCharacterRig to render this athlete.")]
        public CharacterAppearance appearance;

        [Header("Flags")]
        public List<string> currentFlags = new List<string>(); // Simple string IDs for now

        // Helper method to get letter grade from numeric value
        public LetterGrade GetLetterGrade(float value)
        {
            if (value >= 90) return LetterGrade.S;
            if (value >= 80) return LetterGrade.A;
            if (value >= 70) return LetterGrade.B;
            if (value >= 60) return LetterGrade.C;
            if (value >= 50) return LetterGrade.D;
            return LetterGrade.F;
        }

        // Get display name
        public string GetFullName()
        {
            return $"{firstName} {lastName}";
        }

        // Get sport-specific stats
        public SportStats GetSportStats(SportType sport)
        {
            return sportStats.Find(s => s.sportType == sport);
        }

        // Add sport stats if they don't exist
        public void InitializeSportStats(SportType sport)
        {
            if (GetSportStats(sport) == null)
            {
                sportStats.Add(new SportStats { sportType = sport });
            }
        }

        // Flag helpers (we'll expand this later)
        public bool HasFlag(string flagID)
        {
            return currentFlags.Contains(flagID);
        }

        public void AddFlag(string flagID)
        {
            if (!HasFlag(flagID))
            {
                currentFlags.Add(flagID);
                Debug.Log($"{GetFullName()} acquired flag: {flagID}");
            }
        }

        public void RemoveFlag(string flagID)
        {
            if (HasFlag(flagID))
            {
                currentFlags.Remove(flagID);
                Debug.Log($"{GetFullName()} lost flag: {flagID}");
            }
        }
    }

    /// <summary>
    /// Sport-specific stats container
    /// </summary>
    [System.Serializable]
    public class SportStats
    {
        public SportType sportType;

        // Basketball stats
        [Range(0, 100)] public float shooting;
        [Range(0, 100)] public float defense;
        [Range(0, 100)] public float rebounding;
        [Range(0, 100)] public float ballHandling;

        // Baseball stats
        [Range(0, 100)] public float batting;
        [Range(0, 100)] public float pitching;
        [Range(0, 100)] public float fielding;

        // Can add more sport stats as needed

        // Season stats (games played, points, etc.)
        public int gamesPlayed;
        public int points; // or runs, goals, etc.
    }

    /// <summary>
    /// Letter grade enum
    /// </summary>
    public enum LetterGrade
    {
        F = 0,
        D = 1,
        C = 2,
        B = 3,
        A = 4,
        S = 5
    }

    /// <summary>
    /// Height constants in units (1 ft = 0.305 units, based on hoop height of 10' = 3.05 units)
    /// </summary>
    public static class HeightConstants
    {
        public const float Height_5_0 = 1.525f;  // 5'0"
        public const float Height_5_3 = 1.601f;  // 5'3"
        public const float Height_5_9 = 1.754f;  // 5'9"
        public const float Height_6_0 = 1.830f;  // 6'0"
        public const float Height_6_3 = 1.906f;  // 6'3"
        public const float Height_6_6 = 1.983f;  // 6'6"
        public const float Height_6_9 = 2.059f;  // 6'9"
        public const float Height_7_0 = 2.135f;  // 7'0"

        /// <summary>
        /// Convert feet and inches to units
        /// </summary>
        public static float FeetInchesToUnits(int feet, int inches)
        {
            return (feet + inches / 12f) * 0.305f;
        }

        /// <summary>
        /// Convert units to feet and inches
        /// </summary>
        public static (int feet, int inches) UnitsToFeetInches(float units)
        {
            float totalFeet = units / 0.305f;
            int feet = (int)totalFeet;
            int inches = Mathf.RoundToInt((totalFeet - feet) * 12f);
            return (feet, inches);
        }
    }
}
