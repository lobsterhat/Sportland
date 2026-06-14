namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Identifies a single rateable stat that a Special Ability can modify.
    /// Used as the lookup key when a player's ability aggregator reports the
    /// active multiplier for a stat (effective = base * product-of-multipliers).
    ///
    /// Scoped to the dodgeball throw/catch ratings for now. When the ability
    /// system spans multiple sports this enum (and the aggregator it keys into)
    /// promotes to a shared cross-sport location and grows general stats
    /// (Speed, Acceleration, Endurance, ...) plus each sport's specific stats.
    /// </summary>
    public enum AbilityStat
    {
        Catching,
        ThrowSpeed,
        ThrowAccuracy,
        Anticipation,
    }
}
