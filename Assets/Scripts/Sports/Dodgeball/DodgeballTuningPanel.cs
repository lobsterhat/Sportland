using UnityEngine;
using UnityEngine.InputSystem;

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
        [Tooltip("Top edge (px). Pushed down to sit below the DodgeballDiagnosticsHUD stack at top-left.")]
        [SerializeField] private float topOffset = 420f;
        [SerializeField] private float bottomMargin = 12f;
        [Tooltip("Mouse-pick radius (world u). A click within this distance of a player's position selects that player as thrower / receiver.")]
        [SerializeField] private float pickRadius = 1.2f;
        [Tooltip("Lateral speed (u/s) of test lob passes fired from the panel — matches the AI's default passSpeed.")]
        [SerializeField] private float testLobSpeed = 12f;
        [Tooltip("Lateral speed (u/s) of test chest passes fired from the panel — matches AI's passSpeed × hardPassSpeedMul.")]
        [SerializeField] private float testChestSpeed = 19.2f;

        private Ball ball;
        private Vector2 scroll;
        private bool collapsed;
        private GUIStyle headerStyle;
        private GUIStyle markerStyle;

        // Throw-tester state.
        private PlayerZoneTracker testThrower;
        private PlayerZoneTracker testReceiver;

        private void Update()
        {
            // Mouse picks: left = thrower, right = receiver. Ignored if no
            // player is within pickRadius of the click — so clicking empty
            // space (or on the panel UI) doesn't clear the selection.
            if (Mouse.current == null || Camera.main == null) return;

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                var hit = PickPlayerAtScreen(Mouse.current.position.ReadValue());
                if (hit != null) testThrower = hit;
            }
            if (Mouse.current.rightButton.wasPressedThisFrame)
            {
                var hit = PickPlayerAtScreen(Mouse.current.position.ReadValue());
                if (hit != null) testReceiver = hit;
            }
        }

        private void OnGUI()
        {
            if (ball == null) ball = FindFirstObjectByType<Ball>();
            EnsureStyles();

            // Selection markers always drawn (even when panel is collapsed)
            // so you can still see what's selected with the panel hidden.
            DrawSelectionMarkers();

            float h = collapsed ? 36f : Screen.height - topOffset - bottomMargin;
            GUILayout.BeginArea(new Rect(12f, topOffset, panelWidth, h), GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("== TUNING ==");
            collapsed = GUILayout.Toggle(collapsed, "hide", GUILayout.Width(50f));
            GUILayout.EndHorizontal();

            if (collapsed) { GUILayout.EndArea(); return; }

            scroll = GUILayout.BeginScrollView(scroll);

            // ---------- THROW TESTER ----------
            Section("THROW TESTER (mouse: L=thrower, R=receiver)");
            GUILayout.Label("Thrower: " + LabelFor(testThrower));
            GUILayout.Label("Receiver: " + LabelFor(testReceiver));

            GUILayout.BeginHorizontal();
            bool canFire = ball != null && testThrower != null && testReceiver != null;
            GUI.enabled = canFire;
            if (GUILayout.Button("Throw Lob"))   FireTestPass(isLob: true);
            if (GUILayout.Button("Throw Chest")) FireTestPass(isLob: false);
            GUI.enabled = true;
            if (GUILayout.Button("Clear", GUILayout.Width(54f)))
            {
                testThrower = null;
                testReceiver = null;
            }
            GUILayout.EndHorizontal();

            testLobSpeed   = LabeledSlider("testLobSpeed",   testLobSpeed,   4f, 24f);
            testChestSpeed = LabeledSlider("testChestSpeed", testChestSpeed, 4f, 28f);

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
            if (headerStyle == null)
            {
                headerStyle = new GUIStyle(GUI.skin.label)
                {
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(0.8f, 0.9f, 1f) }
                };
            }
            if (markerStyle == null)
            {
                markerStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 18,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                };
            }
        }

        private string LabelFor(PlayerZoneTracker t)
        {
            if (t == null) return "[none]";
            return (t.Spawn.team == Team.A ? "A" : "B") + t.Number;
        }

        // Convert a screen-space mouse position to the nearest player in
        // world space, or null if no player is within pickRadius.
        private PlayerZoneTracker PickPlayerAtScreen(Vector2 screenPos)
        {
            var cam = Camera.main;
            if (cam == null) return null;
            // Push the z so ScreenToWorldPoint lands on the play-plane (z=0).
            var screen3 = new Vector3(screenPos.x, screenPos.y, -cam.transform.position.z);
            Vector2 worldPos = cam.ScreenToWorldPoint(screen3);

            PlayerZoneTracker best = null;
            float bestDistSq = pickRadius * pickRadius;
            var all = PlayerZoneTracker.All;
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (t == null) continue;
                float d = ((Vector2)t.transform.position - worldPos).sqrMagnitude;
                if (d < bestDistSq) { bestDistSq = d; best = t; }
            }
            return best;
        }

        // Force the ball into the thrower's hand and immediately fire the
        // test pass at the receiver. Repeatable: the selected pair persists
        // across button presses so you can hammer the same pass while tuning.
        private void FireTestPass(bool isLob)
        {
            if (ball == null || testThrower == null || testReceiver == null) return;
            ball.ForcePickup(testThrower);
            ball.IntendedTarget = testReceiver;
            float speed = isLob ? testLobSpeed : testChestSpeed;
            ball.Pass(testReceiver.transform.position, speed, isLob);
        }

        // Floating "[T]" and "[R]" markers over the currently selected
        // thrower and receiver. Always drawn (even when the panel is hidden)
        // so you can confirm the selection at a glance.
        private void DrawSelectionMarkers()
        {
            var cam = Camera.main;
            if (cam == null) return;
            if (testThrower != null) DrawMarker(cam, testThrower, "[T]", new Color(1f, 0.85f, 0.3f));
            if (testReceiver != null) DrawMarker(cam, testReceiver, "[R]", new Color(0.4f, 1f, 0.5f));
        }

        private void DrawMarker(Camera cam, PlayerZoneTracker t, string text, Color color)
        {
            Vector3 sp = cam.WorldToScreenPoint(t.transform.position + Vector3.up * 1.8f);
            if (sp.z <= 0f) return;
            var rect = new Rect(sp.x - 18f, Screen.height - sp.y - 14f, 36f, 26f);
            // Shadow + colored text for legibility over any sprite/background.
            var prev = markerStyle.normal.textColor;
            markerStyle.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, markerStyle);
            markerStyle.normal.textColor = color;
            GUI.Label(rect, text, markerStyle);
            markerStyle.normal.textColor = prev;
        }
    }
}
