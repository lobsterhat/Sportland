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
        [SerializeField] private Vector2 anchor = new Vector2(12f, 12f);
        [SerializeField] private float panelWidth = 320f;
        [SerializeField] private int fontSize = 15;
        [SerializeField] private Color textColor = Color.white;
        [Tooltip("Translucent background drawn behind the text for legibility.")]
        [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.55f);

        private GUIStyle textStyle;
        private Texture2D bgTexture;
        private Ball cachedBall;

        private void OnGUI()
        {
            if (!showHud) return;
            EnsureStyles();

            float y = anchor.y;
            y = DrawPanel(BuildStatusLines(), anchor.x, y);
            y += 6f;
            y = DrawPanel(BuildThrowLines(), anchor.x, y);
            y += 6f;
            DrawPanel(BuildCatchLines(), anchor.x, y);
        }

        private float DrawPanel(string[] lines, float x, float y)
        {
            float lineHeight = fontSize + 4f;
            float padding = 8f;
            float height = lines.Length * lineHeight + padding * 2f;

            GUI.DrawTexture(new Rect(x, y, panelWidth, height), bgTexture);
            for (int i = 0; i < lines.Length; i++)
            {
                Rect r = new Rect(x + padding, y + padding + i * lineHeight,
                                  panelWidth - padding * 2f, lineHeight);
                GUI.Label(r, lines[i], textStyle);
            }
            return y + height;
        }

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

            return new[]
            {
                $"Player speed: {Speed(playerSpeed)}",
                $"State:        {state}",
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
            if (bgTexture != null) Destroy(bgTexture);
        }
    }
}
