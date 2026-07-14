namespace Sportland.Career
{
    /// <summary>
    /// One archetype card (design/character_creator.md §4). Hardcoded for the
    /// slice; graduates to ScriptableObject assets when rival generation needs
    /// to consume the same definitions.
    ///
    /// The only perk wired to a real system so far is the daily action count
    /// (Superstar 2 / Player-Coach 4 / others 3); the signature perks activate
    /// as their systems come online (conflicts, training, play-calling).
    /// </summary>
    [System.Serializable]
    public class ArchetypeDefinition
    {
        public string id;
        public string displayName;
        public string fantasy;          // one-line promise
        public string playingGrade;     // F-S display grade for the Playing meter
        public string managementGrade;  // F-S display grade for the Management meter
        public string perkName;
        public string perkDescription;
        public string theCatch;         // the honest weakness, straight off the card
        public string skipLine;         // Skip's plain-language take on the card
        public int actionsPerDay;
        public float generalRatingValue; // slice template: applied to all general ratings
    }

    /// <summary>The proposed roster of six, spanning the playing-vs-managing axis.</summary>
    public static class Archetypes
    {
        public static readonly ArchetypeDefinition[] All =
        {
            new ArchetypeDefinition
            {
                id = "superstar", displayName = "Superstar",
                fantasy = "Win the games yourself.",
                playingGrade = "A", managementGrade = "D",
                perkName = "Take Over",
                perkDescription = "Clutch-moment performance surge.",
                theCatch = "Fewest daily actions — the team leans on your play, not your leadership.",
                skipLine = "\"You'll win games with your own two hands. Just don't expect much time for anything else.\"",
                actionsPerDay = 2, generalRatingValue = 16f,
            },
            new ArchetypeDefinition
            {
                id = "player_coach", displayName = "Player-Coach",
                fantasy = "A bit of everything, every day.",
                playingGrade = "B", managementGrade = "B",
                perkName = "Double Shift",
                perkDescription = "One bonus management action per day.",
                theCatch = "Master of none: no elite edge anywhere.",
                skipLine = "\"The steady hand. You'll touch every part of this club, a medium amount.\"",
                actionsPerDay = 4, generalRatingValue = 13f,
            },
            new ArchetypeDefinition
            {
                id = "tactician", displayName = "Tactician",
                fantasy = "Out-scheme everyone.",
                playingGrade = "C", managementGrade = "A",
                perkName = "Chalkboard",
                perkDescription = "Expanded play-calling and opponent-tendency reads in-game.",
                theCatch = "Modest body: the genius is in the scheme, not your legs.",
                skipLine = "\"Your legs are ordinary. Your clipboard is not.\"",
                actionsPerDay = 3, generalRatingValue = 10f,
            },
            new ArchetypeDefinition
            {
                id = "motivator", displayName = "Motivator",
                fantasy = "The team plays above its numbers.",
                playingGrade = "C", managementGrade = "A",
                perkName = "Locker Room Aura",
                perkDescription = "Passive team-wide morale and chemistry boost; slump protection.",
                theCatch = "Weak on tactics and scouting — you inspire, you don't outscheme.",
                skipLine = "\"People run through walls for coaches like you. Mind the X's and O's, though.\"",
                actionsPerDay = 3, generalRatingValue = 10f,
            },
            new ArchetypeDefinition
            {
                id = "mediator", displayName = "Mediator",
                fantasy = "Hold together a locker room nobody else could.",
                playingGrade = "C", managementGrade = "A",
                perkName = "Clear the Air",
                perkDescription = "Far better odds resolving teammate conflicts; resolutions build chemistry.",
                theCatch = "Reactive power: a peaceful locker room leaves your gift idle.",
                skipLine = "\"Big egos, difficult stars, old grudges — you can sign trouble and make it work.\"",
                actionsPerDay = 3, generalRatingValue = 10f,
            },
            new ArchetypeDefinition
            {
                id = "developer", displayName = "Developer",
                fantasy = "Build champions from prospects.",
                playingGrade = "C", managementGrade = "A",
                perkName = "Growth Eye",
                perkDescription = "Training gains multiplier; spots hidden potential in athletes.",
                theCatch = "Little game-day impact — your wins are built weeks earlier.",
                skipLine = "\"Your trophies take seasons to grow. They're the sweetest kind.\"",
                actionsPerDay = 3, generalRatingValue = 10f,
            },
        };

        public static ArchetypeDefinition ById(string id)
        {
            foreach (var a in All)
                if (a.id == id) return a;
            return null;
        }

        public static string NameOf(string id) => ById(id)?.displayName ?? "";
    }
}
