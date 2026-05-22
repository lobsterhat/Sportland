namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Unit conversions for readouts. The court is built at 1 Unity unit = 1
    /// metre, so a speed in units/sec is just metres/sec.
    /// </summary>
    public static class DodgeballUnits
    {
        public const float MphPerUnitPerSecond = 2.2369363f;   // (m/s) -> mph
        public const float UnitsPerSecondPerMph = 0.44704f;    // mph -> (m/s)

        public static float ToMph(float unitsPerSecond) => unitsPerSecond * MphPerUnitPerSecond;
        public static float ToUnitsPerSecond(float mph) => mph * UnitsPerSecondPerMph;
    }
}
