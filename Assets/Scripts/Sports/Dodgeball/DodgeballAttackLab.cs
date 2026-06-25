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
        private Ball.CatchFactors pendingCatch;   // catch factors at resolution (Face* modes)
        private PlayerZoneTracker attacker;
        private PlayerMovement dummyMove;
        private bool returnPending;           // defer the ball return a frame (avoid event re-entrancy)

        // Grade-cycle keys (',' throwSpeed · '.' throwAccuracy on the attacker · '/' dummy
        // catch): start at C, each press steps to the next grade UP — C→B→A→S→F→E→D→C.
        // Values are the rating midpoint of each grade in that order.
        private static readonly int[] gradeCycle = { 10, 13, 16, 19, 1, 4, 7 };  // C B A S F E D
        private int spdGradeIdx, accGradeIdx, catchGradeIdx;   // 0 = C

        [Header("Dummy start")]
        [SerializeField] private Vector2 dummyStartPos = new Vector2(4.5f, 0f);

        [Header("Auto sweep (Enter to start/stop)")]
        [SerializeField] private int throwsPerGrade = 100;
        [SerializeField] private float autoThrowInterval = 0.4f;   // gap after a throw resolves
        [SerializeField] private float autoThrowTimeout = 3f;      // watchdog if a throw never resolves
        // Sweep throw-speed F→S (ascending) so the summary reads in order.
        private static readonly int[]    sweepRatings = { 1, 4, 7, 10, 13, 16, 19 };
        private static readonly string[] sweepLabels  = { "F", "E", "D", "C", "B", "A", "S" };
        private readonly int[] sweepCaught = new int[7];
        private readonly int[] sweepBobble = new int[7];
        private readonly int[] sweepHit    = new int[7];
        private bool sweeping;
        private int sweepIdx;            // current grade (index into sweepRatings)
        private int sweepDone;           // throws resolved at the current grade
        private bool awaitingResolve;    // a throw is in flight
        private float nextAutoThrow;     // when to fire the next throw
        private float resolveDeadline;   // watchdog

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

            // T cycles the dummy mode; , / . / / step grades (C→B→A→S→F→E→D).
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.tKey.wasPressedThisFrame)
                    mode = (DummyMode)(((int)mode + 1) % 4);
                if (kb.commaKey.wasPressedThisFrame)  CycleGrade(ref spdGradeIdx,   attacker, (a, r) => a.throwSpeedRating    = r);
                if (kb.periodKey.wasPressedThisFrame) CycleGrade(ref accGradeIdx,   attacker, (a, r) => a.throwAccuracyRating = r);
                if (kb.slashKey.wasPressedThisFrame)  CycleGrade(ref catchGradeIdx, dummy,    (a, r) => a.catchTechniqueRating = r);
                if (kb.enterKey.wasPressedThisFrame)  ToggleSweep();
            }

            // Auto-sweep: fire throws on a cadence; OnSweepThrowResolved tallies + advances grade.
            if (sweeping)
            {
                if (awaitingResolve) { if (Time.time >= resolveDeadline) OnSweepThrowResolved("miss"); }   // watchdog
                else if (Time.time >= nextAutoThrow) FireAutoThrow();
            }

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
            // Keep the attacker fresh too (like the dummy) so swept/lab throws reflect
            // the rating set, not fatigue drift over a long sweep.
            var astam = attacker != null ? attacker.GetComponent<PlayerStamina>() : null;
            if (astam != null) { astam.current = 1f; astam.enabled = false; }

            PlaceDummyStart();       // park the dummy at its start pos, facing the thrower
            ApplyStartingGrades();   // attacker throw stats + dummy catch all start at C
        }

        // The grade-cycle keys start everyone at C.
        private void ApplyStartingGrades()
        {
            spdGradeIdx = accGradeIdx = catchGradeIdx = 0;
            var atk = attacker != null ? attacker.GetComponent<DodgeballAttributes>() : null;
            if (atk != null) { atk.throwSpeedRating = gradeCycle[0]; atk.throwAccuracyRating = gradeCycle[0]; }
            var dca = dummy != null ? dummy.GetComponent<DodgeballAttributes>() : null;
            if (dca != null) dca.catchTechniqueRating = gradeCycle[0];
        }

        // Advance one grade up the cycle and apply the new rating.
        private void CycleGrade(ref int idx, PlayerZoneTracker who, System.Action<DodgeballAttributes, float> apply)
        {
            if (who == null) return;
            var a = who.GetComponent<DodgeballAttributes>();
            if (a == null) return;
            idx = (idx + 1) % gradeCycle.Length;
            apply(a, gradeCycle[idx]);
        }

        // Park the dummy at its configured start position, facing the thrower.
        private void PlaceDummyStart()
        {
            if (dummy == null) return;
            var p = dummy.transform.position;
            dummy.transform.position = new Vector3(dummyStartPos.x, dummyStartPos.y, p.z);
            if (dummyMove != null) dummyMove.SetFacing(AttackerDir());
        }

        // Enter starts/stops the auto-sweep: throwsPerGrade full-charge throws at the
        // dummy for each throw-speed grade F→S, tallying caught / bobble / hit.
        private void ToggleSweep()
        {
            if (sweeping) { sweeping = false; return; }
            if (attacker == null || dummy == null || ball == null) return;
            if (mode == DummyMode.NoCatch) mode = DummyMode.FaceToward;   // a catch sweep needs the dummy catching
            System.Array.Clear(sweepCaught, 0, sweepCaught.Length);
            System.Array.Clear(sweepBobble, 0, sweepBobble.Length);
            System.Array.Clear(sweepHit,    0, sweepHit.Length);
            sweepIdx = 0;
            sweepDone = 0;
            SetThrowRating(sweepRatings[0]);
            awaitingResolve = false;
            nextAutoThrow = Time.time + 0.3f;
            sweeping = true;
        }

        private void SetThrowRating(int rating)
        {
            var a = attacker != null ? attacker.GetComponent<DodgeballAttributes>() : null;
            if (a != null) a.throwSpeedRating = rating;
        }

        // Give the attacker the ball and fire a full-charge auto-throw at the dummy.
        private void FireAutoThrow()
        {
            var input = attacker != null ? attacker.GetComponent<DodgeballPlayerInput>() : null;
            if (input == null || dummy == null || ball == null) { sweeping = false; return; }
            ball.ForcePickup(attacker);
            input.AutoThrowAt(dummy, 1f);   // full charge → the grade's nominal speed
            awaitingResolve = true;
            resolveDeadline = Time.time + autoThrowTimeout;
        }

        // A swept throw resolved — tally it and advance the grade after throwsPerGrade.
        private void OnSweepThrowResolved(string outcome)
        {
            havePending = false;            // discard this throw's pending state (covers the watchdog)
            awaitingResolve = false;
            int g = Mathf.Clamp(sweepIdx, 0, sweepRatings.Length - 1);
            if (outcome == "CAUGHT")      sweepCaught[g]++;
            else if (outcome == "BOBBLE") sweepBobble[g]++;
            else                          sweepHit[g]++;   // HIT / miss / watchdog
            sweepDone++;
            if (sweepDone >= throwsPerGrade)
            {
                sweepIdx++;
                sweepDone = 0;
                if (sweepIdx >= sweepRatings.Length) { sweeping = false; return; }
                SetThrowRating(sweepRatings[sweepIdx]);
            }
            nextAutoThrow = Time.time + autoThrowInterval;
        }

        private static void FreezeDummy(PlayerZoneTracker t)
        {
            var ai = t.GetComponent<DodgeballAI>();
            if (ai != null) ai.enabled = false;            // no movement, no catch-arming → it just takes hits
            var rb = t.GetComponent<Rigidbody2D>();
            if (rb != null) { rb.linearVelocity = Vector2.zero; rb.bodyType = RigidbodyType2D.Kinematic; }
            // Tuning target: keep it fresh so its catch reflects the RATING you set,
            // not fatigue (a frozen dummy would otherwise never recover stamina).
            var stam = t.GetComponent<PlayerStamina>();
            if (stam != null) { stam.current = 1f; stam.enabled = false; }
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
            pendingCatch = ball != null ? ball.LastCatchFactors : default;
            RecordRow($"HIT ({zone}) dmg {dmg:F1}");
        }

        // The dummy caught the user's throw (Face* mode): log the catch factors
        // and outcome — no damage.
        private void OnCaught(PlayerZoneTracker catcher)
        {
            if (!havePending || catcher != dummy) return;
            pendingCatch = ball != null ? ball.LastCatchFactors : default;
            RecordRow("CAUGHT");
        }

        private void OnLoose()
        {
            if (!havePending) return;
            pendingCatch = ball != null ? ball.LastCatchFactors : default;
            // A bobble fumbles the ball loose — got hands on it but dropped it —
            // which is distinct from a clean miss (didn't get to it).
            string outcome = pendingCatch.valid && pendingCatch.zone == Ball.CatchZone.Bobble ? "BOBBLE" : "miss";
            RecordRow(outcome);
        }

        private void RecordRow(string outcome)
        {
            var p = pending;
            string acc = p.aimError >= 0f ? $"{p.aimError:F2}u" : "n/a";
            // Catch detail at resolution: actual timing vs the clean / bobble bars.
            string face = (pendingMode != DummyMode.NoCatch && pendingCatch.valid)
                ? $"  {pendingMode} t{pendingCatch.timingScore:F2} c{pendingCatch.cleanBar:F2}/b{pendingCatch.bobbleBar:F2}"
                : "";
            log.Add($"{p.type,-10} [{p.input}]  pow {p.power,4:F0}  spd {pendingSpeed,4:F0}  acc {acc,6}{face}  {outcome}");
            while (log.Count > 60) log.RemoveAt(0);
            havePending = false;
            if (autoReturnBall && !sweeping) returnPending = true;
            if (sweeping) OnSweepThrowResolved(outcome);
        }

        // The dummy's catch breakdown — its Catch Technique grade, and while a ball
        // is in flight the live timing-vs-bars zone (deterministic preview, no AI
        // noise) — so you can dial the catch window against it.
        private void AddDummyCatchLines(List<string> lines)
        {
            if (mode == DummyMode.NoCatch || dummy == null) return;
            var ca = dummy.GetComponent<DodgeballAttributes>();
            if (ca != null)
                lines.Add($"  dummy catch: {ca.CatchTechniqueGrade}  (rating {ca.catchTechniqueRating:0}/20, eff {ca.EffectiveCatching01:F2})");
            if (ball == null || ball.CurrentState != Ball.State.Thrown) return;
            var f = ball.PreviewCatch(dummy);
            lines.Add($"  ballspd {f.ballSpeed:F0} u/s  spdT {f.speedT:F2}  facing a{f.facingAlignment:F2}{(f.backFacing ? " BACK" : "")}");
            lines.Add($"  timing {f.timingScore:F2}(exp) vs clean {f.cleanBar:F2}/bobble {f.bobbleBar:F2} -> {f.zone}");
        }

        // Auto-sweep results: per throw-grade caught / bobble / hit vs the current dummy.
        private void AddSweepLines(List<string> lines)
        {
            int reached = sweeping ? sweepIdx : -1;
            bool hasData = false;
            for (int i = 0; i < sweepRatings.Length; i++)
                if (sweepCaught[i] + sweepBobble[i] + sweepHit[i] > 0) { hasData = true; break; }
            if (!sweeping && !hasData) return;

            var ca = dummy != null ? dummy.GetComponent<DodgeballAttributes>() : null;
            string catcher = ca != null ? ca.CatchTechniqueGrade : "?";
            string status = sweeping
                ? $"running {sweepLabels[Mathf.Min(sweepIdx, 6)]} {sweepDone}/{throwsPerGrade}  [Enter stops]"
                : "done";
            lines.Add($"== SWEEP: catcher {catcher} vs throwSpd  ({status}) ==");
            lines.Add($"  grade  caught bobble  hit  (of {throwsPerGrade})");
            for (int i = 0; i < sweepRatings.Length; i++)
            {
                int total = sweepCaught[i] + sweepBobble[i] + sweepHit[i];
                if (total == 0 && i > reached) continue;   // not reached yet
                lines.Add($"   {sweepLabels[i],-5} {sweepCaught[i],5} {sweepBobble[i],6} {sweepHit[i],5}");
            }
        }

        private void OnGUI()
        {
            EnsureStyle();
            float x = Screen.width - panelWidth - 12f;
            var lines = new List<string> { "== ATTACK LAB (click = copy all) ==" };
            var atkAttr = attacker != null ? attacker.GetComponent<DodgeballAttributes>() : null;
            if (atkAttr != null)
                lines.Add($"attacker: throwSpd {atkAttr.ThrowSpeedGrade} {atkAttr.throwSpeedRating:0}   throwAcc {atkAttr.ThrowAccuracyGrade} {atkAttr.throwAccuracyRating:0} (eff {atkAttr.EffectiveThrowAccuracy01:F2})");
            lines.Add("  keys: ,=throwSpd  .=throwAcc  /=dummyCatch  Enter=sweep50   (grades C→B→A→S→F→E→D)");
            if (dummy == null) lines.Add("(no dummy — control an attacker, non-AI mode)");
            else
            {
                lines.Add($"dummy: {mode}   [T cycles · drag to move · L1 returns ball]");
                AddDummyCatchLines(lines);
                AddSweepLines(lines);
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
