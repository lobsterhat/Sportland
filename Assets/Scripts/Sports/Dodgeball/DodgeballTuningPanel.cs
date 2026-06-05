using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Left-side IMGUI tuning panel: live sliders for the most-iterated
    /// gameplay knobs. Lets us dial in lob arc, run-jump frequency, support
    /// positioning, pivot delay, etc. while in Play mode, without restarting
    /// Unity. Once we land on a feel we like, bake the values into the
    /// inspector defaults / prefab and remove or hide the panel.
    ///
    /// AI knobs are written to EVERY DodgeballAI instance every time the user
    /// drags a slider, so changes apply uniformly across all 12 players.
    /// Movement knobs do the same for PlayerMovement. Ball knobs target the
    /// single Ball.
    ///
    /// Spawned by CourtSetup when showTuningPanel is enabled.
    /// </summary>
    public class DodgeballTuningPanel : MonoBehaviour
    {
        [SerializeField] private float panelWidth = 290f;
        [SerializeField] private float topOffset = 12f;
        [SerializeField] private float bottomMargin = 12f;

        private Ball ball;
        private Vector2 scroll;
        private bool collapsed;
        private GUIStyle headerStyle;

        private void OnGUI()
        {
            if (ball == null) ball = FindFirstObjectByType<Ball>();
            EnsureStyles();

            float h = collapsed ? 36f : Screen.height - topOffset - bottomMargin;
            GUILayout.BeginArea(new Rect(12f, topOffset, panelWidth, h), GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("== TUNING ==");
            collapsed = GUILayout.Toggle(collapsed, "hide", GUILayout.Width(50f));
            GUILayout.EndHorizontal();

            if (collapsed) { GUILayout.EndArea(); return; }

            scroll = GUILayout.BeginScrollView(scroll);

            // ---------- LOBS (Ball) ----------
            Section("LOBS");
            if (ball != null)
            {
                ball.lobApex            = LabeledSlider("lobApex",            ball.lobApex,            0f, 2f);
                ball.lobApexPerUnit     = LabeledSlider("lobApexPerUnit",     ball.lobApexPerUnit,     0f, 0.5f);
                ball.lobClearanceApex   = LabeledSlider("lobClearanceApex",   ball.lobClearanceApex,   0f, 5f);
                ball.maxLobApex         = LabeledSlider("maxLobApex",         ball.maxLobApex,         0f, 6f);
                ball.lobLaneRadius      = LabeledSlider("lobLaneRadius",      ball.lobLaneRadius,      0f, 3f);
                ball.lobLateralSpeedMul = LabeledSlider("lobLateralSpeedMul", ball.lobLateralSpeedMul, 0.2f, 1.5f);
            }

            // ---------- AI OFFENSE (DodgeballAI) ----------
            Section("AI OFFENSE");
            SliderForAllAI("runJumpProbability",       0f,   1f,    ai => ai.runJumpProbability,       (ai, v) => ai.runJumpProbability       = v);
            SliderForAllAI("runJumpEdgeDistance",      0.5f, 3f,    ai => ai.runJumpEdgeDistance,      (ai, v) => ai.runJumpEdgeDistance      = v);
            SliderForAllAI("laneClearRadius",          0.5f, 4f,    ai => ai.laneClearRadius,          (ai, v) => ai.laneClearRadius          = v);
            SliderForAllAI("outfielderPassLaneRadius", 0.5f, 4f,    ai => ai.outfielderPassLaneRadius, (ai, v) => ai.outfielderPassLaneRadius = v);
            SliderForAllAI("supportForwardShift",      0f,   4f,    ai => ai.supportForwardShift,      (ai, v) => ai.supportForwardShift      = v);
            SliderForAllAI("supportRetreatShift",      0f,   5f,    ai => ai.supportRetreatShift,      (ai, v) => ai.supportRetreatShift      = v);
            ToggleForAllAI("enableOutfielderRotation",              ai => ai.enableOutfielderRotation, (ai, v) => ai.enableOutfielderRotation = v);
            SliderForAllAI("passOverThrowBias",        0f,   0.3f,  ai => ai.passOverThrowBias,        (ai, v) => ai.passOverThrowBias        = v);
            SliderForAllAI("passDistancePenalty01",    0f,   0.1f,  ai => ai.passDistancePenalty01,    (ai, v) => ai.passDistancePenalty01    = v);
            SliderForAllAI("crossRetrieveMaxDist",     2f,   15f,   ai => ai.crossRetrieveMaxDist,     (ai, v) => ai.crossRetrieveMaxDist     = v);

            // ---------- MOVEMENT (PlayerMovement) ----------
            Section("MOVEMENT");
            SliderForAllMovement("pivotDuration",      0f,   0.5f, m => m.pivotDuration,      (m, v) => m.pivotDuration      = v);
            SliderForAllMovement("pivotMinSpeed",      0f,   8f,   m => m.pivotMinSpeed,      (m, v) => m.pivotMinSpeed      = v);
            SliderForAllMovement("jumpRecoverDuration", 0f,  0.5f, m => m.jumpRecoverDuration, (m, v) => m.jumpRecoverDuration = v);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void Section(string label)
        {
            GUILayout.Space(6f);
            GUILayout.Label($"--- {label} ---", headerStyle);
        }

        // Draw a labeled slider; returns the new value. Caller assigns.
        private float LabeledSlider(string label, float value, float min, float max)
        {
            GUILayout.Label($"{label}: {value:F2}");
            return GUILayout.HorizontalSlider(value, min, max);
        }

        // Same shape, but applies the new value to every DodgeballAI instance.
        private void SliderForAllAI(string label, float min, float max,
            System.Func<DodgeballAI, float> get, System.Action<DodgeballAI, float> set)
        {
            var all = PlayerZoneTracker.All;
            if (all.Count == 0) return;
            DodgeballAI sample = null;
            for (int i = 0; i < all.Count && sample == null; i++)
                if (all[i] != null) sample = all[i].GetComponent<DodgeballAI>();
            if (sample == null) return;

            float current = get(sample);
            GUILayout.Label($"{label}: {current:F2}");
            float updated = GUILayout.HorizontalSlider(current, min, max);
            if (Mathf.Abs(updated - current) < 0.00001f) return;

            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == null) continue;
                var ai = all[i].GetComponent<DodgeballAI>();
                if (ai != null) set(ai, updated);
            }
        }

        private void ToggleForAllAI(string label,
            System.Func<DodgeballAI, bool> get, System.Action<DodgeballAI, bool> set)
        {
            var all = PlayerZoneTracker.All;
            if (all.Count == 0) return;
            DodgeballAI sample = null;
            for (int i = 0; i < all.Count && sample == null; i++)
                if (all[i] != null) sample = all[i].GetComponent<DodgeballAI>();
            if (sample == null) return;

            bool current = get(sample);
            bool updated = GUILayout.Toggle(current, " " + label);
            if (updated == current) return;

            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == null) continue;
                var ai = all[i].GetComponent<DodgeballAI>();
                if (ai != null) set(ai, updated);
            }
        }

        private void SliderForAllMovement(string label, float min, float max,
            System.Func<PlayerMovement, float> get, System.Action<PlayerMovement, float> set)
        {
            var all = PlayerZoneTracker.All;
            if (all.Count == 0) return;
            PlayerMovement sample = null;
            for (int i = 0; i < all.Count && sample == null; i++)
                if (all[i] != null) sample = all[i].GetComponent<PlayerMovement>();
            if (sample == null) return;

            float current = get(sample);
            GUILayout.Label($"{label}: {current:F2}");
            float updated = GUILayout.HorizontalSlider(current, min, max);
            if (Mathf.Abs(updated - current) < 0.00001f) return;

            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == null) continue;
                var m = all[i].GetComponent<PlayerMovement>();
                if (m != null) set(m, updated);
            }
        }

        private void EnsureStyles()
        {
            if (headerStyle != null) return;
            headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.8f, 0.9f, 1f) }
            };
        }
    }
}
