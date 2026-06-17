using System.Collections.Generic;
using UnityEngine;

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
        [SerializeField] private float panelWidth = 460f;
        [SerializeField] private int rowsShown = 14;
        [SerializeField] private int fontSize = 14;

        private Ball ball;
        private PlayerZoneTracker dummy;
        private bool subscribed;

        private readonly List<string> log = new List<string>();
        private DodgeballPlayerInput.UserAttackInfo pending;
        private float pendingSpeed;
        private bool havePending;

        private GUIStyle style;
        private Texture2D bg;
        private float copiedUntil;

        private void Start()
        {
            ball = FindAnyObjectByType<Ball>();
            SetUpDummy();
            EnsureSubscribed();
        }

        private void OnDestroy()
        {
            if (ball != null && subscribed)
            {
                ball.OnReleased -= OnReleased;
                ball.OnHit -= OnHit;
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
            pendingSpeed = (ball != null && ball.LastThrow.releaseValid) ? ball.LastThrow.releaseSpeed : 0f;
            havePending = true;
        }

        private void OnHit(PlayerZoneTracker victim, Ball.HitZone zone, float ballSpeed)
        {
            if (!havePending) return;
            float tough = victim != null && victim.TryGetComponent<GeneralAttributes>(out var g) ? g.Toughness01 : 0.5f;
            float dmg = ballSpeed * damagePerSpeed * (1f - tough * toughnessReduction);
            RecordRow(victim == dummy, $"HIT ({zone}) dmg {dmg:F1}");
        }

        private void OnLoose()
        {
            if (havePending) RecordRow(false, "miss");
        }

        private void RecordRow(bool hitDummy, string outcome)
        {
            var p = pending;
            string acc = p.aimError >= 0f ? $"{p.aimError:F2}u" : "n/a";
            log.Add($"{p.type,-10} [{p.input}]  pow {p.power,4:F0}  spd {pendingSpeed,4:F0}  acc {acc,6}  {(hitDummy ? outcome : "miss")}");
            while (log.Count > 60) log.RemoveAt(0);
            havePending = false;
        }

        private void OnGUI()
        {
            EnsureStyle();
            float x = Screen.width - panelWidth - 12f;
            var lines = new List<string> { "== ATTACK LAB (click = copy all) ==" };
            if (dummy == null) lines.Add("(no dummy — control an attacker, non-AI mode)");
            else if (log.Count == 0) lines.Add("(attack the dummy to log throws)");
            else
            {
                int show = Mathf.Min(rowsShown, log.Count);
                for (int i = log.Count - show; i < log.Count; i++) lines.Add(log[i]);
            }

            float lh = fontSize + 4f, pad = 8f;
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
