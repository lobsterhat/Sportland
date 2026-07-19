using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Sportland.Diagnostics
{
    /// <summary>
    /// Minimal IMGUI overlay. F8 toggles it, type a question, Enter sends. While
    /// closed it shows a small "Physics Debug: F8" tag bottom-left so you can
    /// confirm the tool actually spawned. IMGUI on purpose — zero prefab setup,
    /// zero scene wiring, and it's a dev tool.
    ///
    /// While the console is open it sets <see cref="CapturingInput"/>; gameplay
    /// input scripts check that and skip their key handling, so typing a question
    /// doesn't also drive the game (e.g. Q toggling the view).
    ///
    /// Answers are selectable (click-drag + Ctrl+C) and there's a Copy button;
    /// Prev/Next walk this session's question+answer history.
    ///
    /// Toggle is read through the new Input System (Keyboard.current), same as
    /// the rest of the dodgeball code — the legacy UnityEngine.Input class is
    /// silent when the project's Active Input Handling is set to the new system.
    /// </summary>
    [RequireComponent(typeof(PhysicsDebugAssistant))]
    public class PhysicsDebugOverlay : MonoBehaviour
    {
        /// <summary>True while the console is open. Gameplay input scripts read
        /// this and skip their hotkeys so typing doesn't leak into the game.</summary>
        public static bool CapturingInput { get; private set; }

        public bool pauseWhileOpen = true;

        private struct Entry { public string question; public string response; public bool isError; }

        private PhysicsDebugAssistant _assistant;
        private bool _open;
        private string _question = "";
        private Vector2 _scroll;
        private float _savedTimeScale = 1f;

        private readonly List<Entry> _history = new List<Entry>();
        private int _viewIndex = -1;      // which history entry is shown (-1 = none yet)
        private string _pendingQuestion;  // question currently in flight
        private bool _wasBusy;

        void Awake()
        {
            _assistant = GetComponent<PhysicsDebugAssistant>();
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
            // The assistant destroys itself in release builds; follow it out.
            Destroy(this);
#endif
        }

        void OnDisable()
        {
            // Never leave the game paused or input blocked if we vanish while open.
            if (_open && pauseWhileOpen) Time.timeScale = _savedTimeScale;
            _open = false;
            CapturingInput = false;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f8Key.wasPressedThisFrame)
                SetOpen(!_open);

            // Capture each request's result into history the moment it lands.
            bool busy = _assistant.IsBusy;
            if (_wasBusy && !busy)
            {
                bool isError = !string.IsNullOrEmpty(_assistant.LastError);
                _history.Add(new Entry
                {
                    question = _pendingQuestion,
                    response = isError ? _assistant.LastError : _assistant.LastAnswer,
                    isError = isError,
                });
                _viewIndex = _history.Count - 1;
            }
            _wasBusy = busy;
        }

        private void SetOpen(bool open)
        {
            if (_open == open) return;
            _open = open;
            CapturingInput = open;
            if (pauseWhileOpen)
            {
                if (open) { _savedTimeScale = Time.timeScale; Time.timeScale = 0f; }
                else Time.timeScale = _savedTimeScale;
            }
        }

        private void Send()
        {
            if (_assistant.IsBusy || string.IsNullOrWhiteSpace(_question)) return;
            _pendingQuestion = _question;
            _assistant.Ask(_question);
        }

        void OnGUI()
        {
            if (_assistant == null) return;

            // Always-visible presence hint when the panel is closed: if you can
            // see this, the tool spawned and is listening. Bottom-left so it
            // clears the controlled-player nameplate at bottom-centre.
            if (!_open)
            {
                var hint = new GUIStyle(GUI.skin.box) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
                GUI.Label(new Rect(10, Screen.height - 26, 150, 20), "Physics Debug: F8", hint);
                return;
            }

            const int w = 620, h = 460;
            var rect = new Rect(20, 20, w, h);
            GUI.Box(rect, "Physics Debug — what just happened?");

            GUILayout.BeginArea(new Rect(rect.x + 10, rect.y + 25, w - 20, h - 35));

            // ── Question field + Ask/Clear ──────────────────────────────
            GUI.enabled = !_assistant.IsBusy;
            GUI.SetNextControlName("question");
            _question = GUILayout.TextField(_question);
            GUI.enabled = true;

            GUILayout.BeginHorizontal();
            GUI.enabled = !_assistant.IsBusy && !string.IsNullOrWhiteSpace(_question);
            if (GUILayout.Button(_assistant.IsBusy ? "Thinking..." : "Ask", GUILayout.Width(90)))
                Send();
            GUI.enabled = true;
            if (GUILayout.Button("Clear", GUILayout.Width(70)))
                _question = "";
            GUILayout.EndHorizontal();

            // ── History navigation (Prev / Next / Reuse / Copy) ─────────
            if (_history.Count > 0)
            {
                GUILayout.Space(4);
                GUILayout.BeginHorizontal();

                GUI.enabled = _viewIndex > 0;
                if (GUILayout.Button("◀ Prev", GUILayout.Width(70))) _viewIndex--;
                GUI.enabled = _viewIndex < _history.Count - 1;
                if (GUILayout.Button("Next ▶", GUILayout.Width(70))) _viewIndex++;
                GUI.enabled = true;

                GUILayout.Label($"{_viewIndex + 1} / {_history.Count}", GUILayout.Width(60));

                if (GUILayout.Button("Reuse Q", GUILayout.Width(75)))
                    _question = _history[_viewIndex].question;
                if (GUILayout.Button("Copy", GUILayout.Width(60)))
                    GUIUtility.systemCopyBuffer = _history[_viewIndex].response;

                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8);

            // ── Response area ───────────────────────────────────────────
            _scroll = GUILayout.BeginScrollView(_scroll);
            if (_assistant.IsBusy)
            {
                GUILayout.Label("Waiting on a response...");
            }
            else if (_history.Count > 0 && _viewIndex >= 0)
            {
                var entry = _history[_viewIndex];

                var qStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, fontStyle = FontStyle.Bold };
                GUILayout.Label($"Q: {entry.question}", qStyle);
                GUILayout.Space(4);

                // TextArea (not Label) so the answer is selectable and Ctrl+C works.
                // We ignore the returned string, keeping it effectively read-only.
                // Explicit content height so it scrolls cleanly inside the view.
                var aStyle = new GUIStyle(GUI.skin.textArea) { wordWrap = true, richText = false };
                if (entry.isError) aStyle.normal.textColor = Color.red;
                float aHeight = aStyle.CalcHeight(new GUIContent(entry.response), w - 40);
                GUILayout.TextArea(entry.response, aStyle, GUILayout.Height(aHeight));
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

            // Enter sends only while the question field has focus — so selecting
            // text in the answer area doesn't re-fire the request.
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Return
                && GUI.GetNameOfFocusedControl() == "question" && !_assistant.IsBusy
                && !string.IsNullOrWhiteSpace(_question))
            {
                Send();
                e.Use();
            }
        }
    }
}
