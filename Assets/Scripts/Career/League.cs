using System;
using System.Collections.Generic;

namespace Sportland.Career
{
    /// <summary>When during a day a game is played (design/hub_actions.md open question — slots get real here first).</summary>
    public enum TimeSlot
    {
        Morning,
        Afternoon,
        Evening,
        Night,
    }

    /// <summary>
    /// Dodgeball's positions, matching the court's 3 infielders + 3 outfielders
    /// per team (CourtSetup). Position sets are per-sport by design
    /// (design/calendar_league.md open questions); this enum grows a shared
    /// abstraction when a second sport needs one.
    /// </summary>
    public enum DodgeballPosition
    {
        Infielder,
        Outfielder,
    }

    /// <summary>
    /// Roster requirements a league imposes. Dodgeball's numbers: 6 on the
    /// court, 4 reserves dressing (10 dressed), plus up to 2 inactive players
    /// (healthy scratches) — max roster 12.
    /// </summary>
    [Serializable]
    public class RosterRules
    {
        public int courtSize = 6;
        public int reserveSize = 4;
        public int inactiveMax = 2;

        public int DressedSize => courtSize + reserveSize;
        public int MaxRoster => DressedSize + inactiveMax;
    }

    /// <summary>One scheduled game. Date stored as ticks so JsonUtility can save it.</summary>
    [Serializable]
    public class Fixture
    {
        public long dateTicks;
        public TimeSlot slot;
        public string opponent;
        public bool home;

        public DateTime Date
        {
            get => new DateTime(dateTicks);
            set => dateTicks = value.Ticks;
        }
    }

    /// <summary>
    /// A rival club in the division: a name and a real roster. The captain
    /// (marked on the athlete) is the club's manager-player — a rival built
    /// from the same parts as the player's character (design/rival_managers.md).
    /// Every rival player, captain included, is poachable in the offseason.
    /// </summary>
    [Serializable]
    public class RivalClub
    {
        public string clubName;
        public List<CareerAthlete> roster = new List<CareerAthlete>();

        public CareerAthlete Captain => roster.Find(a => a.isCaptain);
    }

    /// <summary>
    /// The club's enrollment in one sport's league, including the persistent
    /// lineup: starter slots carry positions by index (0-2 infield, 3-5
    /// outfield), reserves dress without a position, and everyone else in the
    /// club pool is inactive for match days.
    /// </summary>
    [Serializable]
    public class LeagueMembership
    {
        public string sport;
        public string leagueName;
        public string divisionName;
        public RosterRules rules;
        public List<Fixture> fixtures = new List<Fixture>();
        public List<RivalClub> rivals = new List<RivalClub>();

        // Lineup: athlete ids, "" = open slot.
        public string[] starterIds = new string[6] { "", "", "", "", "", "" };
        public string[] reserveIds = new string[4] { "", "", "", "" };

        public static DodgeballPosition StarterPosition(int slot)
            => slot < 3 ? DodgeballPosition.Infielder : DodgeballPosition.Outfielder;

        public static string StarterSlotLabel(int slot)
            => slot < 3 ? $"IN{slot + 1}" : $"OUT{slot - 2}";

        public int StartersFilled
        {
            get { int n = 0; foreach (var id in starterIds) if (id.Length > 0) n++; return n; }
        }

        public int ReservesFilled
        {
            get { int n = 0; foreach (var id in reserveIds) if (id.Length > 0) n++; return n; }
        }

        public bool LineupComplete => StartersFilled == starterIds.Length && ReservesFilled == reserveIds.Length;

        /// <summary>"IN2", "OUT1", "R3", or null when the athlete isn't in the lineup.</summary>
        public string LineupTagOf(string athleteId)
        {
            for (int i = 0; i < starterIds.Length; i++)
                if (starterIds[i] == athleteId) return StarterSlotLabel(i);
            for (int i = 0; i < reserveIds.Length; i++)
                if (reserveIds[i] == athleteId) return $"R{i + 1}";
            return null;
        }

        /// <summary>Pull an athlete out of whatever slot they occupy, if any.</summary>
        public void RemoveFromLineup(string athleteId)
        {
            for (int i = 0; i < starterIds.Length; i++)
                if (starterIds[i] == athleteId) starterIds[i] = "";
            for (int i = 0; i < reserveIds.Length; i++)
                if (reserveIds[i] == athleteId) reserveIds[i] = "";
        }
    }
}
