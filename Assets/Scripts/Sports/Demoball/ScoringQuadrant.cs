namespace Sportland.Sports.Demoball
{
    /// <summary>
    /// One of the four 90° sectors of the scoring ring, measured counter-clockwise
    /// from world +X (standard math convention):
    ///   Q1 = NE (  0° -  90°)
    ///   Q2 = NW ( 90° - 180°)
    ///   Q3 = SW (180° - 270°)
    ///   Q4 = SE (270° - 360°)
    ///
    /// At any time exactly one quadrant is "locked" (un-scorable). After a score,
    /// the quadrant the carrier scored in becomes the new locked quadrant and the
    /// previous one re-opens.
    /// </summary>
    public enum ScoringQuadrant
    {
        Q1,
        Q2,
        Q3,
        Q4
    }
}
