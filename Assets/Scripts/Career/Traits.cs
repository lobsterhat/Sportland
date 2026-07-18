using Sportland.Core;

namespace Sportland.Career
{
    /// <summary>
    /// Expectation traits — claims an athlete makes on finite team resources.
    /// Unmet expectations breed discontent (design/conflict_chemistry.md §2).
    /// </summary>
    public enum ExpectationTrait
    {
        PlayingTime,
        Position,
        Starter,
        Spotlight,
        Recognition,
        Workload,
    }

    /// <summary>
    /// Disposition traits — styles, not claims. They shape willingness to do
    /// or avoid activities and interactions (design/conflict_chemistry.md §2.3).
    /// </summary>
    public enum DispositionTrait
    {
        Social,
        Competitive,
        Openness,
    }

    /// <summary>
    /// Career-side general athletic ratings for the vertical slice. These are
    /// the career record's numbers; match components (e.g. dodgeball's
    /// GeneralAttributes) will read from them once the bridge is built.
    /// </summary>
    public enum GeneralRating
    {
        Speed,
        Agility,
        Endurance,
        Toughness,
    }

    /// <summary>
    /// One rated value on the shared 0-20 scale plus its discovery state.
    /// Hidden until scouted or revealed (design/conflict_chemistry.md §2.2):
    /// undiscovered traits display as "?" on the athlete card.
    /// </summary>
    [System.Serializable]
    public struct TraitEntry
    {
        public float value;    // 0-20, Rating scale
        public bool revealed;

        public TraitEntry(float value, bool revealed = false)
        {
            this.value = value;
            this.revealed = revealed;
        }

        /// <summary>The true grade — for systems, not for display.</summary>
        public string Grade => Rating.Grade(value);

        /// <summary>What the player is allowed to see.</summary>
        public string DisplayGrade => revealed ? Rating.Grade(value) : "?";
    }
}
