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

        [Tooltip("Debug toggle: when enabled, every non-carrier auto-faces the ball each frame. Overrides whatever the AI or input chose for facing. Useful for visual clarity / debugging.")]
        [SerializeField] private bool playersFaceBallWhenEmpty;

        /// <summary>Live toggle: when true, non-carriers auto-face the ball every frame (via LateUpdate override).</summary>
        public bool PlayersFaceBallWhenEmpty { get => playersFaceBallWhenEmpty; set => playersFaceBallWhenEmpty = value; }

        [Header("Shot clock / delay of game")]
        [Tooltip("Seconds the carrier may hold the ball before they're forced to drop it and their team takes a point penalty.")]
        [SerializeField] private float shotClockSeconds = 8f;
        [Tooltip("Seconds a loose ball may sit before the team in whose half it's resting takes a point penalty (the clock then re-arms while the ball stays loose).")]
        [SerializeField] private float delayClockSeconds = 5f;
        [Tooltip("Point penalty applied to the offending team when either clock expires.")]
        [SerializeField] private int clockExpiryPenalty = 1;

        // Live shot clock state — keyed off the OFFENSIVE TEAM, not a single
        // carrier. Continues through teammate passes / pickups; the carrier
        // reference is just a UI hint for which jersey to render the countdown
        // under (null when the ball's in flight or loose with no holder).
        private Team? shotClockTeam;
        private PlayerZoneTracker shotClockCarrier;
        private float shotClockExpiresAt = -1f;
        // Live delay-of-game state: when the loose-ball alarm fires (-1 = not running).
        private float delayClockExpiresAt = -1f;

        /// <summary>Tunable shot-clock period (s).</summary>
        public float ShotClockSeconds { get => shotClockSeconds; set => shotClockSeconds = Mathf.Max(1f, value); }
        /// <summary>Tunable delay-of-game period (s).</summary>
        public float DelayClockSeconds { get => delayClockSeconds; set => delayClockSeconds = Mathf.Max(1f, value); }
        /// <summary>The offensive team currently on the shot clock (null when no clock).</summary>
        public Team? ShotClockTeam => shotClockTeam;
        /// <summary>The carrier currently visible on the clocked team (null mid-flight / when ball is loose).</summary>
        public PlayerZoneTracker ShotClockCarrier => shotClockCarrier;
        /// <summary>Seconds remaining on the shot clock; 0 when not running.</summary>
        public float ShotClockRemaining => shotClockExpiresAt < 0f ? 0f : Mathf.Max(0f, shotClockExpiresAt - Time.time);
        /// <summary>Seconds remaining on the delay-of-game clock; 0 when not running.</summary>
        public float DelayClockRemaining => delayClockExpiresAt < 0f ? 0f : Mathf.Max(0f, delayClockExpiresAt - Time.time);
        /// <summary>True if any team's shot clock is currently running.</summary>
        public bool ShotClockRunning => shotClockTeam.HasValue && shotClockExpiresAt >= 0f;
        /// <summary>True if the loose-ball delay clock is currently running.</summary>
        public bool DelayClockRunning => delayClockExpiresAt >= 0f && ball != null && ball.CurrentState == Ball.State.Loose;

        private Ball ball;
        private bool subscribed;

        private int scoreA;
        private int scoreB;
        private float timeRemaining;
        private bool matchOver;
        private Team? winner;

        /// <summary>Live time remaining (seconds). Settable so the match-controls slider can extend/shorten the current period.</summary>
        public float TimeRemaining { get => timeRemaining; set => timeRemaining = Mathf.Max(0f, value); }
        /// <summary>True if the active mode runs a clock.</summary>
        public bool IsTimed => mode != null && mode.isTimed;

        // Mode 2: hits taken per player. Mode 4: players benched (recallable).
        // Mode 3: current energy per player (lazily seeded from maxEnergy).
        private readonly Dictionary<PlayerZoneTracker, int> hitCounts = new Dictionary<PlayerZoneTracker, int>();
        private readonly List<PlayerZoneTracker> benched = new List<PlayerZoneTracker>();
        private readonly Dictionary<PlayerZoneTracker, float> energy = new Dictionary<PlayerZoneTracker, float>();

        private GUIStyle style;
        private Texture2D bg;

        // --- Play-by-play log (assembled here; shown by DodgeballPlayByPlay) ---
        private bool playOpen;
        private PlayerZoneTracker playThrower, playTarget, playVictim, playCatcher, playElim, playDeflector;
        private bool playIsThrow, playCatchWasDive, playPassCompleted, playViolationTouch;
        private Ball.DodgeKind playDodge;
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
            StopShotClock();
            StopDelayClock();
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
                ball.OnAttached -= OnBallAttached;
                ball.OnViolationTouch -= OnBallViolationTouch;
            }
            PlayerZoneTracker.OnAnyForcedDrop -= OnPlayerForcedDrop;
            PlayerZoneTracker.OnAnyTurnover   -= OnPlayerTurnover;
        }

        private void Update()
        {
            EnsureSubscribed();
            TickClocks();
            if (matchOver || !mode.isTimed) return;

            timeRemaining -= Time.deltaTime;
            if (timeRemaining <= 0f) { timeRemaining = 0f; EndMatch(); }
        }

        // Drive the shot clock + delay-of-game timers each frame; fire their
        // penalties when expired. Subscribers to Ball events start/stop the
        // clocks at the right transitions (Attached → start shot, Released /
        // BecameLoose → stop shot, BecameLoose → start delay).
        private void TickClocks()
        {
            if (matchOver) return;
            if (DodgeballTuningPanel.TimersDisabled) return;   // debug skill bypass

            if (shotClockExpiresAt >= 0f && Time.time >= shotClockExpiresAt)
                FireShotClockExpiry();

            if (delayClockExpiresAt >= 0f && Time.time >= delayClockExpiresAt)
                FireDelayClockExpiry();
        }

        // Begin a fresh shot clock for a team. Called when possession swings
        // from no-clock / from the other team to this carrier's team. Same-
        // team possession continuation goes through UpdateShotClockCarrier
        // instead (no reset).
        private void StartShotClock(PlayerZoneTracker carrier)
        {
            if (carrier == null) { StopShotClock(); return; }
            shotClockTeam = carrier.Spawn.team;
            shotClockCarrier = carrier;
            shotClockExpiresAt = Time.time + shotClockSeconds;
        }

        // Same-team possession continuation: keep the clock ticking, just
        // update which jersey the countdown shows under.
        private void UpdateShotClockCarrier(PlayerZoneTracker carrier)
        {
            shotClockCarrier = carrier;
        }

        private void StopShotClock()
        {
            shotClockTeam = null;
            shotClockCarrier = null;
            shotClockExpiresAt = -1f;
        }

        private void StartDelayClock()
        {
            // Turnover-only modes don't penalize on a loose ball, so there's
            // nothing for this clock to do — leave it disarmed.
            if (mode != null && mode.clockExpiryEffect == ClockExpiryEffect.TurnoverOnly) return;
            delayClockExpiresAt = Time.time + delayClockSeconds;
        }

        private void StopDelayClock()
        {
            delayClockExpiresAt = -1f;
        }

        // Shot clock expired: the offensive team stalled. PointPenalty modes
        // deduct points from the team; TurnoverOnly modes just force the
        // drop. If a current carrier exists (clock fired while they were
        // holding it) force-drop the ball regardless of mode — the stall
        // ended, possession swings. Mid-flight / loose at expiry has no
        // carrier to drop.
        private void FireShotClockExpiry()
        {
            var team = shotClockTeam;
            var carrier = shotClockCarrier;
            StopShotClock();
            if (!team.HasValue) return;
            bool penalize = mode == null || mode.clockExpiryEffect == ClockExpiryEffect.PointPenalty;
            string who = carrier != null ? Label(carrier) : $"team {TeamLetter(team.Value)}";
            if (penalize)
            {
                AddScore(team.Value, -clockExpiryPenalty);
                DodgeballPlayByPlay.Log($"{who} shot clock expired - -{clockExpiryPenalty} {TeamLetter(team.Value)} team");
            }
            else
            {
                DodgeballPlayByPlay.Log($"{who} shot clock expired - turnover");
            }
            if (carrier != null && carrier.HeldBall != null) carrier.HeldBall.Drop();
        }

        // Delay-of-game expired: the ball sat loose too long. PointPenalty
        // modes penalize the team in whose half it's resting (team A = left
        // half, x < 0; team B = right) and re-arm the clock so the penalty
        // keeps ticking until somebody picks the ball up. TurnoverOnly modes
        // disable this clock in StartDelayClock — this branch is unreachable
        // there, but bail safely if it does fire.
        private void FireDelayClockExpiry()
        {
            if (ball == null || ball.CurrentState != Ball.State.Loose)
            {
                StopDelayClock();
                return;
            }
            if (mode != null && mode.clockExpiryEffect == ClockExpiryEffect.TurnoverOnly)
            {
                StopDelayClock();
                return;
            }

            float ballX = ball.transform.position.x;
            Team offendingTeam = ballX < 0f ? Team.A : Team.B;
            AddScore(offendingTeam, -clockExpiryPenalty);
            DodgeballPlayByPlay.Log($"Loose ball sat too long in {TeamLetter(offendingTeam)} territory - -{clockExpiryPenalty} {TeamLetter(offendingTeam)} team");
            delayClockExpiresAt = Time.time + delayClockSeconds;   // re-arm
        }

        // Optional debug override: each frame after gameplay-side Updates,
        // re-point every non-carrier's facing at the ball. Runs in LateUpdate
        // so it wins regardless of script execution order. Whoever's holding
        // the ball is left alone (their facing belongs to their offense logic
        // / aim input).
        private void LateUpdate()
        {
            if (!playersFaceBallWhenEmpty || ball == null) return;
            Vector2 ballPos = ball.transform.position;
            var all = PlayerZoneTracker.All;
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (t == null || t.HasBall) continue;
                var mv = t.GetComponent<PlayerMovement>();
                if (mv != null) mv.SetFacing(ballPos - (Vector2)t.transform.position);
            }
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
            ball.OnAttached += OnBallAttached;
            ball.OnViolationTouch += OnBallViolationTouch;
            PlayerZoneTracker.OnAnyForcedDrop += OnPlayerForcedDrop;
            PlayerZoneTracker.OnAnyTurnover   += OnPlayerTurnover;
            subscribed = true;
        }

        // A landed hit: score for the throwing team, then apply the victim
        // outcome. Outfielders are normally immune to elimination/damage; that
        // immunity drops while they're grounded inside the opposing infield
        // (Phase C — the risk of venturing in for a loose ball). A throw whose
        // release came from inside the opposing infield is wholly neutered
        // (Phase D): no points, no elimination, no damage, no sideline — just
        // a play-by-play entry.
        private void OnBallHit(PlayerZoneTracker victim, Ball.HitZone zone, float ballSpeed)
        {
            if (matchOver || victim == null) return;
            var attacker = ball != null ? ball.RecentThrower : null;
            if (attacker == null || attacker.Spawn.team == victim.Spawn.team) return;  // need an opponent's hit

            bool neutered = ball != null && ball.LastReleaseFromOpposingInfield;

            if (!neutered)
            {
                if (mode.pointsPerHit != 0)
                {
                    AddScore(attacker.Spawn.team, mode.pointsPerHit);
                    RecordScore(attacker.Spawn.team, mode.pointsPerHit);
                }

                // Shot clock: a hit landing on an opposing player who is
                // physically inside their own infield counts as a "successful
                // attack" → stop the offensive team's clock. (Hits on a victim
                // who's wandered out of their infield don't stop it.)
                if (shotClockTeam.HasValue && shotClockTeam.Value == attacker.Spawn.team)
                {
                    Team oppOfClocked = shotClockTeam.Value == Team.A ? Team.B : Team.A;
                    if (ZoneFactory.InfieldFor(oppOfClocked).Contains(victim.transform.position))
                        StopShotClock();
                }

                bool outfielderInOppInfield = victim.Spawn.role == PlayerRole.Outfielder
                                           && victim.IsInOpposingInfield
                                           && victim.IsGrounded;
                bool vulnerable = victim.Spawn.role == PlayerRole.Infielder || outfielderInOppInfield;

                if (vulnerable)
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
            }

            // Play-by-play: a hit resolves the current play.
            playVictim = victim;
            playDeflector = ball != null ? ball.LastDeflector : null;
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
            playDeflector = ball != null ? ball.LastDeflector : null;
            var catcherMove = catcher.GetComponent<PlayerMovement>();
            playCatchWasDive = catcherMove != null && catcherMove.IsDiving;
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
            // Note: shot clock keeps ticking — release doesn't stop it. The
            // clock stops only on a successful attack (hit in opp infield /
            // ground in opp infield) or when the other team gains possession.
            shotClockCarrier = null;   // ball is in flight; no carrier to render under
            playOpen = true;
            playThrower = thrower;
            playTarget = target;
            playIsThrow = isThrow;
            playVictim = playCatcher = playElim = playDeflector = null;
            playCatchWasDive = false;
            playPassCompleted = false;
            playViolationTouch = false;
            playDodge = Ball.DodgeKind.None;
            playScoreTeam = null;
            playScorePoints = 0;
        }

        // The ball settled with no hit/catch: a throw logs as a miss; an
        // uneventful pass (reached a teammate or rolled out) isn't worth a line.
        private void OnBallBecameLoose()
        {
            shotClockCarrier = null;   // no current holder
            // Stop the shot clock ONLY if the ball landed in the clocked team's
            // opposing infield — the "shot reached opp territory" condition.
            // Anything else (loose in own area, neutral strip, etc.) leaves the
            // clock running so the offense feels the pressure.
            if (shotClockTeam.HasValue && ball != null)
            {
                Team oppOfClocked = shotClockTeam.Value == Team.A ? Team.B : Team.A;
                if (ZoneFactory.InfieldFor(oppOfClocked).Contains(ball.transform.position))
                    StopShotClock();
            }
            StartDelayClock();    // ball is on the ground; start the loose-ball alarm
            if (!playOpen) return;
            if (playIsThrow && ball != null) playDodge = ball.LastTargetDodge;
            FlushPlay();   // throw → miss; a pass that reached no one → incomplete
        }

        // A pass completing to a teammate. (Throws and opponent catches resolve
        // via OnHit / OnCaught; OnAttached fires before OnCaught, so an opponent
        // catch attaches to the other team here and is skipped.)
        private void OnBallAttached(PlayerZoneTracker player)
        {
            // Shot clock: same-team possession continuation (teammate pass /
            // pickup) keeps the existing clock running with no reset. Possession
            // swing to the OTHER team stops the old clock and starts a fresh
            // one on the new team. Cancels the loose-ball alarm either way.
            if (player != null)
            {
                if (shotClockTeam.HasValue && shotClockTeam.Value == player.Spawn.team)
                    UpdateShotClockCarrier(player);
                else
                    StartShotClock(player);
                StopDelayClock();
            }

            if (!playOpen || playIsThrow || player == null) return;
            if (playThrower == null || player.Spawn.team != playThrower.Spawn.team) return;
            playPassCompleted = true;
            FlushPlay();
        }

        // A player touched the ball while in the opposing team's half — +1 to
        // the opposing team (whether it was a "catch" or a loose-ball pickup).
        // Folds into the current play if there is one; otherwise logs standalone.
        private void OnBallViolationTouch(PlayerZoneTracker player)
        {
            if (matchOver || player == null) return;
            var oppTeam = player.Spawn.team == Team.A ? Team.B : Team.A;
            AddScore(oppTeam, 1);

            if (playOpen)
            {
                RecordScore(oppTeam, 1);
                playCatcher = player;
                playViolationTouch = true;
                FlushPlay();
            }
            else
            {
                DodgeballPlayByPlay.Log($"{Label(player)} touched ball on opposing side - +1 {TeamLetter(oppTeam)} team");
            }
        }

        // A carrier was forced to drop the ball (return window expired).
        // PointPenalty modes: -1 to their team. TurnoverOnly modes: ball is
        // already loose by the time this fires; no score change. Play stays
        // open in either case.
        private void OnPlayerForcedDrop(PlayerZoneTracker player)
        {
            if (matchOver || player == null) return;
            if (mode != null && mode.clockExpiryEffect == ClockExpiryEffect.TurnoverOnly)
            {
                DodgeballPlayByPlay.Log($"{Label(player)} didn't return in time, dropped ball - turnover");
                return;
            }
            AddScore(player.Spawn.team, -1);
            DodgeballPlayByPlay.Log($"{Label(player)} didn't return in time, dropped ball - -1 {TeamLetter(player.Spawn.team)} team");
        }

        // An outfielder + ball + opposing infield triggered the turnover rule
        // (entry-with-ball, or 2 s expiry with the ball). Ball is now loose; no
        // points change hands. Just record the play.
        private void OnPlayerTurnover(PlayerZoneTracker player)
        {
            if (matchOver || player == null) return;
            DodgeballPlayByPlay.Log($"{Label(player)} crossed opposing infield with the ball - turnover");
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
            {
                string diveTag = playCatchWasDive ? " (dive)" : "";
                string viol = playViolationTouch ? " (violation)" : "";
                if (playDeflector != null && playDeflector != playTarget)
                    line += $" and deflects off of {Label(playDeflector)} and is caught{diveTag}{viol} by {Label(playCatcher)}";
                else if (playDeflector != null)
                    line += $" and deflects and is caught{diveTag}{viol} by {Label(playCatcher)}";
                else
                    line += $" and is caught{diveTag}{viol} by {Label(playCatcher)}";
            }
            else if (playVictim != null)
                line += playVictim == playTarget ? " and hits" : $" and hits {Label(playVictim)}";
            else if (playIsThrow)
                line += (playTarget != null && playDodge != Ball.DodgeKind.None)
                    ? $" who {DodgeWord(playDodge)}"
                    : " and misses";
            else
            {
                // A pass with no opponent involvement — just whether it landed.
                DodgeballPlayByPlay.Log(line + (playPassCompleted ? " - complete" : " - incomplete"));
                return;
            }

            line += " - " + PlayResult();
            DodgeballPlayByPlay.Log(line);
        }

        private string PlayResult()
        {
            string s = null;
            if (playScoreTeam.HasValue && playScorePoints != 0)
                s = $"+{playScorePoints} {TeamLetter(playScoreTeam.Value)} team";
            if (playElim != null)
                s = s == null ? $"{Label(playElim)} out" : $"{s}, {Label(playElim)} out";
            return s ?? "No Score";
        }

        private static string TeamLetter(Team t) => t == Team.A ? "A" : "B";
        private static string Label(PlayerZoneTracker p) => $"{TeamLetter(p.Spawn.team)}{p.Number}";

        private static string DodgeWord(Ball.DodgeKind d) => d switch
        {
            Ball.DodgeKind.Duck => "ducks",
            Ball.DodgeKind.Jump => "jumps",
            Ball.DodgeKind.Dive => "dives",
            _ => "dodges",
        };

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
