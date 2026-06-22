using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Which debug "skill" is active. Each skill exposes a focused set of
    /// controls and freezes the players it needs to.
    /// </summary>
    public enum DebugSkill
    {
        None,
        CatchCannon,    // catch reactions — driven by the existing cannon panel
        PassTesting,    // pass mechanics (lobs / chest) between picked pair
        ThrowTesting,   // throw mechanics + per-thrower stats + movement
    }

    /// <summary>
    /// Left-side debug panel. Top: skill dropdown + global "Disable timers" /
    /// "Disable opposing team" toggles. Below: a skill-specific section with
    /// the controls needed for that flavor of testing.
    ///
    /// PASS / THROW TESTING: left-click a player = thrower (yellow [T]
    /// marker), right-click a player = receiver/target ([R] marker). While
    /// the panel is in either tester mode, the picked pair has AI frozen so
    /// they stay where you put them. Left stick moves the thrower, right
    /// stick moves the receiver. L1 = ball→thrower, R2 = ball→receiver
    /// (Pass Testing only — throw mechanics put the ball in the thrower's
    /// hand automatically when you click "Throw at Target").
    ///
    /// DISABLE TIMERS: short-circuits the shot clock, delay-of-game, carrier-
    /// return-grace, opposing-infield rules, and pickup lockout via the
    /// static TimersDisabled flag. Lets a scenario run indefinitely.
    ///
    /// DISABLE OPPOSING TEAM: freezes every player on the team OPPOSITE the
    /// selected thrower (their AI disabled, they stay put). Use to remove
    /// defensive interference while tuning offense.
    /// </summary>
    public class DodgeballTuningPanel : MonoBehaviour
    {
        // ── Layout ─────────────────────────────────────────────────────
        [SerializeField] private float panelWidth = 290f;
        [Tooltip("Top edge (px). Pushed down to sit below the DodgeballDiagnosticsHUD stack at top-left.")]
        [SerializeField] private float topOffset = 420f;
        [SerializeField] private float bottomMargin = 12f;
        [Tooltip("Mouse-pick radius (world u). A click within this distance of a player's position selects them.")]
        [SerializeField] private float pickRadius = 1.2f;
        [SerializeField] private float testLobSpeed = 12f;
        [SerializeField] private float testChestSpeed = 19.2f;

        // ── Internal state ─────────────────────────────────────────────
        private Ball ball;
        private Vector2 scroll;
        private bool collapsed;
        private GUIStyle headerStyle;
        private GUIStyle markerStyle;

        private DebugSkill skill = DebugSkill.None;
        private bool skillDropdownOpen;
        private string[] skillNames;

        private bool disableTimers;
        private bool disableOpposingTeam;
        private bool soloPairOnly;
        private bool soloModeActive;                                              // tracks whether players are currently hidden
        private readonly List<GameObject> currentlyHidden = new List<GameObject>(); // who we've SetActive(false)'d

        private PlayerZoneTracker testThrower;
        private PlayerZoneTracker testReceiver;   // receiver for pass, target for throw

        // AI freeze tracking. ApplyFreeze keeps each "current frozen" reference
        // in sync with the desired one so mid-mode selection changes correctly
        // restore the previous player's AI before disabling the new one's.
        private PlayerZoneTracker frozenThrower;
        private PlayerZoneTracker frozenReceiver;
        private readonly List<PlayerZoneTracker> frozenOpposingTeam = new List<PlayerZoneTracker>();

        // ── Statics consulted by other systems ────────────────────────
        /// <summary>True while a tester skill (Pass/Throw) is active. Other debug systems (cannon right-stick) consult this to release gamepad sticks.</summary>
        public static bool DebugModeActive { get; private set; }
        /// <summary>True when "Disable timers" is on AND a debug skill is active. Match.TickClocks and PlayerZoneTracker timer paths early-out on this.</summary>
        public static bool TimersDisabled { get; private set; }

        private void Awake()
        {
            skillNames = System.Enum.GetNames(typeof(DebugSkill));
        }

        // ──────────────────────────────────────────────────────────────
        // Per-frame: mouse picks, freeze management, gamepad shortcuts.
        // ──────────────────────────────────────────────────────────────
        private void Update()
        {
            // Mouse picks: left=thrower, right=receiver. Empty-space clicks
            // (no player within pickRadius) don't change selection.
            if (Camera.main != null && Mouse.current != null)
            {
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

            bool inTesterMode = skill == DebugSkill.PassTesting || skill == DebugSkill.ThrowTesting;

            // Freeze the picked pair while in a tester skill.
            ApplyFreeze(ref frozenThrower,  inTesterMode ? testThrower  : null);
            ApplyFreeze(ref frozenReceiver, inTesterMode ? testReceiver : null);

            // Freeze the opposing team if the toggle is on (tester modes only).
            ApplyOpposingTeamFreeze(inTesterMode && disableOpposingTeam);

            // Hide everyone except the thrower / receiver if "Solo pair only"
            // is on. SetActive(false) drops them from PlayerZoneTracker.All,
            // so other systems naturally ignore them while hidden.
            ApplySoloMode(inTesterMode);

            // Publish statics for other systems.
            DebugModeActive = inTesterMode;
            TimersDisabled  = disableTimers && skill != DebugSkill.None;

            if (!inTesterMode) return;

            // Gamepad: ball returns + stick movement.
            var pad = Gamepad.current;
            if (pad == null) return;

            if (pad.leftShoulder.wasPressedThisFrame && testThrower != null && ball != null)
                ball.ForcePickup(testThrower);
            if (skill == DebugSkill.PassTesting && pad.rightTrigger.wasPressedThisFrame
                && testReceiver != null && ball != null)
                ball.ForcePickup(testReceiver);

            if (frozenThrower != null)
            {
                var m = frozenThrower.GetComponent<PlayerMovement>();
                if (m != null) m.ApplyMove(pad.leftStick.ReadValue());
            }
            if (frozenReceiver != null)
            {
                var m = frozenReceiver.GetComponent<PlayerMovement>();
                if (m != null) m.ApplyMove(pad.rightStick.ReadValue());
            }
        }

        // ──────────────────────────────────────────────────────────────
        // IMGUI panel.
        // ──────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            if (ball == null) ball = FindFirstObjectByType<Ball>();
            EnsureStyles();
            DrawSelectionMarkers();

            float h = collapsed ? 36f : Screen.height - topOffset - bottomMargin;
            GUILayout.BeginArea(new Rect(12f, topOffset, panelWidth, h), GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("== DEBUG ==");
            collapsed = GUILayout.Toggle(collapsed, "hide", GUILayout.Width(50f));
            GUILayout.EndHorizontal();

            if (collapsed) { GUILayout.EndArea(); return; }

            scroll = GUILayout.BeginScrollView(scroll);

            DrawSkillSelector();

            GUILayout.Space(4f);
            disableTimers       = GUILayout.Toggle(disableTimers,       " Disable timers");
            disableOpposingTeam = GUILayout.Toggle(disableOpposingTeam, " Disable opposing team");
            soloPairOnly        = GUILayout.Toggle(soloPairOnly,        " Solo pair only (hide other 10)");

            GUILayout.Space(8f);

            switch (skill)
            {
                case DebugSkill.CatchCannon:  DrawCatchCannonSection();  break;
                case DebugSkill.PassTesting:  DrawPassTestingSection();  break;
                case DebugSkill.ThrowTesting: DrawThrowTestingSection(); break;
                default:
                    GUILayout.Label("Pick a debug skill ▾");
                    break;
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawSkillSelector()
        {
            if (GUILayout.Button($"Skill: {skill} ▾"))
                skillDropdownOpen = !skillDropdownOpen;

            if (skillDropdownOpen)
            {
                for (int i = 0; i < skillNames.Length; i++)
                {
                    if (GUILayout.Button(skillNames[i]))
                    {
                        skill = (DebugSkill)i;
                        skillDropdownOpen = false;
                    }
                }
            }
        }

        // ── Skill sections ────────────────────────────────────────────

        private void DrawCatchCannonSection()
        {
            Section("CATCH CANNON");
            GUILayout.Label("Drive the cannon panel on the right.");
            GUILayout.Label("Right stick moves the cannon.");
            GUILayout.Label("R1 / C fires.");
            GUILayout.Label("Auto-fire and trajectory in the cannon panel.");
            GUILayout.Space(4f);
            GUILayout.Label("(\"Disable timers\" and \"Disable opposing");
            GUILayout.Label(" team\" don't apply to this skill.)");
        }

        private void DrawPassTestingSection()
        {
            Section("PASS TESTING");
            GUILayout.Label("Mouse: L=thrower  R=receiver");
            GUILayout.Label("L stick: move thrower   R stick: move receiver");
            GUILayout.Label("L1: ball→thrower   R2: ball→receiver");
            GUILayout.Space(4f);
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

            // Lob mechanics.
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
        }

        private void DrawThrowTestingSection()
        {
            Section("THROW TESTING");
            GUILayout.Label("Mouse: L=thrower  R=target");
            GUILayout.Label("L stick: move thrower   R stick: move target");
            GUILayout.Label("L1: ball→thrower");
            GUILayout.Space(4f);
            GUILayout.Label("Thrower: " + LabelFor(testThrower));
            GUILayout.Label("Target: " + LabelFor(testReceiver));

            GUILayout.BeginHorizontal();
            bool canFire = ball != null && testThrower != null && testReceiver != null;
            GUI.enabled = canFire;
            if (GUILayout.Button("Throw at Target")) FireTestThrow();
            GUI.enabled = true;
            if (GUILayout.Button("Clear", GUILayout.Width(54f)))
            {
                testThrower = null;
                testReceiver = null;
            }
            GUILayout.EndHorizontal();

            // Thrower stats (per-player; writes to the picked thrower's
            // DodgeballAttributes directly so the change applies to their
            // future throws — including from the "Throw at Target" button
            // and from any AI-driven throws once AI resumes).
            Section("THROWER STATS");
            if (testThrower != null)
            {
                var attr = testThrower.GetComponent<DodgeballAttributes>();
                if (attr != null)
                {
                    attr.throwSpeedRating     = LabeledSlider($"throwSpeedRating [{attr.ThrowSpeedGrade}]", attr.throwSpeedRating, 0f, 20f);
                    attr.throwAccuracyRating  = LabeledSlider($"throwAccuracyRating [{attr.ThrowAccuracyGrade}]", attr.throwAccuracyRating, 0f, 20f);
                    attr.anticipation         = LabeledSlider("anticipation", attr.anticipation, 0f, 100f);
                    attr.catchTechniqueRating = LabeledSlider($"catchTechniqueRating [{attr.CatchTechniqueGrade}]", attr.catchTechniqueRating, 0f, 20f);
                }
            }
            else
            {
                GUILayout.Label("(pick a thrower to edit stats)");
            }

            // Attacks (applies to all AI). attackChance × teamAggressionMul ×
            // player aggression decides how often a carrier commits to a moving
            // attack; the weights split it among running / jump / run-jump.
            Section("ATTACKS");
            SliderForAllAI("attackChance",        0f,   1f,  ai => ai.attackChance,          (ai, v) => ai.attackChance          = v);
            SliderForAllAI("teamAggressionMul",   0.2f, 2f,  ai => ai.teamAggressionMul,     (ai, v) => ai.teamAggressionMul     = v);
            SliderForAllAI("runningWeight",       0f,   2f,  ai => ai.runningWeight,         (ai, v) => ai.runningWeight         = v);
            SliderForAllAI("jumpWeight",          0f,   2f,  ai => ai.jumpWeight,            (ai, v) => ai.jumpWeight            = v);
            SliderForAllAI("runJumpWeight",       0f,   2f,  ai => ai.runJumpWeight,         (ai, v) => ai.runJumpWeight         = v);
            SliderForAllAI("runningReleaseDist",  0.5f, 3f,  ai => ai.runningReleaseDistance,(ai, v) => ai.runningReleaseDistance = v);
            SliderForAllAI("runJumpEdgeDistance", 0.5f, 3f,  ai => ai.runJumpEdgeDistance,   (ai, v) => ai.runJumpEdgeDistance   = v);

            // Movement (jump / pivot / landing recovery).
            Section("MOVEMENT");
            SliderForAllMovement("pivotDuration",       0f, 0.5f, m => m.pivotDuration,       (m, v) => m.pivotDuration       = v);
            SliderForAllMovement("pivotMinSpeed",       0f, 8f,   m => m.pivotMinSpeed,       (m, v) => m.pivotMinSpeed       = v);
            SliderForAllMovement("jumpRecoverDuration", 0f, 0.5f, m => m.jumpRecoverDuration, (m, v) => m.jumpRecoverDuration = v);
        }

        // ── Test-fire helpers ─────────────────────────────────────────

        private void FireTestPass(bool isLob)
        {
            if (ball == null || testThrower == null || testReceiver == null) return;
            ball.ForcePickup(testThrower);
            ball.IntendedTarget = testReceiver;
            float speed = isLob ? testLobSpeed : testChestSpeed;
            ball.Pass(testReceiver.transform.position, speed, isLob);
        }

        // Mirror of DodgeballAI.ThrowAtTarget: lead + accuracy scatter using
        // the thrower's stats, so what you see here matches what their AI
        // would do once it resumes (modulo the controlled release).
        private void FireTestThrow()
        {
            if (ball == null || testThrower == null || testReceiver == null) return;
            ball.ForcePickup(testThrower);
            ball.IntendedTarget = testReceiver;

            var attr = testThrower.GetComponent<DodgeballAttributes>();
            float power = ball.ReleaseSpeed(attr != null ? attr.ThrowSpeed01 : 0.6f);
            float anticipation = attr != null ? attr.Anticipation01 : 0f;

            var targetRb = testReceiver.GetComponent<Rigidbody2D>();
            Vector2 targetVel = targetRb != null ? targetRb.linearVelocity : Vector2.zero;
            Vector2 aim = ball.LeadAim(transform.position == Vector3.zero
                ? (Vector2)testThrower.transform.position
                : (Vector2)testThrower.transform.position,
                testReceiver.transform.position, targetVel, power, anticipation);

            if (attr != null)
            {
                float acc01 = attr.ThrowAccuracy01;
                float dist  = Vector2.Distance(testThrower.transform.position, aim);
                float maxError = (1f - acc01) * 0.15f * dist;
                aim += UnityEngine.Random.insideUnitCircle * maxError;
            }

            ball.ThrowAt(aim, power);
        }

        // ── Freeze helpers ────────────────────────────────────────────

        private void ApplyFreeze(ref PlayerZoneTracker current, PlayerZoneTracker desired)
        {
            if (current == desired) return;
            if (current != null)
            {
                var ai = current.GetComponent<DodgeballAI>();
                if (ai != null) ai.enabled = true;
            }
            if (desired != null)
            {
                var ai = desired.GetComponent<DodgeballAI>();
                if (ai != null) ai.enabled = false;
            }
            current = desired;
        }

        private void ApplyOpposingTeamFreeze(bool shouldFreeze)
        {
            Team opposingTeam = Team.A;
            bool haveTarget = shouldFreeze && testThrower != null;
            if (haveTarget)
                opposingTeam = testThrower.Spawn.team == Team.A ? Team.B : Team.A;

            // Restore anyone who should no longer be frozen.
            for (int i = frozenOpposingTeam.Count - 1; i >= 0; i--)
            {
                var t = frozenOpposingTeam[i];
                bool keep = haveTarget && t != null && t.Spawn.team == opposingTeam;
                if (!keep)
                {
                    if (t != null)
                    {
                        var ai = t.GetComponent<DodgeballAI>();
                        if (ai != null) ai.enabled = true;
                    }
                    frozenOpposingTeam.RemoveAt(i);
                }
            }

            if (!haveTarget) return;

            // Freeze anyone on the opposing team who isn't already frozen.
            var all = PlayerZoneTracker.All;
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (t == null || t.Spawn.team != opposingTeam) continue;
                if (frozenOpposingTeam.Contains(t)) continue;
                var ai = t.GetComponent<DodgeballAI>();
                if (ai != null) ai.enabled = false;
                frozenOpposingTeam.Add(t);
            }
        }

        // Hide every player except the picked pair while soloPairOnly is on.
        // Re-activates them when toggled off, when selection changes, or when
        // the skill leaves tester mode. SetActive(false) auto-unregisters the
        // tracker from PlayerZoneTracker.All, so other systems see only the
        // pair while hidden — TryTakeBall, AI iteration, etc.
        private void ApplySoloMode(bool inTesterMode)
        {
            bool desiredActive = inTesterMode && soloPairOnly && testThrower != null;

            // Detect a pair change that needs re-application (the new pair
            // might be in our currently-hidden list).
            bool pairChanged = false;
            if (desiredActive && soloModeActive)
            {
                if (testThrower  != null && currentlyHidden.Contains(testThrower.gameObject))  pairChanged = true;
                if (testReceiver != null && currentlyHidden.Contains(testReceiver.gameObject)) pairChanged = true;
            }

            if (desiredActive == soloModeActive && !pairChanged) return;   // no-op

            // Restore everyone we previously hid.
            for (int i = 0; i < currentlyHidden.Count; i++)
            {
                var go = currentlyHidden[i];
                if (go != null) go.SetActive(true);
            }
            currentlyHidden.Clear();

            if (!desiredActive)
            {
                soloModeActive = false;
                return;
            }

            // Hide everyone except the picked pair. Snapshot the registry
            // because SetActive(false) mutates it via OnDisable.
            var snapshot = new List<PlayerZoneTracker>(PlayerZoneTracker.All);
            for (int i = 0; i < snapshot.Count; i++)
            {
                var t = snapshot[i];
                if (t == null) continue;
                if (t == testThrower || t == testReceiver) continue;
                var go = t.gameObject;
                currentlyHidden.Add(go);
                go.SetActive(false);
            }
            soloModeActive = true;
        }

        private void OnDisable()
        {
            ApplyFreeze(ref frozenThrower,  null);
            ApplyFreeze(ref frozenReceiver, null);
            ApplyOpposingTeamFreeze(false);
            // Bring back any solo-hidden players.
            for (int i = 0; i < currentlyHidden.Count; i++)
            {
                var go = currentlyHidden[i];
                if (go != null) go.SetActive(true);
            }
            currentlyHidden.Clear();
            soloModeActive = false;
            DebugModeActive = false;
            TimersDisabled  = false;
        }

        // ── Misc helpers ──────────────────────────────────────────────

        private void Section(string label)
        {
            GUILayout.Space(6f);
            GUILayout.Label($"--- {label} ---", headerStyle);
        }

        private float LabeledSlider(string label, float value, float min, float max)
        {
            GUILayout.Label($"{label}: {value:F2}");
            return GUILayout.HorizontalSlider(value, min, max);
        }

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

        private string LabelFor(PlayerZoneTracker t)
        {
            if (t == null) return "[none]";
            return (t.Spawn.team == Team.A ? "A" : "B") + t.Number;
        }

        private PlayerZoneTracker PickPlayerAtScreen(Vector2 screenPos)
        {
            var cam = Camera.main;
            if (cam == null) return null;
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

        private void DrawSelectionMarkers()
        {
            var cam = Camera.main;
            if (cam == null) return;
            if (testThrower  != null) DrawMarker(cam, testThrower,  "[T]", new Color(1f, 0.85f, 0.3f));
            if (testReceiver != null) DrawMarker(cam, testReceiver, "[R]", new Color(0.4f, 1f, 0.5f));
        }

        private void DrawMarker(Camera cam, PlayerZoneTracker t, string text, Color color)
        {
            Vector3 sp = cam.WorldToScreenPoint(t.transform.position + Vector3.up * 1.8f);
            if (sp.z <= 0f) return;
            var rect = new Rect(sp.x - 18f, Screen.height - sp.y - 14f, 36f, 26f);
            var prev = markerStyle.normal.textColor;
            markerStyle.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, markerStyle);
            markerStyle.normal.textColor = color;
            GUI.Label(rect, text, markerStyle);
            markerStyle.normal.textColor = prev;
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
    }
}
