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

    /// <summary>One scheduled game.</summary>
    [Serializable]
    public class Fixture
    {
        public DateTime date;
        public TimeSlot slot;
        public string opponent;
        public bool home;
    }

    /// <summary>The club's enrollment in one sport's league.</summary>
    [Serializable]
    public class LeagueMembership
    {
        public string sport;
        public string leagueName;
        public string divisionName;
        public RosterRules rules;
        public List<Fixture> fixtures = new List<Fixture>();
    }
}
