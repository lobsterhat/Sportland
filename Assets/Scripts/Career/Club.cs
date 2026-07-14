using System;
using System.Collections.Generic;

namespace Sportland.Career
{
    /// <summary>
    /// The player's club: one signed pool of athletes, from which each sport's
    /// roster will eventually draw (design/athlete_development.md §6 — nobody
    /// has to be on every team; unrostered athletes are the development squad).
    /// Per-sport rosters arrive with the league system.
    /// </summary>
    [Serializable]
    public class Club
    {
        public string clubName;
        public List<CareerAthlete> pool = new List<CareerAthlete>();

        public CareerAthlete PlayerCharacter => pool.Find(a => a.isPlayerCharacter);
        public CareerAthlete Mentor => pool.Find(a => a.isMentor);
    }
}
