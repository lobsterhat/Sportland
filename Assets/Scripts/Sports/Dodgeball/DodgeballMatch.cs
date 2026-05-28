using System.Collections.Generic;
using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Runtime match scorer. Reads a GameMode (the rules), consumes Ball.OnHit
    /// and Ball.OnCaught, keeps the live score + clock + eliminations, draws a
    /// scoreboard, and resolves the win.
    ///
    /// Implemented: Mode 1 (running hits), Mode 2 (count-to-out removal), and
    /// Mode 4 (sideline + catch-revive + wipeout). Mode 3 (energy) is stubbed
    /// pending the per-player energy/damage model.
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

        // Mode 2: hits taken per player. Mode 4: players benched (recallable).
        // Mode 3: current energy per player (lazily seeded from maxEnergy).
        private readonly Dictionary<PlayerZoneTracker, int> hitCounts = new Dictionary<PlayerZoneTracker, int>();
        private readonly List<PlayerZoneTracker> benched = new List<PlayerZoneTracker>();
        private readonly Dictionary<PlayerZoneTracker, float> energy = new Dictionary<PlayerZoneTracker, float>();

        private GUIStyle style;
        private Texture2D bg;

        // --- Play-by-play log (assembled here; shown by DodgeballPlayByPlay) ---
        private bool playOpen;
        private PlayerZoneTracker playThrower, playTarget, playVictim, playCatcher, playElim;
        private bool playIsThrow, playDeflected;
        private Team? playScoreTeam;
        private int playScorePoints;

        /// <summary>Assign the rules (CourtSetup passes its GameMode; null = default Mode 1).</summary>
        public void Configure(GameMode gameMode)
        {
            mode = gameMode != null ? gameMode : GameMode.CreateDefault();
            timeRemaining = mode.isTimed ? mode.secondsPerPeriod : 0f;
            scoreA = scoreB = 0;
            matchOver = false;
            winner = null;
            hitCounts.Clear();
            benched.Clear();
            energy.Clear();
            playOpen = false;
        }

        private void Awake()
        {
            if (mode == null) Configure(null);   // default to Mode 1 if not configured
        }

        private void OnDestroy()
        {
            if (ball != null && subscribed)
            {
                ball.OnHit -= OnBallHit;
                ball.OnCaught -= OnBallCaught;
                ball.OnReleased -= OnBallReleased;
                ball.OnBecameLoose -= OnBallBecameLoose;
            }
        }

        private void Update()
        {
            EnsureSubscribed();
            if (matchOver || !mode.isTimed) return;

            timeRemaining -= Time.deltaTime;
            if (timeRemaining <= 0f) { timeRemaining = 0f; EndMatch(); }
        }

        // The ball may not exist at Awake; subscribe once it's found.
        private void EnsureSubscribed()
        {
            if (subscribed) return;
            if (ball == null) ball = FindFirstObjectByType<Ball>();
            if (ball == null) return;
            ball.OnHit += OnBallHit;
            ball.OnCaught += OnBallCaught;
            ball.OnReleased += OnBallReleased;
            ball.OnBecameLoose += OnBallBecameLoose;
            subscribed = true;
        }

        // A landed hit: score for the throwing team, then apply the victim
        // outcome. Eliminations only affect infielders — the backrow is immune.
        private void OnBallHit(PlayerZoneTracker victim, Ball.HitZone zone, float ballSpeed)
        {
            if (matchOver || victim == null) return;
            var attacker = ball != null ? ball.RecentThrower : null;
            if (attacker == null || attacker.Spawn.team == victim.Spawn.team) return;  // need an opponent's hit

            if (mode.pointsPerHit != 0)
            {
                AddScore(attacker.Spawn.team, mode.pointsPerHit);
                RecordScore(attacker.Spawn.team, mode.pointsPerHit);
            }

            if (victim.Spawn.role == PlayerRole.Infielder)   // backrow can't be eliminated
            {
                switch (mode.victimOutcome)
                {
                    case VictimOutcome.CountToOut:   // Mode 2
                        hitCounts.TryGetValue(victim, out int n);
                        hitCounts[victim] = ++n;
                        if (n >= mode.hitsToOut) TakeOut(victim, permanent: true);
                        break;
                    case VictimOutcome.DamageEnergy: // Mode 3
                        if (ApplyDamage(victim, ballSpeed) <= 0f) TakeOut(victim, permanent: true);
                        break;
                    case VictimOutcome.Sideline:     // Mode 4
                        TakeOut(victim, permanent: false);
                        break;
                    case VictimOutcome.None:         // Mode 1 — hits only score.
                        break;
                }
            }

            // Play-by-play: a hit resolves the current play.
            playVictim = victim;
            playDeflected = ball != null && ball.DeflectedSinceRelease;
            FlushPlay();
        }

        // Drain the victim's energy by the ball's impact speed, softened by the
        // victim's toughness. Returns the energy remaining.
        private float ApplyDamage(PlayerZoneTracker victim, float ballSpeed)
        {
            var gen = victim.GetComponent<GeneralAttributes>();
            float maxE = gen != null ? gen.maxEnergy : 100f;
            float tough01 = gen != null ? gen.Toughness01 : 0.5f;

            if (!energy.TryGetValue(victim, out float e)) e = maxE;
            float dmg = ballSpeed * mode.damagePerSpeed * (1f - tough01 * mode.toughnessReduction);
            e -= Mathf.Max(0f, dmg);
            energy[victim] = e;
            return e;
        }

        // A caught opponent throw. Mode 4 scores + recalls the catching team's
        // bench; and in every mode that removes players, the catch also puts the
        // thrower out — the classic dodgeball rule.
        private void OnBallCaught(PlayerZoneTracker catcher)
        {
            if (matchOver || catcher == null) return;

            if (mode.catchEffect == CatchEffect.ScoreAndReviveTeam)
            {
                if (mode.pointsPerCatch != 0)
                {
                    AddScore(catcher.Spawn.team, mode.pointsPerCatch);
                    RecordScore(catcher.Spawn.team, mode.pointsPerCatch);
                }
                RecallTeam(catcher.Spawn.team);
            }

            TakeOutThrowerOnCatch(catcher);

            // Play-by-play: a catch resolves the current play.
            playCatcher = catcher;
            playDeflected = ball != null && ball.DeflectedSinceRelease;
            FlushPlay();
        }

        // Catching a THROW (an offensive attack — not an intercepted pass to a
        // teammate) puts the thrower out, the way this mode removes players: gone
        // for good in the elimination modes (CountToOut / DamageEnergy), benched
        // in the hybrid (Sideline). Mode 1 (None) has no eliminations, so a catch
        // is a turnover only. Backrow throwers are immune.
        private void TakeOutThrowerOnCatch(PlayerZoneTracker catcher)
        {
            if (ball == null || !ball.LastReleaseWasThrow) return;   // only a caught throw, not a picked pass
            var thrower = ball.RecentThrower;
            if (thrower == null || thrower.Spawn.team == catcher.Spawn.team) return;
            if (thrower.Spawn.role != PlayerRole.Infielder) return;   // backrow can't be eliminated

            switch (mode.victimOutcome)
            {
                case VictimOutcome.CountToOut:    // Mode 2 — out for good
                case VictimOutcome.DamageEnergy:  // Mode 3 — out for good
                    TakeOut(thrower, permanent: true);
                    break;
                case VictimOutcome.Sideline:      // Mode 4 — benched (recallable)
                    TakeOut(thrower, permanent: false);
                    break;
                case VictimOutcome.None:          // Mode 1 — no eliminations
                    break;
            }
        }

        // ── Play-by-play (debug log) ──

        // A throw/pass was released — open a fresh play.
        private void OnBallReleased(PlayerZoneTracker thrower, PlayerZoneTracker target, bool isThrow)
        {
            playOpen = true;
            playThrower = thrower;
            playTarget = target;
            playIsThrow = isThrow;
            playVictim = playCatcher = playElim = null;
            playDeflected = false;
            playScoreTeam = null;
            playScorePoints = 0;
        }

        // The ball settled with no hit/catch: a throw logs as a miss; an
        // uneventful pass (reached a teammate or rolled out) isn't worth a line.
        private void OnBallBecameLoose()
        {
            if (!playOpen) return;
            if (playIsThrow) FlushPlay();
            else playOpen = false;
        }

        private void RecordScore(Team team, int points)
        {
            if (!playOpen) return;
            playScoreTeam = team;
            playScorePoints = points;
        }

        // Build one line from the current play and append it to the log.
        private void FlushPlay()
        {
            if (!playOpen) return;
            playOpen = false;
            if (playThrower == null) return;

            string line = playTarget != null
                ? $"{Label(playThrower)} {(playIsThrow ? "throws at" : "passes to")} {Label(playTarget)}"
                : $"{Label(playThrower)} {(playIsThrow ? "throws" : "passes")}";

            if (playCatcher != null)
                line += playDeflected
                    ? $" and deflects and is caught by {Label(playCatcher)}"
                    : $" and is caught by {Label(playCatcher)}";
            else if (playVictim != null)
                line += playIsThrow
                    ? (playVictim == playTarget ? " and hits" : $" and hits {Label(playVictim)}")
                    : $" and is deflected by {Label(playVictim)}";
            else
                line += " and misses";

            line += " - " + PlayResult();
            DodgeballPlayByPlay.Log(line);
        }

        private string PlayResult()
        {
            string s = null;
            if (playScoreTeam.HasValue && playScorePoints != 0)
                s = $"+{playScorePoints} {ColorName(playScoreTeam.Value)}";
            if (playElim != null)
                s = s == null ? $"{Label(playElim)} out" : $"{s}, {Label(playElim)} out";
            return s ?? "No Score";
        }

        private static string ColorName(Team t) => t == Team.A ? "Blue" : "Red";
        private static string Label(PlayerZoneTracker p) => $"{ColorName(p.Spawn.team)} {p.Number}";

        // Remove a player from play. permanent = gone for good (Modes 2/3);
        // otherwise benched and recallable (Mode 4). Hands control off first if
        // the player was the one being driven, then checks for a wipeout.
        private void TakeOut(PlayerZoneTracker player, bool permanent)
        {
            if (player.GetComponent<DodgeballPlayerInput>() != null)
            {
                var heir = NearestActiveTeammate(player);
                if (heir != null) DodgeballPlayerInput.TransferControl(heir.gameObject);
            }

            player.gameObject.SetActive(false);   // OnDisable drops it from PlayerZoneTracker.All
            if (!permanent && !benched.Contains(player)) benched.Add(player);
            if (playOpen) playElim = player;   // note it for the play-by-play

            CheckWipeout();
        }

        // Mode 4 catch: bring every benched player on this team back at their spawn.
        private void RecallTeam(Team team)
        {
            for (int i = benched.Count - 1; i >= 0; i--)
            {
                var p = benched[i];
                if (p == null) { benched.RemoveAt(i); continue; }
                if (p.Spawn.team != team) continue;
                benched.RemoveAt(i);
                p.transform.position = p.Spawn.position;
                p.gameObject.SetActive(true);   // OnEnable re-adds it to PlayerZoneTracker.All
            }
        }

        private static PlayerZoneTracker NearestActiveTeammate(PlayerZoneTracker player)
        {
            PlayerZoneTracker best = null;
            float bestDistSq = float.MaxValue;
            Vector2 me = player.transform.position;
            var team = player.Spawn.team;
            var all = PlayerZoneTracker.All;
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (t == null || t == player || t.Spawn.team != team) continue;
                float d = ((Vector2)t.transform.position - me).sqrMagnitude;
                if (d < bestDistSq) { bestDistSq = d; best = t; }
            }
            return best;
        }

        private static int CountActiveInfielders(Team team)
        {
            int n = 0;
            var all = PlayerZoneTracker.All;
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (t != null && t.Spawn.team == team && t.Spawn.role == PlayerRole.Infielder) n++;
            }
            return n;
        }

        private void CheckWipeout()
        {
            if (!mode.endOnTeamWipeout) return;
            if (CountActiveInfielders(Team.A) == 0) EndMatch(Team.B);
            else if (CountActiveInfielders(Team.B) == 0) EndMatch(Team.A);
        }

        private void AddScore(Team team, int points)
        {
            if (team == Team.A) scoreA += points; else scoreB += points;
        }

        private void EndMatch(Team? forcedWinner = null)
        {
            if (matchOver) return;
            matchOver = true;
            winner = forcedWinner ?? (scoreA == scoreB ? (Team?)null : (scoreA > scoreB ? Team.A : Team.B));
        }

        private void OnGUI()
        {
            EnsureStyle();

            string time = mode.isTimed
                ? $"{Mathf.FloorToInt(timeRemaining) / 60}:{Mathf.FloorToInt(timeRemaining) % 60:00}"
                : "";
            string counts = mode.endOnTeamWipeout
                ? $"   [A:{CountActiveInfielders(Team.A)} B:{CountActiveInfielders(Team.B)}]"
                : "";
            string nrg = mode.victimOutcome == VictimOutcome.DamageEnergy ? ControlledEnergyLabel() : "";
            string modeTag = mode != null && !string.IsNullOrEmpty(mode.modeName) ? $"{mode.modeName}   " : "";
            string label = matchOver
                ? (winner.HasValue ? $"{modeTag}FINAL   A {scoreA} – {scoreB} B   ({winner} wins)"
                                   : $"{modeTag}FINAL   A {scoreA} – {scoreB} B   (tie)")
                : $"{modeTag}A {scoreA} – {scoreB} B    {time}{counts}{nrg}";

            const float w = 520f, h = 30f;
            float x = (Screen.width - w) * 0.5f;
            var r = new Rect(x, 8f, w, h);
            GUI.DrawTexture(r, bg);
            GUI.Label(r, label, style);
        }

        // Mode 3 readout: the energy of whoever the human currently controls.
        private string ControlledEnergyLabel()
        {
            var cur = DodgeballPlayerInput.Current;
            var t = cur != null ? cur.GetComponent<PlayerZoneTracker>() : null;
            if (t == null) return "";
            var gen = t.GetComponent<GeneralAttributes>();
            float maxE = gen != null ? gen.maxEnergy : 100f;
            float e = energy.TryGetValue(t, out float v) ? v : maxE;
            return $"   NRG {Mathf.CeilToInt(Mathf.Max(0f, e))}/{Mathf.CeilToInt(maxE)}";
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
