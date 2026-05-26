using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Represents the rectangular zone a player is assigned to.
    /// Infielders occupy a court half. Outfielders occupy a strip
    /// alongside the opposing team's court half.
    ///
    /// IMPORTANT: zones are mutually exclusive. An outfielder leaving
    /// their strip and entering another outfielder's strip is still
    /// "out of zone" — same as stepping onto the court.
    /// </summary>
    [System.Serializable]
    public struct PlayZone
    {
        public Vector2 min;
        public Vector2 max;

        public bool Contains(Vector2 p)
        {
            return p.x >= min.x && p.x <= max.x
                && p.y >= min.y && p.y <= max.y;
        }

        /// <summary>
        /// Clamps a position to the zone. Useful for hard-locking movement
        /// (we won't use this for the gameplay rules — players can leave —
        /// but it's handy for AI pathing or respawns).
        /// </summary>
        public Vector2 Clamp(Vector2 p)
        {
            return new Vector2(
                Mathf.Clamp(p.x, min.x, max.x),
                Mathf.Clamp(p.y, min.y, max.y)
            );
        }
    }

    public static class ZoneFactory
    {
        // Outfielder strip thickness (perpendicular to court edge).
        // Tweak to taste — this is the "depth" of the outfielder lane.
        public const float StripDepth = 3f;

        /// <summary>
        /// Returns the zone assigned to a player based on their spawn definition.
        /// We use the spawn ID suffix to distinguish outfielder strips
        /// (Back / Top / Bottom). Infielders share their whole team half.
        /// </summary>
        public static PlayZone For(PlayerSpawn spawn)
        {
            float hw = CourtSetup.HalfWidth;   // 9
            float hh = CourtSetup.HalfHeight;  // 4.5

            if (spawn.role == PlayerRole.Infielder)
            {
                // Team A: left half [-9, 0]. Team B: right half [0, 9].
                return spawn.team == Team.A
                    ? new PlayZone { min = new Vector2(-hw, -hh), max = new Vector2( 0f, hh) }
                    : new PlayZone { min = new Vector2( 0f, -hh), max = new Vector2( hw, hh) };
            }

            // Outfielders surround the *opposing* team's half.
            // Determine which strip from the spawn ID.
            bool surroundsRightHalf = spawn.team == Team.A; // A's outfielders are around B's half

            if (spawn.id.EndsWith("_Back"))
            {
                // Back strip: behind the opposing team's baseline. Runs the full
                // court height so the left/right ends also cover the corners.
                float fh = hh + StripDepth;
                return surroundsRightHalf
                    ? new PlayZone { min = new Vector2( hw,            -fh), max = new Vector2( hw + StripDepth,  fh) }
                    : new PlayZone { min = new Vector2(-hw - StripDepth, -fh), max = new Vector2(-hw,               fh) };
            }
            if (spawn.id.EndsWith("_Top"))
            {
                // Top strip: above the opposing team's top sideline.
                return surroundsRightHalf
                    ? new PlayZone { min = new Vector2( 0f,  hh),                        max = new Vector2( hw,  hh + StripDepth) }
                    : new PlayZone { min = new Vector2(-hw,  hh),                        max = new Vector2( 0f,  hh + StripDepth) };
            }
            if (spawn.id.EndsWith("_Bottom"))
            {
                // Bottom strip: below the opposing team's bottom sideline.
                return surroundsRightHalf
                    ? new PlayZone { min = new Vector2( 0f, -hh - StripDepth),           max = new Vector2( hw, -hh) }
                    : new PlayZone { min = new Vector2(-hw, -hh - StripDepth),           max = new Vector2( 0f, -hh) };
            }

            Debug.LogError($"[ZoneFactory] Unknown outfielder id pattern: {spawn.id}");
            return new PlayZone();
        }
    }
}
