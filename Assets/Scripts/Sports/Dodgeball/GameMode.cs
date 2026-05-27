using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>What a hit does to the player who got hit.</summary>
    public enum VictimOutcome
    {
        None,          // hit just scores; victim keeps playing (Mode 1)
        CountToOut,    // Nth hit removes the player (Mode 2)
        DamageEnergy,  // hit drains energy; 0 removes the player (Mode 3)
        Sideline,      // hit benches the player, recallable by a team catch (Mode 4)
    }

    /// <summary>What catching an opponent's throw does, beyond the turnover.</summary>
    public enum CatchEffect
    {
        TurnoverOnly,        // just possession (Modes 1-3)
        ScoreAndReviveTeam,  // point + recall all benched teammates (Mode 4)
    }

    /// <summary>
    /// Data-driven rules for a dodgeball match. Author one asset per mode
    /// (Create &gt; Sportland &gt; Dodgeball &gt; Game Mode); the runtime
    /// DodgeballMatch reads it. This is immutable config — live state (scores,
    /// clock, who's out) lives in DodgeballMatch, not here.
    ///
    /// The planned modes map to:
    ///   1 Running hits : isTimed, pointsPerHit 1, VictimOutcome.None
    ///   2 Elimination  : VictimOutcome.CountToOut (hitsToOut 3), endOnTeamWipeout
    ///   3 Energy       : VictimOutcome.DamageEnergy, endOnTeamWipeout
    ///                    (per-player energy lives on the player, not here)
    ///   4 Hybrid       : isTimed, pointsPerHit 1, pointsPerCatch 1,
    ///                    VictimOutcome.Sideline, CatchEffect.ScoreAndReviveTeam,
    ///                    endOnTeamWipeout
    /// </summary>
    [CreateAssetMenu(menuName = "Sportland/Dodgeball/Game Mode", fileName = "DodgeballGameMode")]
    public class GameMode : ScriptableObject
    {
        [Header("Identity")]
        public string modeName = "Running Hits";

        [Header("Match / timing")]
        public bool isTimed = true;
        [Tooltip("Match length in seconds (used when isTimed).")]
        public float secondsPerPeriod = 120f;
        [Tooltip("End immediately (other team wins) when a team has no players left on the court.")]
        public bool endOnTeamWipeout = false;

        [Header("Scoring")]
        public int pointsPerHit = 1;
        public int pointsPerCatch = 0;

        [Header("On hit")]
        public VictimOutcome victimOutcome = VictimOutcome.None;
        [Tooltip("Hits needed to remove a player (used by VictimOutcome.CountToOut).")]
        public int hitsToOut = 3;

        [Header("On catch")]
        public CatchEffect catchEffect = CatchEffect.TurnoverOnly;

        [Header("Roster (per team)")]
        public int infieldersPerTeam = 3;
        public int outfieldersPerTeam = 3;

        /// <summary>Fallback Mode 1 (running hits) used when no asset is assigned.</summary>
        public static GameMode CreateDefault() => CreateInstance<GameMode>();
    }
}
