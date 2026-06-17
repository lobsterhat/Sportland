using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Shared settings for the IMGUI on-screen overlays (score, debug HUDs).
    /// CourtSetup assigns <see cref="Font"/> from its inspector slot so the
    /// runtime-added overlay components (which have no Inspector of their own)
    /// can pick it up. Null = Unity's built-in GUI font.
    /// </summary>
    public static class DodgeballUI
    {
        /// <summary>Optional font for IMGUI text. Apply via style.font; null falls back to the default.</summary>
        public static Font Font;
    }
}
