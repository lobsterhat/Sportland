using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Top-right debug panel: a Mode dropdown and a Reset button. Calls back
    /// to CourtSetup (which holds the spawn list, the match, and the ball ref)
    /// to do the actual mode switch / restart. Spawned by CourtSetup; pairs
    /// with the F1-F4 / M runtime keys (either works).
    /// </summary>
    public class DodgeballMatchControls : MonoBehaviour
    {
        [SerializeField] private float panelWidth = 250f;
        [Tooltip("Top edge (px). Sits between the cannon panel and the play-by-play.")]
        [SerializeField] private float topOffset = 290f;

        private CourtSetup court;
        private string[] presetNames;
        private bool dropdownOpen;

        private void Awake()
        {
            court = GetComponent<CourtSetup>();
            presetNames = System.Enum.GetNames(typeof(GameMode.Preset));
        }

        private void OnGUI()
        {
            if (court == null) return;
            GUI.depth = -1;   // open dropdown draws on top of the play-by-play below it

            float h = dropdownOpen ? 70f + presetNames.Length * 24f : 70f;
            float x = Screen.width - panelWidth - 12f;
            GUILayout.BeginArea(new Rect(x, topOffset, panelWidth, h), GUI.skin.box);

            GUILayout.Label("== MATCH ==");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"Mode: {court.CurrentPreset} ▾"))
                dropdownOpen = !dropdownOpen;
            if (GUILayout.Button("Reset", GUILayout.Width(56f)))
                court.RestartMatch();
            GUILayout.EndHorizontal();

            if (dropdownOpen)
            {
                for (int i = 0; i < presetNames.Length; i++)
                {
                    if (GUILayout.Button(presetNames[i]))
                    {
                        court.SwitchMode((GameMode.Preset)i);
                        dropdownOpen = false;
                    }
                }
            }

            GUILayout.EndArea();
        }
    }
}
