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

        [Header("=== KICKOFF FORMATION ===")]
        [Tooltip("Radial offset added to ScoringRing.RingMidRadius when computing each team's cluster " +
                 "centre. 0 = players' bodies sit on the mid-ring (feet in the endzone). " +
                 "Negative pulls them toward field centre, positive pushes them outward.")]
        [SerializeField] private float formationRadialOffset = 0f;

        [Tooltip("Lateral spacing between teammates along the endzone arc.")]
        [SerializeField] private float playerSpacing = 1.5f;

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

        /// <summary>
        /// True only when at least one ball is in play and the period is active.
        /// New engagements are blocked while this is false (dead-ball windows).
        /// </summary>
        public static bool IsPlayLive { get; private set; }

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
            // Count down to the first ball with the same dead-ball window as a
            // post-score replacement. Defenders may leave their endzone during
            // this window but cannot enter the inner exclusion circle.
            ballActivationTimer = ballActivationInterval;
            bonusZoneTimer      = bonusZoneRotationInterval;
            replacementTimer    = ballReplacementDelay;
            IsPlayLive          = false;

            cannon.ResetPool();
            AssignRolesForPeriod();
            scoringRing.RotateBonusZone();
            ResetAllPlayers();
            RepositionPlayersForPlay();   // hard teleport into endzones
            AssignSetupTargets();         // soft pull defense forward to the inner-circle edge

            phase = Phase.BallReplacement;
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

            // Ball is in play — engagements re-enabled, setup targets released.
            IsPlayLive = true;
            ClearAllSetupTargets();
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

            // Play is dead — kick off the 5s replacement timer and force-disengage everyone.
            // Players walk to their setup positions during the window (no teleport).
            replacementTimer = ballReplacementDelay;
            phase            = Phase.BallReplacement;
            IsPlayLive       = false;
            EndAllEngagements();
            AssignSetupTargets();
        }

        private void HandleBallRemoved(Ball ball)
        {
            UnsubscribeBall(ball);
            activeBalls.Remove(ball);
            Debug.Log("[Demoball] Ball removed from play by defense.");

            // If that was the last ball, treat as a dead play: 5s replacement
            // window, force-disengage, players walk to setup, next ball auto-fires.
            if (activeBalls.Count == 0 && phase == Phase.PeriodActive)
            {
                replacementTimer = ballReplacementDelay;
                phase            = Phase.BallReplacement;
                IsPlayLive       = false;
                EndAllEngagements();
                AssignSetupTargets();
            }
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
            IsPlayLive = false;
        }

        // Forces every player on either roster to break any active engagement.
        // Used when a play ends so locked pairs don't carry into the dead-ball window.
        private void EndAllEngagements()
        {
            for (int i = 0; i < teamA.Count; i++)
                if (teamA[i] != null && teamA[i].IsEngaged) teamA[i].EndEngagement();
            for (int i = 0; i < teamB.Count; i++)
                if (teamB[i] != null && teamB[i].IsEngaged) teamB[i].EndEngagement();
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
        //  KICKOFF FORMATION
        // ──────────────────────────────────────────────

        // Hard reset for a clean kickoff (game start / period start). Snaps both
        // teams onto their formation positions and zeros velocity. Use the soft
        // variant (AssignSetupTargets) for post-score dead-ball windows.
        private void RepositionPlayersForPlay()
        {
            if (scoringRing == null) return;

            ResolveFormation(out ScoringQuadrant offQuad, out ScoringQuadrant defQuad,
                             out var offense, out var defense, out Vector2 fieldCentre);

            PlaceTeam(offense, fieldCentre, offQuad);
            PlaceTeam(defense, fieldCentre, defQuad);

            BlockingAiLog.Log($"<b>Kickoff</b>: offense → {offQuad}, defense → {defQuad}");
        }

        // Soft variant for the dead-ball window. Each player gets a setup target;
        // their AI steers them there over the 5-second replacement timer, and the
        // target is cleared when the next ball fires.
        //
        // Offense holds in their endzone (the locked quadrant). Defense walks
        // forward to the inner-exclusion edge in their own quadrant — they leave
        // their starting zone but cannot cross into the central no-go circle
        // until the ball launches.
        private void AssignSetupTargets()
        {
            if (scoringRing == null) return;

            ResolveFormation(out ScoringQuadrant offQuad, out ScoringQuadrant defQuad,
                             out var offense, out var defense, out Vector2 fieldCentre);

            float endzoneRadius      = scoringRing.RingMidRadius + formationRadialOffset;
            float defenseLineRadius  = scoringRing.InnerExclusionRadius + 0.5f; // sit just outside the no-go circle

            AssignTeamTargets(offense, fieldCentre, offQuad, endzoneRadius);
            AssignTeamTargets(defense, fieldCentre, defQuad, defenseLineRadius);

            BlockingAiLog.Log($"<b>Setup</b>: offense → {offQuad}, defense → inner edge ({defQuad} side)");
        }

        private void ClearAllSetupTargets()
        {
            for (int i = 0; i < teamA.Count; i++) if (teamA[i] != null) teamA[i].ClearSetupTarget();
            for (int i = 0; i < teamB.Count; i++) if (teamB[i] != null) teamB[i].ClearSetupTarget();
        }

        private void ResolveFormation(out ScoringQuadrant offQuad, out ScoringQuadrant defQuad,
                                      out List<DemoballMovementController> offense,
                                      out List<DemoballMovementController> defense,
                                      out Vector2 fieldCentre)
        {
            offQuad     = scoringRing.LockedQuadrant;
            defQuad     = ScoringRing.GetOppositeQuadrant(offQuad);
            fieldCentre = scoringRing.transform.position;
            offense     = teamAOnOffense ? teamA : teamB;
            defense     = teamAOnOffense ? teamB : teamA;
        }

        private Vector2 ComputeFormationSlot(Vector2 fieldCentre, ScoringQuadrant quad,
                                             int slotIndex, int slotCount, float radius)
        {
            float angleRad  = ScoringRing.GetQuadrantCentreAngle(quad) * Mathf.Deg2Rad;
            Vector2 quadDir = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));
            Vector2 perp    = Vector2.Perpendicular(quadDir);
            Vector2 centre  = fieldCentre + quadDir * radius;

            float halfSpread = (slotCount - 1) * 0.5f * playerSpacing;
            return centre + perp * (slotIndex * playerSpacing - halfSpread);
        }

        // Hard placement uses the endzone radius for both teams — every player
        // starts the period with feet in the scoring ring annulus.
        private void PlaceTeam(List<DemoballMovementController> team,
                               Vector2 fieldCentre,
                               ScoringQuadrant quad)
        {
            float radius = scoringRing.RingMidRadius + formationRadialOffset;
            for (int i = 0; i < team.Count; i++)
            {
                var p = team[i];
                if (p == null) continue;

                Vector2 pos = ComputeFormationSlot(fieldCentre, quad, i, team.Count, radius);
                p.transform.position = pos;
                p.ClearSetupTarget();

                var rb = p.GetComponent<Rigidbody2D>();
                if (rb != null) rb.linearVelocity = Vector2.zero;
            }
        }

        private void AssignTeamTargets(List<DemoballMovementController> team,
                                       Vector2 fieldCentre,
                                       ScoringQuadrant quad,
                                       float radius)
        {
            for (int i = 0; i < team.Count; i++)
            {
                var p = team[i];
                if (p == null) continue;

                Vector2 pos = ComputeFormationSlot(fieldCentre, quad, i, team.Count, radius);
                p.SetSetupTarget(pos);
            }
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
