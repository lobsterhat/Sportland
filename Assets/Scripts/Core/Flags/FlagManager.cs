using UnityEngine;
using System.Collections.Generic;
using Sportland.Core.Athletes;

namespace Sportland.Core.Flags
{
    /// <summary>
    /// FlagManager - Handles flag acquisition, removal, and progression
    /// TODO: Implement full flag system based on design document
    /// </summary>
    public class FlagManager : MonoBehaviour
    {
        // Singleton instance
        public static FlagManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        // TODO: Implement flag trigger system
        // TODO: Implement flag removal/redemption system
        // TODO: Implement flag weight system (Light, Medium, Heavy, Permanent)
        // TODO: Implement sport-specific vs universal flags
        // TODO: Implement flag display notifications
    }

    /// <summary>
    /// Base class for all flags
    /// TODO: Implement based on MLB Power Pros-style system
    /// </summary>
    [System.Serializable]
    public class Flag
    {
        public string flagID;
        public string displayName;
        public string description;
        public FlagType type; // Positive, Negative, Neutral
        public FlagWeight weight; // Light (1-3), Medium (4-6), Heavy (7-9), Permanent (10)
        public FlagScope scope; // Universal or Sport-Specific

        // TODO: Implement flag effects on stats
        // TODO: Implement flag behavioral modifiers
        // TODO: Implement flag removal conditions
    }

    public enum FlagType
    {
        Positive,
        Negative,
        Neutral
    }

    public enum FlagWeight
    {
        Light = 1,      // Easy to change (5-15 games)
        Medium = 4,     // Moderate challenge (20-40 games)
        Heavy = 7,      // Very difficult (1-3 seasons)
        Permanent = 10  // Unchangeable
    }

    public enum FlagScope
    {
        Universal,      // Applies to all sports
        SportSpecific   // Only applies to one sport
    }
}
