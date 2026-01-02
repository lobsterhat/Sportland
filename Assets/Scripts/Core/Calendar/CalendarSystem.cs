using UnityEngine;
using System;
using Sportland.Core.GameManagement;

namespace Sportland.Core.Calendar
{
    /// <summary>
    /// CalendarSystem - Manages time progression and multi-sport season overlap
    /// TODO: Implement full calendar system with overlapping seasons
    /// </summary>
    public class CalendarSystem : MonoBehaviour
    {
        // Singleton instance
        public static CalendarSystem Instance { get; private set; }

        [Header("Current Date")]
        public DateTime currentDate = new DateTime(2025, 9, 1); // Sept 1, 2025

        // Active Seasons
        // TODO: Track which sports are in season
        // TODO: Handle season overlaps
        // TODO: Manage schedules across multiple sports

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

        // TODO: Implement day advancement
        // TODO: Implement season start/end dates for each sport
        // TODO: Implement schedule generation
        // TODO: Implement event scheduling (games, practices, team building)
        // TODO: Implement multi-sport athlete fatigue tracking across seasons
    }

    /// <summary>
    /// Season definition for a sport
    /// TODO: Implement season structure
    /// </summary>
    [System.Serializable]
    public class Season
    {
        public SportType sport;
        public DateTime startDate;
        public DateTime endDate;
        public bool isActive;

        // TODO: Implement playoff structure
        // TODO: Implement game scheduling
    }
}
