using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Debug overlay above each player:
    ///   • Jersey label (team letter + number, e.g. "A2") so players are easy
    ///     to tell apart on screen.
    ///   • Red penalty countdown when a carrier is out of their zone.
    ///   • Optional AI decision line (the chain node currently driving the AI —
    ///     "Offense", "Intercept", "Chase", etc.) toggleable live from the
    ///     Match controls panel.
    /// Jersey numbers follow spawn order per team — infielders 1-3, outfielders
    /// 4-6. IMGUI, so it needs no Canvas / TMP. Spawned by CourtSetup.
    /// </summary>
    public class DodgeballPlayerLabels : MonoBehaviour
    {
        [SerializeField] private bool show = true;
        [Tooltip("Show the AI's current chain decision under each player. Toggleable live from the Match controls panel.")]
        [SerializeField] private bool showAIDecisions = true;
        [Tooltip("World-space height (units) above the player root to float the label.")]
        [SerializeField] private float heightAbove = 1.3f;
        [SerializeField] private int fontSize = 16;
        [Tooltip("Smaller font for the AI decision line (debug only).")]
        [SerializeField] private int decisionFontSize = 11;

        /// <summary>Live toggle for the AI decision overlay — used by the Match controls panel.</summary>
        public bool ShowAIDecisions { get => showAIDecisions; set => showAIDecisions = value; }

        private GUIStyle style;
        private GUIStyle decisionStyle;
        private Camera cam;

        private void OnGUI()
        {
            if (!show) return;
            if (cam == null) cam = Camera.main;
            if (cam == null) return;
            EnsureStyle();

            var all = PlayerZoneTracker.All;
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (t == null) continue;

                Vector3 screen = cam.WorldToScreenPoint(t.transform.position + Vector3.up * heightAbove);
                if (screen.z <= 0f) continue;   // behind the camera

                // WorldToScreenPoint's origin is bottom-left; GUI's is top-left.
                var rect = new Rect(screen.x - 24f, Screen.height - screen.y - 12f, 48f, 24f);
                DrawShadowed(rect, $"{(t.Spawn.team == Team.A ? "A" : "B")}{t.Number}", Color.white, style);

                // Stack any extra rows below the jersey, top-to-bottom.
                float belowY = rect.y + rect.height + 2f;

                // Penalty countdown: a carrier who's out of their area is on
                // the clock. Show the remaining seconds in red so it reads as urgent.
                if (t.HasBall && !t.IsInZone)
                {
                    var cRect = new Rect(rect.x, belowY, rect.width, 22f);
                    DrawShadowed(cRect, $"{t.SecondsUntilReturnExpiry:F1}", new Color(1f, 0.35f, 0.35f), style);
                    belowY += 20f;
                }

                // AI decision (debug): which chain node is driving this player.
                if (showAIDecisions)
                {
                    var ai = t.GetComponent<DodgeballAI>();
                    if (ai != null && !string.IsNullOrEmpty(ai.CurrentDecision))
                    {
                        var dRect = new Rect(rect.x - 24f, belowY, rect.width + 48f, 18f);
                        DrawShadowed(dRect, ai.CurrentDecision, new Color(0.7f, 0.9f, 1f), decisionStyle);
                    }
                }
            }
        }

        // Text with a dark offset copy behind it, so it reads over any sprite.
        private void DrawShadowed(Rect rect, string text, Color color, GUIStyle s)
        {
            s.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, s);
            s.normal.textColor = color;
            GUI.Label(rect, text, s);
        }

        private void EnsureStyle()
        {
            if (style != null && style.fontSize == fontSize
                && decisionStyle != null && decisionStyle.fontSize == decisionFontSize) return;
            style = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };
            decisionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = decisionFontSize,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Normal,
            };
        }
    }
}
