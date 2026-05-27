using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Runtime match scorer. Reads a GameMode (the rules), consumes Ball.OnHit,
    /// keeps the live score + clock, draws a scoreboard, and resolves the win.
    ///
    /// First slice fully implements Mode 1 (running hits: timed, points per hit,
    /// most points wins). The other modes' victim outcomes (elimination / energy
    /// / sideline + catch revive + wipeout) are stubbed in OnBallHit for now.
    /// </summary>
    public class DodgeballMatch : MonoBehaviour
    {
        [SerializeField] private GameMode mode;

        private Ball ball;
        private bool subscribed;

        private int scoreA;
        private int scoreB;
        private float timeRemaining;
        private bool matchOver;
        private Team? winner;

        private GUIStyle style;
        private Texture2D bg;

        /// <summary>Assign the rules (CourtSetup passes its GameMode; null = default Mode 1).</summary>
        public void Configure(GameMode gameMode)
        {
            mode = gameMode != null ? gameMode : GameMode.CreateDefault();
            timeRemaining = mode.isTimed ? mode.secondsPerPeriod : 0f;
            scoreA = scoreB = 0;
            matchOver = false;
            winner = null;
        }

        private void Awake()
        {
            if (mode == null) Configure(null);   // default to Mode 1 if not configured
        }

        private void OnDestroy()
        {
            if (ball != null && subscribed) ball.OnHit -= OnBallHit;
        }

        private void Update()
        {
            EnsureSubscribed();
            if (matchOver || !mode.isTimed) return;

            timeRemaining -= Time.deltaTime;
            if (timeRemaining <= 0f)
            {
                timeRemaining = 0f;
                EndMatch();
            }
        }

        // The ball may not exist at Awake; subscribe once it's found.
        private void EnsureSubscribed()
        {
            if (subscribed) return;
            if (ball == null) ball = FindFirstObjectByType<Ball>();
            if (ball == null) return;
            ball.OnHit += OnBallHit;
            subscribed = true;
        }

        // A landed hit (carom): award points to the throwing team and apply the
        // mode's outcome to the player who got hit.
        private void OnBallHit(PlayerZoneTracker victim, HitZone zone)
        {
            if (matchOver || victim == null) return;
            var attacker = ball != null ? ball.RecentThrower : null;
            if (attacker == null || attacker.Spawn.team == victim.Spawn.team) return;  // need an opponent's hit

            if (mode.pointsPerHit != 0) AddScore(attacker.Spawn.team, mode.pointsPerHit);

            switch (mode.victimOutcome)
            {
                case VictimOutcome.None:
                    break;   // Mode 1 — hits only score; nobody leaves.
                case VictimOutcome.CountToOut:   // Mode 2
                case VictimOutcome.DamageEnergy: // Mode 3
                case VictimOutcome.Sideline:     // Mode 4
                    // TODO: per-player hit count / energy / benching, then the
                    // endOnTeamWipeout check. Needs an elimination system first.
                    break;
            }
        }

        private void AddScore(Team team, int points)
        {
            if (team == Team.A) scoreA += points; else scoreB += points;
        }

        private void EndMatch()
        {
            matchOver = true;
            winner = scoreA == scoreB ? (Team?)null : (scoreA > scoreB ? Team.A : Team.B);
        }

        private void OnGUI()
        {
            EnsureStyle();

            string time = mode.isTimed
                ? $"{Mathf.FloorToInt(timeRemaining) / 60}:{Mathf.FloorToInt(timeRemaining) % 60:00}"
                : "";
            string label = matchOver
                ? (winner.HasValue ? $"FINAL   A {scoreA} – {scoreB} B   ({winner} wins)"
                                   : $"FINAL   A {scoreA} – {scoreB} B   (tie)")
                : $"A {scoreA} – {scoreB} B    {time}";

            const float w = 380f, h = 30f;
            float x = (Screen.width - w) * 0.5f;
            var r = new Rect(x, 8f, w, h);
            GUI.DrawTexture(r, bg);
            GUI.Label(r, label, style);
        }

        private void EnsureStyle()
        {
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
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
