using System.Collections.Generic;
using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Debug play-by-play log: a scrollable text field of throw/pass outcomes
    /// with a Copy button, drawn top-right under the cannon panel. DodgeballMatch
    /// assembles each line and pushes it via the static Log(). IMGUI — no Canvas.
    ///
    /// Example lines:
    ///   Blue 1 throws at Red 3 and hits - +1 Blue
    ///   Blue 2 throws at Red 1 and deflects and is caught by Red 1 - +1 Red
    ///   Blue 3 passes to Blue 6 and is deflected by Red 1 - +1 Blue
    ///   Blue 1 throws at Red 1 and misses - No Score
    /// </summary>
    public class DodgeballPlayByPlay : MonoBehaviour
    {
        [SerializeField] private bool show = true;
        [SerializeField] private float panelWidth = 250f;
        [SerializeField] private float panelHeight = 240f;
        [Tooltip("Top edge (px). Sits under the cannon panel (12 + ~270 + gap).")]
        [SerializeField] private float topOffset = 290f;

        private static readonly List<string> lines = new List<string>();
        private static string cached = "";
        private static bool dirty = true;

        private Vector2 scroll;
        private int shownCount;

        /// <summary>Append one play-by-play line (called by DodgeballMatch).</summary>
        public static void Log(string line)
        {
            lines.Add(line);
            if (lines.Count > 500) lines.RemoveRange(0, lines.Count - 500);
            dirty = true;
        }

        /// <summary>Clear the log (Clear button, and on a fresh match / mode switch).</summary>
        public static void Clear()
        {
            lines.Clear();
            cached = "";
            dirty = false;
        }

        private void Awake() => Clear();   // fresh log per play session

        private void OnGUI()
        {
            if (!show) return;

            float x = Screen.width - panelWidth - 12f;
            GUILayout.BeginArea(new Rect(x, topOffset, panelWidth, panelHeight), GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("== PLAY-BY-PLAY ==");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Copy", GUILayout.Width(46f))) GUIUtility.systemCopyBuffer = Text();
            if (GUILayout.Button("Clear", GUILayout.Width(46f))) Clear();
            GUILayout.EndHorizontal();

            // Snap to the newest line whenever one arrives (otherwise leave the
            // user free to scroll back through the history).
            if (lines.Count != shownCount)
            {
                shownCount = lines.Count;
                scroll.y = float.MaxValue;
            }

            scroll = GUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
            GUILayout.TextArea(Text(), GUILayout.ExpandHeight(true));
            GUILayout.EndScrollView();

            GUILayout.EndArea();
        }

        private static string Text()
        {
            if (dirty) { cached = string.Join("\n", lines); dirty = false; }
            return cached;
        }
    }
}
