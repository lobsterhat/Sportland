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

        [Header("Referee reset")]
        [Tooltip("Seconds the game pauses while the referee transfers the ball to a player on the OTHER team after any clock expiry. Match clock, shot clock, and delay-of-game all halt during this window. AI bails out of loose-ball chase so no scramble happens.")]
        [SerializeField] private float refTransferDuration = 1.5f;

        [Header("AI offense strategy (team aggression)")]
        [Tooltip("Score/body margin at which a team's aggression hits full swing: behind by this much → maximally aggressive, ahead by this much → maximally cautious.")]
        [SerializeField] private float aggressionMarginScale = 4f;
        [Tooltip("Aggression swing early in a timed match (or at full strength in elimination). 0.25 → multiplier ~0.75..1.25.")]
        [SerializeField] private float aggressionBaseSwing = 0.25f;
        [Tooltip("Aggression swing late in a timed match (or as bodies dwindle in elimination). 0.8 → ~0.2..1.8 before clamping — protect a lead hard / chase hard.")]
        [SerializeField] private float aggressionMaxSwing = 0.8f;
        [Tooltip("Clamp on the team aggression multiplier.")]
        [SerializeField] private float aggressionMin = 0.5f, aggressionMax = 1.8f;

        // Live shot clock state — keyed off the OFFENSIVE TEAM, not a single
        // carrier. Continues through teammate passes / pickups; the carrier
        // reference is just a UI hint for which jersey to render the countdown
        // under (null when the ball's in flight or loose with no holder).
        private Team? shotClockTeam;
        private PlayerZoneTracker shotClockCarrier;
        private float shotClockExpiresAt = -1f;
        // Live delay-of-game state: when the loose-ball alarm fires (-1 = not running).
        private float delayClockExpiresAt = -1f;
        // Live referee-transfer state. >= 0 = we're mid-transfer; game time
        // and all rule clocks are paused, AI ignores the loose ball, the
        // recipient receives the ball via ForcePickup when the timer expires.
        private float refTransferEndsAt = -1f;
        private PlayerZoneTracker refTransferRecipient;

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
        /// <summary>True while the referee is mid-transfer (between any clock expiry and the recipient receiving the ball). Match clock and all rule clocks paused; AI bails out of loose-ball chase.</summary>
        public bool RefTransferActive => refTransferEndsAt >= 0f;
        /// <summary>Seconds remaining in the current referee transfer; 0 when not running.</summary>
        public float RefTransferRemaining => refTransferEndsAt < 0f ? 0f : Mathf.Max(0f, refTransferEndsAt - Time.time);
        /// <summary>Static convenience for AI / Ball checks (no match reference needed). Cleared on match disable.</summary>
        public static bool RefereeTransferActive { get; private set; }

        private Ball ball;
        private bool subscribed;

        private int scoreA;
        private int scoreB;
        private float timeRemaining;
        private bool matchOver;
        private Team? winner;

        /// <summary>Live time remaining (seconds). Settable so the match-controls slider can extend/shorten the current period.</summary>
        public float TimeRemaining { get => timeRemaining; set => timeRemaining = Mathf.Max(0f, value); }

        /// <summary>True once the match has resolved (clock expiry or wipeout).</summary>
        public bool IsOver => matchOver;

        /// <summary>The winning team once the match is over; null for a tie (or while running).</summary>
        public Team? WinnerTeam => winner;
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
            refTransferEndsAt = -1f;
            refTransferRecipient = null;
            RefereeTransferActive = false;
        }

        private void Awake()
        {
            if (mode == null) Configure(null);   // default to Mode 1 if not configured
        }

        private void OnDestroy()
        {
            RefereeTransferActive = false;   // don't leak across scene reloads
            if (ball != null && subscribed)
            {
                ball.OnHit -= OnBallHit;
                ball.OnImpact -= OnBallImpact;
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
            TickRefTransfer();    // resolve a pending ref-transfer first
            TickClocks();
            if (matchOver || !mode.isTimed) return;
            if (RefTransferActive) return;   // game time stops while ref holds the ball

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
            if (RefTransferActive) return;                     // ref holds the ball; no clock progress

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

        // Possession swings without a known carrier (e.g., a throw settled in
        // the receiving team's infield — they "own" the loose ball now). The
        // clock starts ticking against that team from the moment of swing;
        // when one of their players picks the ball up, OnBallAttached sees
        // same-team continuation and just hands the carrier reference over
        // without resetting the countdown.
        private void StartShotClockForTeam(Team team)
        {
            shotClockTeam = team;
            shotClockCarrier = null;
            shotClockExpiresAt = Time.time + shotClockSeconds;
        }

        private void StopShotClock()
        {
            shotClockTeam = null;
            shotClockCarrier = null;
            shotClockExpiresAt = -1f;
        }

        // ----- Referee transfer -----
        // After any clock expiry the ref takes the ball off the offending
        // team and hands it to a player on the OTHER team. Game time and
        // all rule clocks halt for refTransferDuration; AI ignores the
        // parked ball; then ForcePickup attaches it to the recipient and
        // play resumes (their new shot clock arms via OnBallAttached).

        private void TickRefTransfer()
        {
            if (!RefTransferActive) return;
            if (Time.time >= refTransferEndsAt)
                CompleteRefereeTransfer();
        }

        // recipientTeam: who gets the ball.
        // offendingDescriptor: label for the play-by-play log.
        private void StartRefereeTransfer(Team recipientTeam, string offendingDescriptor)
        {
            if (matchOver) return;
            // Force-drop any held ball — the carrier surrenders to the ref.
            if (ball != null && ball.Carrier != null) ball.Drop();
            // Halt rule clocks; the new team's shot clock arms on pickup.
            StopShotClock();
            StopDelayClock();

            refTransferRecipient = PickRefereeRecipient(recipientTeam);
            refTransferEndsAt = Time.time + Mathf.Max(0.1f, refTransferDuration);
            RefereeTransferActive = true;

            string who = refTransferRecipient != null
                ? Label(refTransferRecipient)
                : $"team {TeamLetter(recipientTeam)}";
            DodgeballPlayByPlay.Log($"Referee takes ball from {offendingDescriptor}, handing to {who}");
        }

        private void CompleteRefereeTransfer()
        {
            refTransferEndsAt = -1f;
            RefereeTransferActive = false;

            // Re-pick if the original recipient was benched / removed mid-pause.
            var recipient = refTransferRecipient;
            if (recipient == null || benched.Contains(recipient))
            {
                Team t = recipient != null ? recipient.Spawn.team : Team.A;
                recipient = PickRefereeRecipient(t);
            }
            refTransferRecipient = null;

            if (recipient != null && ball != null)
            {
                ball.ForcePickup(recipient);
                DodgeballPlayByPlay.Log($"Referee hands ball to {Label(recipient)}");
            }
        }

        // Closest live infielder to the centerline (x=0). Falls back to an
        // outfielder if the infield is empty (e.g., Mode 2 eliminations).
        // Returns null only when the recipient team has no playable players.
        private PlayerZoneTracker PickRefereeRecipient(Team team)
        {
            var pick = PickClosestEligibleToCenter(team, PlayerRole.Infielder);
            if (pick != null) return pick;
            return PickClosestEligibleToCenter(team, PlayerRole.Outfielder);
        }

        private PlayerZoneTracker PickClosestEligibleToCenter(Team team, PlayerRole role)
        {
            PlayerZoneTracker best = null;
            float bestDist = float.MaxValue;
            foreach (var p in PlayerZoneTracker.All)
            {
                if (p == null) continue;
                if (p.Spawn.team != team) continue;
                if (p.Spawn.role != role) continue;
                if (benched.Contains(p)) continue;
                float dx = Mathf.Abs(p.transform.position.x);
                if (dx < bestDist) { bestDist = dx; best = p; }
            }
            return best;
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
        // deduct points from the team; TurnoverOnly modes skip the penalty.
        // Either way, the referee takes the ball off the offending team and
        // hands it to a player on the OTHER team (StartRefereeTransfer
        // force-drops any held ball as part of the takeover).
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
            Team opp = team.Value == Team.A ? Team.B : Team.A;
            StartRefereeTransfer(opp, who);
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
            // Ref takes the loose ball from the offending half and hands it
            // to the other team. (Replaces the previous "re-arm and tick
            // again" loop — the ref ends the stall instead.)
            Team opp = offendingTeam == Team.A ? Team.B : Team.A;
            StartRefereeTransfer(opp, $"team {TeamLetter(offendingTeam)}");
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
            ball.OnImpact += OnBallImpact;
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
        private void OnBallHit(PlayerZoneTracker victim, float ballSpeed)
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

                // Bonus rule: hitting an outfielder who's in the opposing
                // infield is a punish for being where they shouldn't be —
                // attacker gets extra points on top of pointsPerHit.
                if (outfielderInOppInfield && mode.outfielderInOppInfieldBonus != 0)
                {
                    AddScore(attacker.Spawn.team, mode.outfielderInOppInfieldBonus);
                    RecordScore(attacker.Spawn.team, mode.outfielderInOppInfieldBonus);
                }

                if (vulnerable)
                {
                    switch (mode.victimOutcome)
                    {
                        case VictimOutcome.CountToOut:   // Mode 2
                            hitCounts.TryGetValue(victim, out int n);
                            hitCounts[victim] = ++n;
                            if (n >= mode.hitsToOut) TakeOut(victim, permanent: true);
                            break;
                        case VictimOutcome.DamageEnergy: // Mode 3 — energy damage + the
                            break;                       // 0-energy elimination now land at
                                                         // IMPACT (OnBallImpact), so a caught
                                                         // rebound can't undo them.
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

        // The instant a throw contacts a defender (connect or mishandle), BEFORE the
        // rebound resolves. Damage + stamina land here and are never undone by a catch
        // (the catch only saves the deferred scoring hit / outs the thrower). contactMul
        // is 1 for a direct connect, <1 for a glancing mishandle (bobble).
        private void OnBallImpact(PlayerZoneTracker victim, float ballSpeed, float contactMul)
        {
            if (matchOver || victim == null) return;
            var attacker = ball != null ? ball.RecentThrower : null;
            if (attacker == null || attacker.Spawn.team == victim.Spawn.team) return;   // opponent's hit only
            if (ball != null && ball.LastReleaseFromOpposingInfield) return;             // neutered throw (Phase D)

            // Universal across all modes: being hit tires you — even when a teammate
            // catches the rebound and saves the point.
            victim.GetComponent<PlayerStamina>()?.TakeImpact(ballSpeed, contactMul);

            // Damage game (Mode 3): energy drains at impact; 0 = out. Same vulnerability
            // rule as scoring — a backrow outfielder shrugs it off.
            if (mode.victimOutcome == VictimOutcome.DamageEnergy && IsVulnerable(victim))
                if (ApplyDamage(victim, ballSpeed, contactMul) <= 0f) TakeOut(victim, permanent: true);
        }

        // Who an opponent's throw can damage / eliminate: infielders always; an
        // outfielder only while grounded inside the opposing infield (Phase C).
        private bool IsVulnerable(PlayerZoneTracker victim)
            => victim.Spawn.role == PlayerRole.Infielder
               || (victim.Spawn.role == PlayerRole.Outfielder && victim.IsInOpposingInfield && victim.IsGrounded);

        // Drain the victim's energy by the ball's impact speed, softened by the victim's
        // toughness and scaled by the contact (direct connect vs glancing mishandle).
        // Returns the energy remaining.
        private float ApplyDamage(PlayerZoneTracker victim, float ballSpeed, float contactMul)
        {
            var gen = victim.GetComponent<GeneralAttributes>();
            float maxE = gen != null ? gen.maxEnergy : 100f;
            float tough01 = gen != null ? gen.Toughness01 : 0.5f;

            if (!energy.TryGetValue(victim, out float e)) e = maxE;
            // TODO: × thrower "sting" damage multiplier (future attribute) — see defense.md
            float dmg = ballSpeed * mode.damagePerSpeed * Mathf.Clamp01(contactMul)
                      * (1f - tough01 * mode.toughnessReduction);
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

            // Bonus rule: an outfielder catching a ball while standing in the
            // opposing infield gives the ATTACKER points — the catch counts
            // as a play out of position, not as a clean defensive read. Runs
            // before the normal catch effects so the turnover still happens.
            if (catcher.Spawn.role == PlayerRole.Outfielder
                && catcher.IsInOpposingInfield
                && mode.outfielderInOppInfieldBonus != 0
                && ball != null && ball.RecentThrower != null
                && ball.RecentThrower.Spawn.team != catcher.Spawn.team)
            {
                var attacker = ball.RecentThrower;
                AddScore(attacker.Spawn.team, mode.outfielderInOppInfieldBonus);
                RecordScore(attacker.Spawn.team, mode.outfielderInOppInfieldBonus);
            }

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
            // clock running so the offense feels the pressure. When the swing
            // does happen, we ALSO start a fresh shot clock for the receiving
            // team — they're now in possession of the loose ball and have to
            // use it. Delay-of-game is suppressed in that case (the shot
            // clock is the only stalling pressure that should be active).
            bool shotReachedOppInfield = false;
            if (shotClockTeam.HasValue && ball != null)
            {
                Team oppOfClocked = shotClockTeam.Value == Team.A ? Team.B : Team.A;
                if (ZoneFactory.InfieldFor(oppOfClocked).Contains(ball.transform.position))
                {
                    StopShotClock();
                    StartShotClockForTeam(oppOfClocked);
                    shotReachedOppInfield = true;
                }
            }
            if (!shotReachedOppInfield)
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
        // PointPenalty modes: -1 to their team. TurnoverOnly modes: no
        // score change. Either way the referee then takes the ball off
        // them and hands it to a player on the OTHER team.
        private void OnPlayerForcedDrop(PlayerZoneTracker player)
        {
            if (matchOver || player == null) return;
            bool penalize = mode == null || mode.clockExpiryEffect == ClockExpiryEffect.PointPenalty;
            if (penalize)
            {
                AddScore(player.Spawn.team, -1);
                DodgeballPlayByPlay.Log($"{Label(player)} didn't return in time, dropped ball - -1 {TeamLetter(player.Spawn.team)} team");
            }
            else
            {
                DodgeballPlayByPlay.Log($"{Label(player)} didn't return in time, dropped ball - turnover");
            }
            Team opp = player.Spawn.team == Team.A ? Team.B : Team.A;
            StartRefereeTransfer(opp, Label(player));
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

        /// <summary>Current score for a team.</summary>
        public int Score(Team team) => team == Team.A ? scoreA : scoreB;

        /// <summary>
        /// AI offense strategy: how aggressive <paramref name="team"/> should be
        /// right now, as a multiplier on attack chance. Behind → &gt;1 (chase),
        /// ahead → &lt;1 (protect the lead), tied → ~1. The swing is amplified
        /// late in timed modes, and as bodies dwindle in elimination modes.
        /// Read per-possession by DodgeballAI.SelectAttack.
        /// </summary>
        public float TeamAggression(Team team)
        {
            if (matchOver || mode == null) return 1f;
            Team opp = team == Team.A ? Team.B : Team.A;

            float margin;    // + = this team is ahead
            float urgency;   // 0..1 amplifier on the swing

            if (mode.isTimed)
            {
                margin = Score(team) - Score(opp);
                float frac = mode.secondsPerPeriod > 0f
                    ? Mathf.Clamp01(timeRemaining / mode.secondsPerPeriod) : 1f;
                urgency = 1f - frac;   // late game → more urgent
            }
            else   // elimination / energy: margin is bodies, urgency rises as bodies thin out
            {
                margin = CountActiveInfielders(team) - CountActiveInfielders(opp);
                int minAlive = Mathf.Min(CountActiveInfielders(team), CountActiveInfielders(opp));
                urgency = mode.infieldersPerTeam > 0
                    ? 1f - Mathf.Clamp01((float)minAlive / mode.infieldersPerTeam) : 0f;
            }

            float normalized = Mathf.Clamp(-margin / Mathf.Max(0.01f, aggressionMarginScale), -1f, 1f); // behind → +1
            float swing = Mathf.Lerp(aggressionBaseSwing, aggressionMaxSwing, urgency);
            return Mathf.Clamp(1f + normalized * swing, aggressionMin, aggressionMax);
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
            style.font = DodgeballUI.Font;   // null = built-in; drag a .ttf onto CourtSetup.uiFont to test
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
