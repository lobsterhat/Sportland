using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Debug overlay: floats each player's jersey label (team letter + number,
    /// e.g. "A2") above them so players are easy to tell apart on screen.
    /// Numbers follow spawn order per team — infielders 1-3, outfielders 4-6.
    /// IMGUI, so it needs no Canvas / TMP. Spawned by CourtSetup.
    /// </summary>
    public class DodgeballPlayerLabels : MonoBehaviour
    {
        [SerializeField] private bool show = true;
        [Tooltip("World-space height (units) above the player root to float the label.")]
        [SerializeField] private float heightAbove = 1.3f;
        [SerializeField] private int fontSize = 16;

        private GUIStyle style;
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
                DrawShadowed(rect, $"{(t.Spawn.team == Team.A ? "A" : "B")}{t.Number}");
            }
        }

        // White text with a dark offset copy behind it, so it reads over any sprite.
        private void DrawShadowed(Rect rect, string text)
        {
            style.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, style);
            style.normal.textColor = Color.white;
            GUI.Label(rect, text, style);
        }

        private void EnsureStyle()
        {
            if (style != null && style.fontSize == fontSize) return;
            style = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };
        }
    }
}
