using System.Collections.Generic;

namespace Sportland.Career
{
    /// <summary>
    /// The handoff between the hub and a playable sport scene. The hub fills
    /// it and loads the sport; the sport's career director reads the rosters,
    /// plays the match, writes the result, and loads the hub back. Static so
    /// it survives the scene swap without any scene object involved.
    /// </summary>
    public static class CareerMatchContext
    {
        /// <summary>True while a career match is being set up or played.</summary>
        public static bool Active;

        public static string ourClubName;
        public static string theirClubName;

        /// <summary>Our six starters, by lineup slot: 0-2 infield, 3-5 outfield.</summary>
        public static List<CareerAthlete> ourStarters = new List<CareerAthlete>();

        /// <summary>Their six starters, same ordering (best-first from their squad).</summary>
        public static List<CareerAthlete> theirStarters = new List<CareerAthlete>();

        /// <summary>Which of our starters the human controls (athlete id).</summary>
        public static string controlledAthleteId;

        /// <summary>Set by the sport when the final whistle result is in.</summary>
        public static bool ResultReady;
        public static int ourScore;
        public static int theirScore;

        public static void Clear()
        {
            Active = false;
            ResultReady = false;
            ourClubName = theirClubName = controlledAthleteId = null;
            ourStarters.Clear();
            theirStarters.Clear();
            ourScore = theirScore = 0;
        }
    }
}
