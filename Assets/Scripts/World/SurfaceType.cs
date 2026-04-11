namespace Sportland.World
{
    /// <summary>
    /// Identifies the type of surface or terrain a zone represents.
    /// Each type has built-in default movement modifiers defined in SurfaceDefinition.
    /// </summary>
    public enum SurfaceType
    {
        // ── Hard/paved ──
        Asphalt,      // neutral baseline — all multipliers 1.0
        Concrete,     // slightly firmer, marginally better grip
        SportCourt,   // optimised athletic surface — best acceleration and traction

        // ── Natural ground ──
        Grass,        // slight drag, mild traction loss
        Gravel,       // moderate drag and stamina cost, reduced traction
        Sand,         // heavy drag, very tiring, no sprint
        Mud,          // slippery and slow, no sprint

        // ── Water ──
        Water,        // heavy resistance, most tiring; disables sprint, dive, lunge, barge

        // ── Directional slope ──
        Slope,        // speed varies with movement direction relative to slopeDirection

        // ── Obstacle (soft) ──
        Undergrowth,  // dense vegetation — extreme drag, no sprint/dive/lunge
    }
}
