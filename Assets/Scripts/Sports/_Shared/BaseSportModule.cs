using UnityEngine;
using System.Collections.Generic;
using Sportland.Core.GameManagement;
using Sportland.Core.Athlete;

namespace Sportland.Sports.Shared
{
    /// <summary>
    /// BaseSportModule - Abstract base class for all sport modules
    /// Provides common functionality that all sports share
    /// TODO: Implement shared sport logic
    /// </summary>
    public abstract class BaseSportModule : MonoBehaviour, ISportModule
    {
        // Sport Identity
        protected SportType sportType;
        protected string sportName;

        // Game State
        protected List<Athlete.Athlete> availableAthletes;
        protected GameContext currentContext;
        protected GameResult gameResult;

        // ===== ISportModule Implementation =====

        public virtual SportType GetSportType() => sportType;
        public virtual string GetSportName() => sportName;

        public virtual void Initialize(List<Athlete.Athlete> athletes)
        {
            availableAthletes = athletes;
            Debug.Log($"{sportName} module initialized with {athletes.Count} athletes");
        }

        public virtual void StartGame(GameContext context)
        {
            currentContext = context;
            Debug.Log($"Starting {sportName} game");
        }

        public virtual void Cleanup()
        {
            Debug.Log($"{sportName} module cleaning up");
        }

        public abstract GameResult GetGameResults();

        public virtual bool CanAthletePlay(Athlete.Athlete athlete, GameContext context)
        {
            // Default implementation - check fatigue
            if (athlete.fatigue > 80f)
            {
                Debug.Log($"{athlete.GetFullName()} is too fatigued to play");
                return false;
            }

            return true;
        }

        // ===== Shared Helper Methods =====

        protected virtual void RecordGameEvent(GameEvent.EventType eventType, string athleteID, string description)
        {
            if (gameResult == null)
            {
                gameResult = new GameResult();
                gameResult.significantEvents = new List<GameEvent>();
            }

            gameResult.significantEvents.Add(new GameEvent
            {
                type = eventType,
                athleteID = athleteID,
                description = description,
                gameTime = Time.time
            });
        }

        // TODO: Implement shared gameplay systems
        // TODO: Implement shared UI elements
        // TODO: Implement shared stat tracking
    }
}
