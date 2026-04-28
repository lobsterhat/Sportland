namespace Sportland.Sports.Demoball
{
    /// <summary>
    /// Possible results of a shove attempt during an engagement. Some are
    /// symmetric (either role can roll them); others are role-specific.
    /// </summary>
    public enum ShoveOutcome
    {
        /// <summary>Failed shove. Both stay engaged, no positional change.</summary>
        Hold,

        /// <summary>Both disengage with a brief mutual stun (collision/recovery).</summary>
        Collide,

        /// <summary>Loser prone for an extended stun. Winner disengages free.</summary>
        Knockdown,

        /// <summary>Loser knocked back along the shove axis. Both disengage.</summary>
        PushBack,

        /// <summary>Defender disengages cleanly to a chosen side.</summary>
        SpinOff,

        /// <summary>Defender disengages past the blocker, ending up carrier-side.</summary>
        SwimThrough,

        /// <summary>Blocker bumps the defender (small impulse) and disengages free to receive.</summary>
        ChipBlock
    }
}
