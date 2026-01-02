using UnityEngine;
using System.Collections.Generic;
using Sportland.Core.Athlete;

namespace Sportland.Core.GameManagement
{
    /// <summary>
    /// Interface that all sport modules must implement
    /// This allows CoreGameManager to load/unload sports dynamically
    /// </summary>
    public interface ISportModule
    {
        // ===== IDENTITY =====

        /// <summary>
        /// What sport is this?
        /// </summary>
        SportType GetSportType();

        /// <summary>
        /// Display name for UI
        /// </summary>
        string GetSportName();

        // ===== LIFECYCLE =====

        /// <summary>
        /// Called when sport module is first loaded
        /// Setup any sport-specific systems here
        /// </summary>
        void Initialize(List<Athlete.Athlete> availableAthletes);

        /// <summary>
        /// Called when a game is about to start
        /// </summary>
        void StartGame(GameContext context);

        /// <summary>
        /// Called when sport is being unloaded
        /// Clean up resources, save state, etc.
        /// </summary>
        void Cleanup();

        // ===== GAME RESULTS =====

        /// <summary>
        /// Get the results from the game that just finished
        /// Called by CoreGameManager after game ends
        /// </summary>
        GameResult GetGameResults();

        // ===== VALIDATION =====

        /// <summary>
        /// Can this athlete participate in a game?
        /// Check fatigue, injuries, eligibility, etc.
        /// </summary>
        bool CanAthletePlay(Athlete.Athlete athlete, GameContext context);
    }

    /// <summary>
    /// Context/setup information passed to a sport when starting a game
    /// </summary>
    [System.Serializable]
    public class GameContext
    {
        public System.DateTime gameDate;
        public List<Athlete.Athlete> homeRoster;
        public List<Athlete.Athlete> awayRoster;
        public bool isPlayoffGame;
        public string venueName;
        public int homeScore; // For continuing games
        public int awayScore;
    }

    /// <summary>
    /// Results returned from a completed game
    /// CoreGameManager uses this to update stats, process flags, etc.
    /// </summary>
    [System.Serializable]
    public class GameResult
    {
        public SportType sport;
        public System.DateTime gameDate;

        public int homeScore;
        public int awayScore;
        public bool homeWon;

        public List<PlayerGameStats> playerStats;
        public List<GameEvent> significantEvents; // For flag triggers

        public float gameDuration; // For fatigue calculation
    }

    /// <summary>
    /// Individual player's stats from a game
    /// </summary>
    [System.Serializable]
    public class PlayerGameStats
    {
        public string athleteID;
        public int points; // Or runs, goals, etc.
        public float minutesPlayed;

        // Sport-specific stats can be added as needed
        public Dictionary<string, float> customStats = new Dictionary<string, float>();
    }

    /// <summary>
    /// Significant events during a game (for flag triggers)
    /// </summary>
    [System.Serializable]
    public class GameEvent
    {
        public enum EventType
        {
            ClutchShot,
            MissedClutchShot,
            GameWinningPlay,
            InjuryOccurred,
            FightStarted,
            PerfectGame
        }

        public EventType type;
        public string athleteID; // Who was involved
        public string description;
        public float gameTime; // When it happened
    }

    /// <summary>
    /// Enum for all supported sports
    /// </summary>
    public enum SportType
    {
        Basketball,
        Baseball,
        Football,
        Volleyball,
        Dodgeball,
        Hockey
    }
}
