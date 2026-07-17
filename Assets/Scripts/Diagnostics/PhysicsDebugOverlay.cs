using UnityEngine;

namespace Sportland.Diagnostics
{
    /// <summary>
    /// Minimal IMGUI overlay. Backtick toggles it, type a question, Enter sends.
    /// IMGUI on purpose — zero prefab setup, zero scene wiring, and it's a dev tool.
    /// </summary>
    [RequireComponent(typeof(PhysicsDebugAssistant))]
    public class PhysicsDebugOverlay : MonoBehaviour
    {
        public KeyCode toggleKey = KeyCode.BackQuote;
        public bool pauseWhileOpen = true;

        private PhysicsDebugAssistant _assistant;
        private bool _open;
        private string _question = "";
        private Vector2 _scroll;
        private float _savedTimeScale = 1f;

        void Awake()
        {
            _assistant = GetComponent<PhysicsDebugAssistant>();
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
            // The assistant destroys itself in release builds; follow it out.
            Destroy(this);
#endif
        }

        void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                _open = !_open;

                if (pauseWhileOpen)
                {
                    if (_open)
                    {
                        _savedTimeScale = Time.timeScale;
                        Time.timeScale = 0f;
                    }
                    else
                    {
                        Time.timeScale = _savedTimeScale;
                    }
                }
            }
        }

        void OnGUI()
        {
            if (!_open || _assistant == null) return;

            const int w = 620, h = 420;
            var rect = new Rect(20, 20, w, h);
            GUI.Box(rect, "Physics Debug — what just happened?");

            GUILayout.BeginArea(new Rect(rect.x + 10, rect.y + 25, w - 20, h - 35));

            GUI.enabled = !_assistant.IsBusy;
            GUI.SetNextControlName("question");
            _question = GUILayout.TextField(_question);
            GUI.enabled = true;

            GUILayout.BeginHorizontal();
            GUI.enabled = !_assistant.IsBusy && !string.IsNullOrWhiteSpace(_question);
            if (GUILayout.Button(_assistant.IsBusy ? "Thinking..." : "Ask", GUILayout.Width(90)))
                _assistant.Ask(_question);
            GUI.enabled = true;

            if (GUILayout.Button("Clear", GUILayout.Width(70)))
            {
                _question = "";
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            _scroll = GUILayout.BeginScrollView(_scroll);
            if (_assistant.IsBusy)
            {
                GUILayout.Label("Waiting on a response...");
            }
            else if (!string.IsNullOrEmpty(_assistant.LastError))
            {
                var style = new GUIStyle(GUI.skin.label) { wordWrap = true };
                style.normal.textColor = Color.red;
                GUILayout.Label(_assistant.LastError, style);
            }
            else if (!string.IsNullOrEmpty(_assistant.LastAnswer))
            {
                var style = new GUIStyle(GUI.skin.label) { wordWrap = true };
                GUILayout.Label(_assistant.LastAnswer, style);
            }
            else
            {
                GUILayout.Label(
                    "Ask about the last few seconds of play. For example:\n\n" +
                    "  \"How fast was the ball moving when it hit the player?\"\n" +
                    "  \"Why did that hit not score a point?\"\n" +
                    "  \"Did the jump reach the height I'd expect for that launch?\"",
                    new GUIStyle(GUI.skin.label) { wordWrap = true });
            }
            GUILayout.EndScrollView();

            GUILayout.EndArea();

            // Enter to send.
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Return
                && !_assistant.IsBusy && !string.IsNullOrWhiteSpace(_question))
            {
                _assistant.Ask(_question);
                e.Use();
            }
        }
    }
}
