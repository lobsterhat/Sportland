using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// The shared 0-20 internal rating scale for rated player attributes
    /// (Throwing Speed, and others as they're converted). The raw number is
    /// hidden from players and surfaced as an F..S letter grade; value/20 gives
    /// the 0..1 the gameplay math consumes.
    ///
    /// Grades are deliberately coarse — 7 tiers, 3 ratings wide each — so a low
    /// A and a high A both read "A". Players judge relative quality by feel, not
    /// by a number.
    ///
    ///   0-2  F     3-5  E     6-8  D     9-11 C
    ///   12-14 B    15-17 A    18-20 S
    /// </summary>
    public static class Rating
    {
        public const float Max = 20f;

        /// <summary>Rating → 0..1 for gameplay math.</summary>
        public static float To01(float value) => Mathf.Clamp01(value / Max);

        /// <summary>Rating → player-facing F..S letter grade.</summary>
        public static string Grade(float value)
        {
            int v = Mathf.RoundToInt(Mathf.Clamp(value, 0f, Max));
            if (v <= 2)  return "F";
            if (v <= 5)  return "E";
            if (v <= 8)  return "D";
            if (v <= 11) return "C";
            if (v <= 14) return "B";
            if (v <= 17) return "A";
            return "S";
        }
    }
}
