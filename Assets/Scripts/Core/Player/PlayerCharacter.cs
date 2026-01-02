using UnityEngine;
using Sportland.Core.Athletes;

namespace Sportland.Core.Player
{
    /// <summary>
    /// PlayerCharacter - The user's character in the sports city
    /// Can participate in games AND manage teams
    /// TODO: Implement full player-coach system
    /// </summary>
    public class PlayerCharacter : MonoBehaviour
    {
        [Header("Character Identity")]
        public string playerName;
        public PlayerClass characterClass; // Motivator, Tactician, Superstar, etc.

        [Header("Skills")]
        public PlayerSkills skills;
        public PlayerManagementStats managementStats;

        // TODO: Implement character creation
        // TODO: Implement class system with trade-offs (skill vs management)
        // TODO: Implement player progression
        // TODO: Implement needs/wants system
    }

    /// <summary>
    /// Player Class - Defines playstyle and trade-offs
    /// TODO: Implement based on design doc (Superstar, Player-Coach, Tactician, Motivator, Developer)
    /// </summary>
    [System.Serializable]
    public class PlayerClass
    {
        public string className;
        public string description;

        // Skill modifiers (affects playing ability)
        public float skillMultiplier = 1.0f;

        // Management modifiers (affects team management)
        public float managementMultiplier = 1.0f;
        public int bonusActionsPerDay = 0;
        public float teamMoraleBonus = 0f;

        // TODO: Implement class-specific abilities
        // TODO: Implement class progression
    }

    [System.Serializable]
    public class PlayerSkills
    {
        // Universal athletic stats (same as Athlete)
        [Range(0, 100)] public float speed = 50f;
        [Range(0, 100)] public float strength = 50f;
        [Range(0, 100)] public float agility = 50f;
        [Range(0, 100)] public float reactionTime = 50f;

        // Sport-specific skills added dynamically
        // TODO: Multi-sport skill tracking
    }

    [System.Serializable]
    public class PlayerManagementStats
    {
        [Range(0, 100)] public float timeManagement = 50f;
        [Range(0, 100)] public float motivation = 50f;
        [Range(0, 100)] public float tacticalKnowledge = 50f;
        [Range(0, 100)] public float scoutingAbility = 50f;
        [Range(0, 100)] public float playerDevelopment = 50f;

        // TODO: Implement management stat effects
    }
}
