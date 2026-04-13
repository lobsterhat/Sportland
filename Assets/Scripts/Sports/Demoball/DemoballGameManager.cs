using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Sportland.Sports.Demoball
{
    /// <summary>
    /// Demoball game orchestrator.
    ///
    /// Structure:
    ///   4 periods × 2 minutes. Teams alternate offense/defense each period so
    ///   each team plays 2 offensive and 2 defensive periods.
    ///
    /// Ball timing:
    ///   BallCannon fires a new ball every 30 seconds of game clock.
    ///   Scoring triggers a 5-second replacement countdown while the clock keeps running.
    ///   Bonus zone rotates every 30 seconds (independent of ball timer).
    ///
    /// Scoring:
    ///   Touch-down anywhere in ring  = 1 pt (base).
    ///   Touch-down in bonus zone     = base + 1 pt (first offensive period).
    ///   Touch-down in bonus zone     = base + 2 pt (second offensive period).
    ///   Max per team ≈ 16 or 20 depending on bonus escalation.
    ///
    /// Substitutions:
    ///   2 per period; players may re-enter in a later period.
    ///   Injury substitutions permanently remove the injured player.
    ///
    /// Early end:
    ///   Game ends immediately if the trailing team cannot mathematically tie.
    /// </summary>
    public class DemoballGameManager : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        //  CONFIGURATION
        // ──────────────────────────────────────────────

        [Header("=== PERIODS ===")]
        [SerializeField] private float periodDuration = 120f;   // 2 minutes
        [SerializeField] private int   totalPeriods   = 4;

        [Header("=== BALL TIMING ===")]
        [SerializeField] private float ballActivationInterval = 30f;
        [SerializeField] private float ballReplacementDelay   =  5f;

        [Header("=== BONUS ZONE ===")]
        [SerializeField] private float bonusZoneRotationInterval = 30f;

        [Header("=== SCORING ===")]
        [Tooltip("Bonus points added to a touch-down in the bonus zone during a team's FIRST offensive period.")]
        [SerializeField] private int firstOffensivePeriodBonus  = 1; // 1 + 1 = 2 pts

        [Tooltip("Bonus points added to a touch-down in the bonus zone during a team's SECOND offensive period.")]
        [SerializeField] private int secondOffensivePeriodBonus = 2; // 1 + 2 = 3 pts

        [Header("=== SCENE REFERENCES ===")]
        [SerializeField] private BallCannon  cannon;
        [SerializeField] private ScoringRing scoringRing;

        [Header("=== TEAMS ===")]
        [Tooltip("All players on Team A (roster of up to 10; 6 active at once).")]
        [SerializeField] private List<DemoballMovementController> teamA;

        [Tooltip("All players on Team B (roster of up to 10; 6 active at once).")]
        [SerializeField] private List<DemoballMovementController> teamB;

        // ──────────────────────────────────────────────
        //  GAME STATE
        // ──────────────────────────────────────────────

        private enum Phase
        {
            PreGame,
            PeriodActive,
            PeriodEnd,
            BallReplacement, // 5-second window after a score; clock still runs
            GameOver
        }

        private Phase phase = Phase.PreGame;

        private int  currentPeriod;      // 1-indexed during play
        private bool teamAOnOffense;

        private float periodTimeRemaining;
        private float ballActivationTimer;
        private float bonusZoneTimer;
        private float replacementTimer;

        // How many offensive periods each team has completed (for bonus calculation)
        private int teamAOffensivePeriods;
        private int teamBOffensivePeriods;

        // ──────────────────────────────────────────────
        //  SCORE
        // ──────────────────────────────────────────────

        private int teamAScore;
        private int teamBScore;

        // ──────────────────────────────────────────────
        //  SUBSTITUTIONS
        // ──────────────────────────────────────────────

        private int teamASubsLeft;
        private int teamBSubsLeft;
        private const int SubsPerPeriod = 2;

        // ──────────────────────────────────────────────
        //  ACTIVE BALLS
        // ──────────────────────────────────────────────

        private readonly List<Ball> activeBalls = new List<Ball>();

        // ──────────────────────────────────────────────
        //  HUD (runtime-built stubs — flesh out alongside PlayerInfoBar)
        // ──────────────────────────────────────────────

        private Text periodLabel;
        private Text clockLabel;
        private Text scoreLabel;
        private Text statusLabel;

        // ──────────────────────────────────────────────
        //  UNITY LIFECYCLE
        // ──────────────────────────────────────────────

        private void Start()
        {
            BuildHUD();
            SubscribeToPlayerEvents();
            StartGame();
        }

        private void Update()
        {
            switch (phase)
            {
                case Phase.PeriodActive:    TickPeriod();      break;
                case Phase.BallReplacement: TickReplacement(); break;
            }
            RefreshHUD();
        }

        // ──────────────────────────────────────────────
        //  GAME FLOW
        // ──────────────────────────────────────────────

        private void StartGame()
        {
            teamAScore = teamBScore = 0;
            teamAOffensivePeriods = teamBOffensivePeriods = 0;
            currentPeriod = 0;
            teamAOnOffense = true;
            BeginNextPeriod();
        }

        private void BeginNextPeriod()
        {
            currentPeriod++;
            if (currentPeriod > totalPeriods)
            {
                EndGame();
                return;
            }

            // Odd periods → Team A on offense; even → Team B
            teamAOnOffense = (currentPeriod % 2 == 1);
            if (teamAOnOffense) teamAOffensivePeriods++;
            else                teamBOffensivePeriods++;

            teamASubsLeft = teamBSubsLeft = SubsPerPeriod;

            periodTimeRemaining = periodDuration;
            ballActivationTimer = 0f;   // fire first ball immediately at kick-off
            bonusZoneTimer      = bonusZoneRotationInterval;

            cannon.ResetPool();
            AssignRolesForPeriod();
            scoringRing.RotateBonusZone();
            ResetAllPlayers();

            phase = Phase.PeriodActive;
            Debug.Log($"[Demoball] Period {currentPeriod} started — " +
                      $"Offense: {(teamAOnOffense ? "Team A" : "Team B")}");
        }

        private void TickPeriod()
        {
            periodTimeRemaining -= Time.deltaTime;

            // Ball activation
            ballActivationTimer -= Time.deltaTime;
            if (ballActivationTimer <= 0f)
            {
                ballActivationTimer = ballActivationInterval;
                FireNewBall();
            }

            // Bonus zone rotation
            bonusZoneTimer -= Time.deltaTime;
            if (bonusZoneTimer <= 0f)
            {
                bonusZoneTimer = bonusZoneRotationInterval;
                scoringRing.RotateBonusZone();
            }

            if (periodTimeRemaining <= 0f)
                EndPeriod();
        }

        private void TickReplacement()
        {
            // Clock keeps running during replacement countdown
            periodTimeRemaining -= Time.deltaTime;

            replacementTimer -= Time.deltaTime;
            if (replacementTimer <= 0f)
            {
                phase = Phase.PeriodActive;
                FireNewBall();
            }

            if (periodTimeRemaining <= 0f)
                EndPeriod();
        }

        private void EndPeriod()
        {
            phase = Phase.PeriodEnd;
            CleanUpActiveBalls();

            Debug.Log($"[Demoball] Period {currentPeriod} ended. " +
                      $"Score — A: {teamAScore}  B: {teamBScore}");

            if (IsMathematicallyOver())
            {
                EndGame();
                return;
            }

            // TODO: brief inter-period pause / substitution window UI before next period
            BeginNextPeriod();
        }

        private void EndGame()
        {
            phase = Phase.GameOver;
            string result = teamAScore > teamBScore ? "Team A wins"
                          : teamBScore > teamAScore ? "Team B wins"
                          : "Draw";
            Debug.Log($"[Demoball] Game Over — {result}  ({teamAScore} – {teamBScore})");
            // TODO: display final result screen
        }

        // ──────────────────────────────────────────────
        //  EARLY END CHECK
        // ──────────────────────────────────────────────

        /// <summary>
        /// Returns true when the trailing team can no longer tie even if they score
        /// the maximum possible points on every remaining ball this game.
        /// </summary>
        private bool IsMathematicallyOver()
        {
            int periodsLeft = totalPeriods - currentPeriod;
            if (periodsLeft <= 0) return false;

            // Rough upper bound: all remaining balls scored with max bonus
            int maxBallsPerPeriod = Mathf.RoundToInt(periodDuration / ballActivationInterval);
            int maxPtsPerBall     = 1 + secondOffensivePeriodBonus;
            int maxPossible       = periodsLeft * maxBallsPerPeriod * maxPtsPerBall;

            int gap = Mathf.Abs(teamAScore - teamBScore);
            return gap > maxPossible;
        }

        // ──────────────────────────────────────────────
        //  BALL MANAGEMENT
        // ──────────────────────────────────────────────

        private void FireNewBall()
        {
            Ball ball = cannon.Fire();
            if (ball == null) return;

            ball.OnScored          += HandleBallScored;
            ball.OnRemovedFromPlay += HandleBallRemoved;
            activeBalls.Add(ball);
        }

        private void HandleBallScored(Ball ball, bool inBonusZone)
        {
            UnsubscribeBall(ball);
            activeBalls.Remove(ball);

            // Attribute points to the current offensive team
            int offensivePeriods  = teamAOnOffense ? teamAOffensivePeriods : teamBOffensivePeriods;
            int bonusPoints       = offensivePeriods <= 1
                                    ? firstOffensivePeriodBonus
                                    : secondOffensivePeriodBonus;
            int points = 1 + (inBonusZone ? bonusPoints : 0);

            if (teamAOnOffense) teamAScore += points;
            else                teamBScore += points;

            Debug.Log($"[Demoball] Score! {points} pt{(points > 1 ? "s" : "")} " +
                      $"({(inBonusZone ? "BONUS" : "standard")}) — " +
                      $"A: {teamAScore}  B: {teamBScore}");

            replacementTimer = ballReplacementDelay;
            phase = Phase.BallReplacement;
        }

        private void HandleBallRemoved(Ball ball)
        {
            UnsubscribeBall(ball);
            activeBalls.Remove(ball);
            Debug.Log("[Demoball] Ball removed from play by defense.");
        }

        private void UnsubscribeBall(Ball ball)
        {
            ball.OnScored          -= HandleBallScored;
            ball.OnRemovedFromPlay -= HandleBallRemoved;
        }

        private void CleanUpActiveBalls()
        {
            foreach (var ball in activeBalls)
            {
                UnsubscribeBall(ball);
                ball.RemoveFromPlay();
            }
            activeBalls.Clear();
        }

        // ──────────────────────────────────────────────
        //  ROLE ASSIGNMENT
        // ──────────────────────────────────────────────

        private void AssignRolesForPeriod()
        {
            var offense = teamAOnOffense ? teamA : teamB;
            var defense = teamAOnOffense ? teamB : teamA;

            // First 2 active offense players = Scorers, rest = Blockers
            for (int i = 0; i < offense.Count; i++)
                offense[i].AssignRole(i < 2 ? DemoballRole.Scorer : DemoballRole.Blocker);

            foreach (var p in defense)
                p.AssignRole(DemoballRole.Defender);
        }

        // ──────────────────────────────────────────────
        //  SUBSTITUTION  (called from UI or input)
        // ──────────────────────────────────────────────

        /// <summary>
        /// Swaps outPlayer (currently on field) for inPlayer (on bench).
        /// Returns false if this team has no subs left this period.
        /// </summary>
        public bool RequestSubstitution(DemoballMovementController outPlayer,
                                        DemoballMovementController inPlayer,
                                        bool isTeamA)
        {
            if (isTeamA && teamASubsLeft <= 0) return false;
            if (!isTeamA && teamBSubsLeft <= 0) return false;

            // TODO: enforce active roster size, validate team membership, animate swap
            if (isTeamA) teamASubsLeft--;
            else         teamBSubsLeft--;

            Debug.Log($"[Demoball] Sub: {outPlayer.name} → {inPlayer.name}");
            return true;
        }

        /// <summary>
        /// Permanently removes an injured player. No sub credit consumed.
        /// </summary>
        public void ReportInjury(DemoballMovementController player)
        {
            player.gameObject.SetActive(false);
            Debug.Log($"[Demoball] Injury: {player.name} removed from game.");
        }

        // ──────────────────────────────────────────────
        //  PLAYER EVENT SUBSCRIPTIONS
        // ──────────────────────────────────────────────

        private void SubscribeToPlayerEvents()
        {
            foreach (var p in teamA) p.OnTouchDown += HandleTouchDown;
            foreach (var p in teamB) p.OnTouchDown += HandleTouchDown;
        }

        // OnTouchDown fires from DemoballMovementController before Ball.Score() is called,
        // passing inBonusZone. Ball.OnScored fires after and HandleBallScored does the
        // actual point attribution — so this handler is just a hook for VFX / audio.
        private void HandleTouchDown(Ball ball, bool inBonusZone)
        {
            // TODO: trigger crowd reaction, VFX, HUD flash
        }

        // ──────────────────────────────────────────────
        //  PLAYER RESET
        // ──────────────────────────────────────────────

        private void ResetAllPlayers()
        {
            foreach (var p in teamA) p.ResetForNewPeriod();
            foreach (var p in teamB) p.ResetForNewPeriod();
        }

        // ──────────────────────────────────────────────
        //  HUD
        // ──────────────────────────────────────────────

        private void BuildHUD()
        {
            // TODO: implement runtime-built HUD following the PixelArtRenderer / PlayerInfoBar pattern
            // Placeholder — wire up Text references here when the canvas is built
        }

        private void RefreshHUD()
        {
            if (clockLabel  != null) clockLabel.text  = FormatTime(Mathf.Max(0f, periodTimeRemaining));
            if (scoreLabel  != null) scoreLabel.text  = $"A  {teamAScore}  –  {teamBScore}  B";
            if (periodLabel != null) periodLabel.text = $"Period  {currentPeriod} / {totalPeriods}";
            if (statusLabel != null) statusLabel.text = phase == Phase.BallReplacement
                ? $"Next ball in {replacementTimer:F1}s"
                : string.Empty;
        }

        private static string FormatTime(float seconds)
        {
            int m = (int)(seconds / 60f);
            int s = (int)(seconds % 60f);
            return $"{m}:{s:00}";
        }
    }
}
