namespace Sportland.Sports.Demoball
{
    /// <summary>
    /// The role a player occupies in Demoball for the current period.
    ///
    /// Roles are assigned by DemoballGameManager at the start of each period and
    /// may change between periods as teams swap offense/defense.
    ///
    /// Pickup rules:
    ///   - Scorer: the only role that may initiate possession of a fresh (cannon-fired) ball.
    ///   - Blocker: may carry and pass once the ball has been scorer-touched; cannot pick up fresh.
    ///   - Defender: recovers loose balls to remove them from play; cannot score.
    /// </summary>
    public enum DemoballRole
    {
        Scorer,    // 2 per offensive lineup — initiate possession; can pass to any teammate
        Blocker,   // 4 per offensive lineup — carry/screen after scorer initiates; cannot pick up fresh
        Defender   // 6 per defensive lineup — free range; tackle ball-carriers; recovery = ball out of play
    }
}
