namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Compatibility bridge: the Rating scale was promoted to Sportland.Core
    /// (as its comments always planned), but existing dodgeball code — and
    /// work in progress — references it unqualified from this namespace.
    /// This forwarder keeps every old call site compiling; new code should
    /// use Sportland.Core.Rating directly, and this file can be deleted once
    /// all dodgeball references migrate.
    /// </summary>
    public static class Rating
    {
        public const float Max = Sportland.Core.Rating.Max;

        /// <summary>Rating → 0..1 for gameplay math.</summary>
        public static float To01(float value) => Sportland.Core.Rating.To01(value);

        /// <summary>Rating → player-facing F..S letter grade.</summary>
        public static string Grade(float value) => Sportland.Core.Rating.Grade(value);
    }
}
