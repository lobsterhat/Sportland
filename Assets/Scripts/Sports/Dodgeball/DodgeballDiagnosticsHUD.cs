using System.Collections.Generic;
using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Lightweight on-screen readout for tuning. Two stacked panels:
    ///   1. Movement / ball: controlled player speed, walk/run, accel, ball
    ///      speed + height.
    ///   2. Catch math: the per-term breakdown the catch skill-check uses for
    ///      the controlled player vs the current ball, plus the last resolved
    ///      catch attempt (chance, roll, result).
    ///
    /// Uses IMGUI so it needs no Canvas / TMP / EventSystem — drop it on any
    /// GameObject and it draws to the top-left of the game view.
    /// </summary>
    public class DodgeballDiagnosticsHUD : MonoBehaviour
    {
        [SerializeField] private bool showHud = true;
        [Header("Panels (declutter: enable only what you're inspecting)")]
        [SerializeField] private bool showAIDecision = true;
        [SerializeField] private bool showUserAttack = true;
        [SerializeField] private bool showDecisionLog = true;
        [SerializeField] private bool showStatus = false;
        [SerializeField] private bool showThrow = false;
        [SerializeField] private bool showCatch = false;
        [SerializeField] private bool showAbilities = false;
        [SerializeField] private Vector2 anchor = new Vector2(12f, 12f);
        [SerializeField] private float panelWidth = 400f;
        [SerializeField] private int fontSize = 15;
        [SerializeField] private Color textColor = Color.white;
        [Tooltip("Translucent background drawn behind the text for legibility.")]
        [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.55f);
        [Tooltip("Max possession-log entries kept; oldest dropped past this.")]
        [SerializeField] private int decisionLogMax = 80;
        [Tooltip("How many of the most recent log entries the panel shows (copy gets the full log).")]
        [SerializeField] private int decisionLogShown = 16;

        private GUIStyle textStyle;
        private Texture2D bgTexture;
        private Ball cachedBall;

        // Possession log: appended on ball events; reviewable + click-to-copy.
        private readonly List<string> decisionLog = new List<string>();
        private PlayerZoneTracker lastCarrier;
        private bool subscribed;
        private float copiedFlashUntil;

        private void OnGUI()
        {
            if (!showHud) return;
            EnsureStyles();
            EnsureSubscribed();

            float y = anchor.y;
            if (showAIDecision) { y = DrawPanel(BuildAIDecisionLines(), anchor.x, y); y += 6f; }
            if (showUserAttack) { y = DrawPanel(BuildUserAttackLines(), anchor.x, y); y += 6f; }
            if (showDecisionLog) { y = DrawPanel(BuildDecisionLogLines(), anchor.x, y, string.Join("\n", decisionLog)); y += 6f; }
            if (showStatus)     { y = DrawPanel(BuildStatusLines(),     anchor.x, y); y += 6f; }
            if (showThrow)      { y = DrawPanel(BuildThrowLines(),      anchor.x, y); y += 6f; }
            if (showCatch)      { y = DrawPanel(BuildCatchLines(),      anchor.x, y); y += 6f; }
            if (showAbilities)  { y = DrawPanel(BuildAbilityLines(),    anchor.x, y); y += 6f; }

            if (Time.realtimeSinceStartup < copiedFlashUntil)
                GUI.Label(new Rect(anchor.x + 8f, y, panelWidth, fontSize + 4f), "✓ copied to clipboard", textStyle);
        }

        // Draws a panel and, on a left click anywhere inside it, copies its text
        // to the system clipboard (copyText overrides what gets copied — used by
        // the possession log to copy the FULL log, not just the shown lines).
        private float DrawPanel(string[] lines, float x, float y, string copyText = null)
        {
            float lineHeight = fontSize + 4f;
            float padding = 8f;
            float height = lines.Length * lineHeight + padding * 2f;
            var panelRect = new Rect(x, y, panelWidth, height);

            GUI.DrawTexture(panelRect, bgTexture);
            for (int i = 0; i < lines.Length; i++)
            {
                Rect r = new Rect(x + padding, y + padding + i * lineHeight,
                                  panelWidth - padding * 2f, lineHeight);
                GUI.Label(r, lines[i], textStyle);
            }

            var e = Event.current;
            if (e != null && e.type == EventType.MouseDown && e.button == 0 && panelRect.Contains(e.mousePosition))
            {
                GUIUtility.systemCopyBuffer = copyText ?? string.Join("\n", lines);
                copiedFlashUntil = Time.realtimeSinceStartup + 1.2f;
                e.Use();
            }
            return y + height;
        }

        // ── Possession log ──

        private void EnsureSubscribed()
        {
            if (subscribed) return;
            EnsureBall();
            if (cachedBall == null) return;
            cachedBall.OnAttached += OnBallAttached;
            cachedBall.OnReleased += OnBallReleased;
            cachedBall.OnBecameLoose += OnBallLoose;
            cachedBall.OnHit += OnBallHit;
            cachedBall.OnCaught += OnBallCaught;
            subscribed = true;
        }

        private void OnBallAttached(PlayerZoneTracker p)
        {
            if (p == null || p == lastCarrier) return;
            lastCarrier = p;
            LogDecision($"{Short(p)} ({p.Spawn.role}) gains");
        }

        private void OnBallReleased(PlayerZoneTracker thrower, PlayerZoneTracker target, bool isThrow)
        {
            string detail = isThrow ? "throw" : "pass";
            var ai = thrower != null ? thrower.GetComponent<DodgeballAI>() : null;
            if (ai != null && !string.IsNullOrEmpty(ai.DbgBranch)) detail = ai.DbgBranch;
            LogDecision($"  {Short(thrower)} {detail} -> {Short(target)}");
        }

        private void OnBallLoose() => LogDecision("  ball loose (miss)");

        private void OnBallHit(PlayerZoneTracker victim, Ball.HitZone zone, float speed)
            => LogDecision($"  >> HIT {Short(victim)} ({zone}, {speed:F0}u/s)");

        private void OnBallCaught(PlayerZoneTracker catcher)
            => LogDecision($"  >> {Short(catcher)} CAUGHT");

        private void LogDecision(string s)
        {
            decisionLog.Add($"{Time.timeSinceLevelLoad,6:F1}  {s}");
            int cap = Mathf.Max(1, decisionLogMax);
            while (decisionLog.Count > cap) decisionLog.RemoveAt(0);
        }

        private string[] BuildDecisionLogLines()
        {
            var lines = new List<string> { "== POSSESSION LOG (click = copy all) ==" };
            if (decisionLog.Count == 0) { lines.Add("(waiting for first possession)"); return lines.ToArray(); }
            int show = Mathf.Min(Mathf.Max(1, decisionLogShown), decisionLog.Count);
            for (int i = decisionLog.Count - show; i < decisionLog.Count; i++)
                lines.Add(decisionLog[i]);
            return lines.ToArray();
        }

        private static string Short(PlayerZoneTracker t) => t != null ? $"{t.Spawn.team}{t.Number}" : "?";

        private string[] BuildStatusLines()
        {
            EnsureBall();
            float ballSpeed  = GetBallSpeed();
            float ballHeight = cachedBall != null ? cachedBall.Height : 0f;

            var input = DodgeballPlayerInput.Current;
            if (input == null)
            {
                return new[]
                {
                    "Player: (no controller)",
                    $"Ball speed:   {Speed(ballSpeed)}",
                    $"Ball height:  {ballHeight,5:F2} u",
                };
            }

            var movement = input.GetComponent<PlayerMovement>();
            var rb       = input.GetComponent<Rigidbody2D>();

            float playerSpeed = rb != null ? rb.linearVelocity.magnitude : 0f;
            string state      = movement != null && movement.IsRunning ? "RUN" : "walk";
            string accelStr   = movement != null && movement.IsAccelerating ? "yes" : "no";
            string stanceStr  = movement != null && movement.InDefensiveStance ? "ON (set, slow)" : "off";

            return new[]
            {
                $"Player speed: {Speed(playerSpeed)}",
                $"State:        {state}",
                $"Stance:       {stanceStr}",
                $"Accelerating: {accelStr}",
                $"Ball speed:   {Speed(ballSpeed)}",
                $"Ball height:  {ballHeight,5:F2} u",
            };
        }

        private string[] BuildThrowLines()
        {
            EnsureBall();
            if (cachedBall == null)
                return new[] { "== THROW ==", "(no ball)" };

            var t = cachedBall.LastThrow;
            if (!t.releaseValid)
                return new[] { "== THROW ==", "(none yet — fire the cannon)" };

            var lines = new System.Collections.Generic.List<string>
            {
                "== THROW ==",
                $"release: {Speed(t.releaseSpeed)}",
                $"  height {t.releaseHeight:F2} u",
            };
            if (t.destValid)
            {
                lines.Add($"dest:    {Speed(t.arrivalSpeed)}");
                lines.Add($"  height {t.arrivalHeight:F2} u");
                lines.Add($"distance {t.Distance:F1} u ({DodgeballUnits.ToFeet(t.Distance):F1} ft)");
            }
            else
            {
                lines.Add("dest:    (in flight)");
            }
            return lines.ToArray();
        }

        private string[] BuildCatchLines()
        {
            EnsureBall();
            var input = DodgeballPlayerInput.Current;
            if (cachedBall == null || input == null)
                return new[] { "== CATCH ==", "(no ball / controller)" };

            var tracker = input.GetComponent<PlayerZoneTracker>();
            if (tracker == null)
                return new[] { "== CATCH ==", "(no tracker)" };

            var lines = new List<string>
            {
                $"== CATCH MATH  (ball: {cachedBall.StateLabel}) ==",
            };

            if (tracker.HasBall)
            {
                lines.Add("(holding the ball)");
            }
            else
            {
                var f = cachedBall.PreviewCatch(tracker);
                lines.Add($"catching {f.catching01:F2}    base   +{f.baseChance:F2}");
                lines.Add($"ballspd {f.ballSpeed:F1} u/s ({DodgeballUnits.ToMph(f.ballSpeed):F0}mph) -spd {f.speedPenalty:F2}");
                lines.Add($"facing  a{f.facingAlignment,5:F2}   {Signed(f.facingFactor)} fac");
                lines.Add($"stance  {(f.inStance ? "Y" : "n")}        {Signed(f.stanceFactor)} stn");
                lines.Add($"armed {(f.armed ? "Y" : "n")}  t{f.timingScore:F2}    {Signed(f.timingFactor)} time");
                lines.Add($"luck {f.luck01:F2} (roll adds 0..{f.luck01 * 0.15f:F2})");
                lines.Add($"= preview chance {Pct(f.finalChance)}  (no luck)");
            }

            lines.Add("-------- last attempt --------");
            if (cachedBall.LastCatchTime > 0f)
            {
                var lf = cachedBall.LastCatchFactors;
                float ago = Time.time - cachedBall.LastCatchTime;
                string result = cachedBall.LastCatchSucceeded ? "CAUGHT" : "MISS";
                lines.Add($"chance {Pct(lf.finalChance)}  (+luck {lf.luckContribution:F2})");
                lines.Add($"roll {cachedBall.LastCatchRoll:F2} -> {result}  ({ago:F1}s ago)");
            }
            else
            {
                lines.Add("(none yet)");
            }

            return lines.ToArray();
        }

        // Special Abilities on the controlled player: each ability's active
        // state, stacks, remaining latched time, and the per-stat multipliers
        // it is currently contributing at its live stack count.
        private string[] BuildAbilityLines()
        {
            var input = DodgeballPlayerInput.Current;
            if (input == null)
                return new[] { "== ABILITIES ==", "(no controller)" };

            var pa = input.GetComponent<PlayerAbilities>();
            if (pa == null)
                return new[] { "== ABILITIES ==", "(no PlayerAbilities on this player)" };

            var rts = pa.Runtimes;
            if (rts.Count == 0)
                return new[] { "== ABILITIES ==", "(none assigned)" };

            var lines = new List<string> { "== ABILITIES ==" };
            for (int i = 0; i < rts.Count; i++)
            {
                var rt = rts[i];
                var ab = rt.ability;
                string nm = !string.IsNullOrEmpty(ab.displayName) ? ab.displayName : ab.name;

                if (!rt.active)
                {
                    lines.Add($"{nm}  idle");
                    continue;
                }

                string dur = rt.activeUntil >= 0f
                    ? $"  ({Mathf.Max(0f, rt.activeUntil - Time.time):F1}s)"
                    : "";
                lines.Add($"{nm}  ACTIVE  stacks {rt.stacks}{dur}");

                var mods = ab.Modifiers;
                string m = "";
                for (int j = 0; j < mods.Count; j++)
                {
                    float mult = ab.StackedMultiplier(mods[j].stat, rt.stacks);
                    m += $"{mods[j].stat} x{mult:F2}  ";
                }
                if (m.Length > 0) lines.Add("  " + m.TrimEnd());
            }
            return lines.ToArray();
        }

        // Feedback on the user-controlled player's last crow-hop attack: how
        // well the throw was timed to the plant (early / late / perfect). The
        // live "crow" readout on the carrier line shows the window during the
        // hop; this shows the verdict after the release, held a few seconds.
        private string[] BuildUserAttackLines()
        {
            var lines = new List<string> { "== USER ATTACK (crow hop) ==" };
            float age = Time.realtimeSinceStartup - DodgeballPlayerInput.LastCrowFeedbackTime;
            if (string.IsNullOrEmpty(DodgeballPlayerInput.LastCrowFeedback) || age > 4f)
                lines.Add("(run + tap jump, release on the plant)");
            else
                lines.Add(DodgeballPlayerInput.LastCrowFeedback);
            return lines.ToArray();
        }

        // The ball carrier's decision process — what determined pass-vs-attack
        // and the attack dice roll. Follows whoever currently holds the ball,
        // i.e. the player actually making an offense decision right now.
        private string[] BuildAIDecisionLines()
        {
            EnsureBall();
            var lines = new List<string> { "== AI DECISION (carrier) ==" };

            var carrier = cachedBall != null ? cachedBall.Carrier : null;
            if (carrier == null)
            {
                string st = cachedBall != null ? cachedBall.StateLabel : "?";
                lines.Add($"(no carrier — ball: {st})");
                return lines.ToArray();
            }

            var sta = carrier.GetComponent<PlayerStamina>();
            var mv = carrier.GetComponent<PlayerMovement>();
            string staStr = sta != null ? $"  sta {sta.Stamina01:F2}" : "";
            string setStr = (mv != null && mv.CatchSettle01 < 0.999f) ? $"  settle {mv.CatchSettle01:F2}" : "";
            string crowStr = (mv != null && mv.IsCrowHopping) ? $"  crow {mv.CrowHopTiming01:F2}" : "";
            lines.Add($"{carrier.Spawn.team}{carrier.Number}  {carrier.Spawn.role}{staStr}{setStr}{crowStr}");

            var ai = carrier.GetComponent<DodgeballAI>();
            if (ai == null) { lines.Add("(human-controlled)"); return lines.ToArray(); }

            lines.Add($"chain:  {ai.CurrentDecision}");
            if (!string.IsNullOrEmpty(ai.DbgBranch)) lines.Add($"branch: {ai.DbgBranch}");
            if (!string.IsNullOrEmpty(ai.DbgPass))   lines.Add($"pass?   {ai.DbgPass}");
            if (!string.IsNullOrEmpty(ai.DbgTarget)) lines.Add($"target: {ai.DbgTarget}");
            if (!string.IsNullOrEmpty(ai.DbgAttack)) lines.Add($"roll:   {ai.DbgAttack}");
            return lines.ToArray();
        }

        private static string Signed(float v) => (v >= 0f ? "+" : "") + v.ToString("F2");
        private static string Pct(float v) => $"{v * 100f:F0}%";
        private static string Speed(float unitsPerSec) => $"{unitsPerSec,5:F2} u/s ({DodgeballUnits.ToMph(unitsPerSec),5:F1} mph)";

        private void EnsureBall()
        {
            if (cachedBall == null) cachedBall = FindFirstObjectByType<Ball>();
        }

        private float GetBallSpeed()
        {
            if (cachedBall == null) return 0f;
            var rb = cachedBall.GetComponent<Rigidbody2D>();
            return rb != null ? rb.linearVelocity.magnitude : 0f;
        }

        private void EnsureStyles()
        {
            if (textStyle == null || textStyle.fontSize != fontSize)
            {
                textStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = fontSize,
                    richText = false,
                };
                textStyle.normal.textColor = textColor;
            }
            if (bgTexture == null)
            {
                bgTexture = new Texture2D(1, 1);
                bgTexture.SetPixel(0, 0, backgroundColor);
                bgTexture.Apply();
                bgTexture.hideFlags = HideFlags.HideAndDontSave;
            }
        }

        private void OnDestroy()
        {
            if (cachedBall != null && subscribed)
            {
                cachedBall.OnAttached -= OnBallAttached;
                cachedBall.OnReleased -= OnBallReleased;
                cachedBall.OnBecameLoose -= OnBallLoose;
                cachedBall.OnHit -= OnBallHit;
                cachedBall.OnCaught -= OnBallCaught;
            }
            if (bgTexture != null) Destroy(bgTexture);
        }
    }
}
