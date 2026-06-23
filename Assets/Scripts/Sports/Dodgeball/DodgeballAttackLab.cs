using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Attack tuning lab. Replaces the opposing team with a single stationary
    /// "dummy" (everyone else — both teams except the controlled attacker — is
    /// disabled) and logs each of the user's attacks: input execution, attack
    /// type, throw power (pre-momentum) + speed (with momentum), accuracy (aim
    /// error vs the target), and the damage it would deal. For fine-tuning
    /// abilities — control an attacker, pelt the dummy, read the numbers.
    ///
    /// Added by CourtSetup when attackLabMode is on. Run in a non-elimination
    /// mode (Running Hits) so the dummy persists; the damage number is computed
    /// from the Energy-mode formula regardless of the active mode.
    /// </summary>
    public class DodgeballAttackLab : MonoBehaviour
    {
        [Header("Damage model (mirrors GameMode Energy mode)")]
        [Tooltip("Energy lost per unit of ball impact speed, before toughness reduction.")]
        public float damagePerSpeed = 1.5f;
        [Range(0f, 1f)] public float toughnessReduction = 0.5f;

        [Header("Panel")]
        [SerializeField] private float panelWidth = 680f;
        [SerializeField] private int rowsShown = 14;
        [SerializeField] private int fontSize = 14;

        [Header("Dummy drag")]
        [Tooltip("Click within this distance (u) of the dummy to grab it, then drag with the mouse to reposition.")]
        [SerializeField] private float dragPickRadius = 1.5f;

        [Header("Dummy facing / catch (T cycles)")]
        [Tooltip("NoCatch = the dummy just takes hits (pure damage tuning). The Face* modes make it TRY to catch, oriented toward / partially off / away from the attacker — to tune the facing-based catch penalty and the damage when it fails. Cycle live with T.")]
        [SerializeField] private DummyMode mode = DummyMode.NoCatch;
        [Tooltip("Off-angle (deg) for FacePartial — within the side (half-penalty) facing zone.")]
        [SerializeField] private float partialAngle = 65f;
        [Tooltip("Distance (u) at which the dummy arms a catch on an incoming thrown ball (Face* modes).")]
        [SerializeField] private float catchArmRange = 6f;
        [Tooltip("After an attack resolves (caught / hit / miss), automatically hand the ball back to the attacker. " +
                 "Off by default — it was snatching the ball back mid-carom before it settled. Press L1 to return " +
                 "it manually when you're ready for the next rep.")]
        [SerializeField] private bool autoReturnBall = false;

        public enum DummyMode { NoCatch, FaceToward, FacePartial, FaceAway }

        private bool dragging;

        private Ball ball;
        private PlayerZoneTracker dummy;
        private bool subscribed;

        private readonly List<string> log = new List<string>();
        private DodgeballPlayerInput.UserAttackInfo pending;
        private float pendingSpeed;
        private bool havePending;
        private DummyMode pendingMode;        // dummy mode at the time of the pending throw
        private float pendingCatchChance;     // catch chance at resolution (Face* modes)
        private PlayerZoneTracker attacker;
        private PlayerMovement dummyMove;
        private bool returnPending;           // defer the ball return a frame (avoid event re-entrancy)

        private GUIStyle style;
        private Texture2D bg;
        private float copiedUntil;

        private void Start()
        {
            ball = FindAnyObjectByType<Ball>();
            SetUpDummy();
            EnsureSubscribed();
        }

        // Grab the dummy by clicking near it and drag with the mouse to
        // reposition (clamped to the play area). Lets you test attacks from
        // different ranges/angles without restarting.
        private void Update()
        {
            if (dummy == null) return;

            // Deferred ball return (flagged in RecordRow when autoReturnBall is on)
            // — done here, not inside the ball's hit/catch callback, to avoid
            // re-entrancy.
            if (returnPending && attacker != null && ball != null)
            {
                ball.ForcePickup(attacker);
                returnPending = false;
            }

            // L1 returns the ball to the attacker for the next rep — manual, so a
            // hit's carom is allowed to finish and the ball settle before it's
            // snatched back.
            var pad = Gamepad.current;
            if (pad != null && pad.leftShoulder.wasPressedThisFrame && attacker != null && ball != null)
                ball.ForcePickup(attacker);

            // T cycles the dummy mode.
            var kb = Keyboard.current;
            if (kb != null && kb.tKey.wasPressedThisFrame)
                mode = (DummyMode)(((int)mode + 1) % 4);

            // Catching modes: orient the dummy per the mode (relative to the
            // attacker) and arm a catch on an incoming thrown ball so the
            // facing-penalized skill-check actually runs.
            if (mode != DummyMode.NoCatch && dummyMove != null)
            {
                Vector2 toAtk = AttackerDir();
                Vector2 face = mode == DummyMode.FaceToward ? toAtk
                             : mode == DummyMode.FaceAway ? -toAtk
                             : (Vector2)(Quaternion.Euler(0f, 0f, partialAngle) * toAtk);
                dummyMove.SetFacing(face);

                if (ball != null && ball.CurrentState == Ball.State.Thrown
                    && Vector2.Distance(dummy.transform.position, ball.transform.position) < catchArmRange)
                    dummy.ArmCatch();
            }

            var mouse = Mouse.current;
            var cam = Camera.main;
            if (mouse == null || cam == null) return;

            Vector3 sp = mouse.position.ReadValue();
            sp.z = -cam.transform.position.z;               // distance to the z=0 play plane
            Vector2 wp = cam.ScreenToWorldPoint(sp);

            if (mouse.leftButton.wasPressedThisFrame
                && Vector2.Distance(wp, dummy.transform.position) <= dragPickRadius)
                dragging = true;
            if (mouse.leftButton.wasReleasedThisFrame)
                dragging = false;

            if (dragging)
            {
                float x = Mathf.Clamp(wp.x, -CourtSetup.PlayAreaHalfWidth, CourtSetup.PlayAreaHalfWidth);
                float y = Mathf.Clamp(wp.y, -CourtSetup.PlayAreaHalfHeight, CourtSetup.PlayAreaHalfHeight);
                var p = dummy.transform.position;
                dummy.transform.position = new Vector3(x, y, p.z);
            }
        }

        private Vector2 AttackerDir()
        {
            var a = attacker;
            if (a == null && DodgeballPlayerInput.Current != null)
                a = DodgeballPlayerInput.Current.GetComponent<PlayerZoneTracker>();
            if (a == null) return Vector2.left;
            Vector2 d = (Vector2)a.transform.position - (Vector2)dummy.transform.position;
            return d.sqrMagnitude > 0.0001f ? d.normalized : Vector2.left;
        }

        private void OnDestroy()
        {
            if (ball != null && subscribed)
            {
                ball.OnReleased -= OnReleased;
                ball.OnHit -= OnHit;
                ball.OnCaught -= OnCaught;
                ball.OnBecameLoose -= OnLoose;
            }
            if (bg != null) Destroy(bg);
        }

        // Keep the controlled attacker + one opposing infielder (frozen); disable
        // everyone else so the only thing on the court to hit is the dummy.
        private void SetUpDummy()
        {
            var current = DodgeballPlayerInput.Current;
            PlayerZoneTracker controlled = current != null ? current.GetComponent<PlayerZoneTracker>() : null;
            attacker = controlled;
            Team userTeam = controlled != null ? controlled.Spawn.team : Team.A;
            Team oppTeam = userTeam == Team.A ? Team.B : Team.A;

            var all = new List<PlayerZoneTracker>(PlayerZoneTracker.All);
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (t == null || t == controlled) continue;
                if (dummy == null && t.Spawn.team == oppTeam && t.Spawn.role == PlayerRole.Infielder)
                {
                    dummy = t;
                    dummyMove = t.GetComponent<PlayerMovement>();
                    FreezeDummy(t);
                }
                else
                {
                    t.gameObject.SetActive(false);
                }
            }
        }

        private static void FreezeDummy(PlayerZoneTracker t)
        {
            var ai = t.GetComponent<DodgeballAI>();
            if (ai != null) ai.enabled = false;            // no movement, no catch-arming → it just takes hits
            var rb = t.GetComponent<Rigidbody2D>();
            if (rb != null) { rb.linearVelocity = Vector2.zero; rb.bodyType = RigidbodyType2D.Kinematic; }
        }

        private void EnsureSubscribed()
        {
            if (subscribed || ball == null) return;
            ball.OnReleased += OnReleased;
            ball.OnHit += OnHit;
            ball.OnCaught += OnCaught;
            ball.OnBecameLoose += OnLoose;
            subscribed = true;
        }

        // The user threw: latch the attack telemetry + the actual release speed.
        private void OnReleased(PlayerZoneTracker thrower, PlayerZoneTracker target, bool isThrow)
        {
            if (!isThrow) return;
            if (thrower == null || thrower.GetComponent<DodgeballPlayerInput>() == null) return;  // user attacks only
            var a = DodgeballPlayerInput.LastUserAttack;
            if (!a.valid) return;
            pending = a;
            pendingMode = mode;
            pendingSpeed = (ball != null && ball.LastThrow.releaseValid) ? ball.LastThrow.releaseSpeed : 0f;
            havePending = true;
        }

        private void OnHit(PlayerZoneTracker victim, Ball.HitZone zone, float ballSpeed)
        {
            if (!havePending) return;
            float tough = victim != null && victim.TryGetComponent<GeneralAttributes>(out var g) ? g.Toughness01 : 0.5f;
            float dmg = ballSpeed * damagePerSpeed * (1f - tough * toughnessReduction);
            pendingCatchChance = (pendingMode != DummyMode.NoCatch && ball != null) ? ball.LastCatchFactors.finalChance : -1f;
            RecordRow($"HIT ({zone}) dmg {dmg:F1}");
        }

        // The dummy caught the user's throw (Face* mode): log the facing-based
        // catch chance and outcome — no damage.
        private void OnCaught(PlayerZoneTracker catcher)
        {
            if (!havePending || catcher != dummy) return;
            pendingCatchChance = ball != null ? ball.LastCatchFactors.finalChance : 0f;
            RecordRow("CAUGHT");
        }

        private void OnLoose()
        {
            if (havePending) RecordRow("miss");
        }

        private void RecordRow(string outcome)
        {
            var p = pending;
            string acc = p.aimError >= 0f ? $"{p.aimError:F2}u" : "n/a";
            string face = pendingMode != DummyMode.NoCatch
                ? $"  {pendingMode} catch {Mathf.Max(0f, pendingCatchChance) * 100f:F0}%"
                : "";
            log.Add($"{p.type,-10} [{p.input}]  pow {p.power,4:F0}  spd {pendingSpeed,4:F0}  acc {acc,6}{face}  {outcome}");
            while (log.Count > 60) log.RemoveAt(0);
            havePending = false;
            if (autoReturnBall) returnPending = true;
        }

        // The dummy's catch breakdown — its Catch Technique grade, and while a ball
        // is in flight the live timing-vs-bars zone (deterministic preview, no AI
        // noise) — so you can dial the catch window against it.
        private void AddDummyCatchLines(List<string> lines)
        {
            if (mode == DummyMode.NoCatch || dummy == null) return;
            var ca = dummy.GetComponent<DodgeballAttributes>();
            if (ca != null)
                lines.Add($"  dummy catch: {ca.CatchTechniqueGrade}  (rating {ca.catchTechniqueRating:0}/20)");
            if (ball == null || ball.CurrentState != Ball.State.Thrown) return;
            var f = ball.PreviewCatch(dummy);
            lines.Add($"  ballspd {f.ballSpeed:F0} u/s  spdT {f.speedT:F2}  facing a{f.facingAlignment:F2}{(f.backFacing ? " BACK" : "")}");
            lines.Add($"  timing {f.timingScore:F2}(exp) vs clean {f.cleanBar:F2}/bobble {f.bobbleBar:F2} -> {f.zone}");
        }

        private void OnGUI()
        {
            EnsureStyle();
            float x = Screen.width - panelWidth - 12f;
            var lines = new List<string> { "== ATTACK LAB (click = copy all) ==" };
            var atkAttr = attacker != null ? attacker.GetComponent<DodgeballAttributes>() : null;
            if (atkAttr != null)
                lines.Add($"attacker throw spd: {atkAttr.ThrowSpeedGrade}  (rating {atkAttr.throwSpeedRating:0}/20)");
            if (dummy == null) lines.Add("(no dummy — control an attacker, non-AI mode)");
            else
            {
                lines.Add($"dummy: {mode}   [T cycles · drag to move · L1 returns ball]");
                AddDummyCatchLines(lines);
                if (log.Count == 0)
                {
                    lines.Add("(attack the dummy to log throws)");
                }
                else
                {
                    int show = Mathf.Min(rowsShown, log.Count);
                    for (int i = log.Count - show; i < log.Count; i++) lines.Add(log[i]);
                }
            }

            float lh = fontSize + 12f, pad = 8f;
            float h = lines.Count * lh + pad * 2f;
            var rect = new Rect(x, 12f, panelWidth, h);
            GUI.DrawTexture(rect, bg);
            for (int i = 0; i < lines.Count; i++)
                GUI.Label(new Rect(x + pad, 12f + pad + i * lh, panelWidth - pad * 2f, lh), lines[i], style);

            var e = Event.current;
            if (e != null && e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                GUIUtility.systemCopyBuffer = string.Join("\n", log);
                copiedUntil = Time.realtimeSinceStartup + 1.2f;
                e.Use();
            }
            if (Time.realtimeSinceStartup < copiedUntil)
                GUI.Label(new Rect(x + pad, 12f + h, panelWidth, lh), "✓ copied", style);
        }

        private void EnsureStyle()
        {
            if (style == null || style.fontSize != fontSize)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = fontSize, richText = false };
                style.normal.textColor = Color.white;
            }
            style.font = DodgeballUI.Font;   // BoldPixels when assigned in CourtSetup; null = built-in font
            style.wordWrap = false;          // keep each log row on one line (no clipped wrap)
            if (bg == null)
            {
                bg = new Texture2D(1, 1);
                bg.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.6f));
                bg.Apply();
                bg.hideFlags = HideFlags.HideAndDontSave;
            }
        }
    }
}
