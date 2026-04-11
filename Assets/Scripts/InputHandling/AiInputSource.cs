using UnityEngine;
using Sportland.Movement;
using Sportland.Rendering;
using Sportland.Sports.Tag;

namespace Sportland.InputHandling
{
    /// <summary>
    /// AI input source for Tag. Contains chase/flee decision-making and
    /// exposes commands through IInputSource for the InputBroker.
    /// 
    /// All the AI "brain" lives here. The character's TagMovementController
    /// and InputBroker don't know they're being driven by AI.
    /// </summary>
    [System.Serializable]
    public class AIInputSource : IInputSource
    {
        // ──────────────────────────────────────────────
        //  CONFIGURATION (set via AIInputConfig SO or defaults)
        // ──────────────────────────────────────────────

        [Header("=== AWARENESS ===")]
        private float awarenessRadius = 20f;
        private LayerMask playerLayer;

        [Header("=== CHASE BEHAVIOR ===")]
        private float sprintChaseDistance = 8f;
        private float lungeAttemptDistance = 1.8f;
        private float tagAttemptDistance = 1.3f;
        private float sprintStaminaThreshold = 0.3f;
        private float sprintRecoveryThreshold = 0.6f;
        private float lungeCooldown = 1.2f;          // min seconds between AI lunge decisions
        private float interceptLookaheadCap = 1.5f;  // max seconds to predict ahead for intercept
        private float cutoffZoneScanRadius = 8f;     // how far ahead to check if runner targets a zone
        private float cutoffAlignmentThreshold = 0.6f; // dot product: how directly runner heads at zone

        [Header("=== FLEE BEHAVIOR ===")]
        private float fleeDistance = 7f;
        private float evasionTriggerDistance = 2.5f;
        private float panicDistance = 3f;

        [Header("=== STANDOFF BEHAVIOR ===")]
        private float standoffEvalDistance = 3.5f;   // max distance to consider entering standoff
        private float standoffMinStamina = 0.25f;    // enter standoff if stamina below this
        private float standoffWallThreshold = 3;     // number of blocked escape routes to trigger standoff
        private float standoffMaintainDistance = 2.5f;// ideal gap to hold during standoff
        private float standoffLeanInterval = 0.4f;   // time between AI lean feints
        private float standoffBurstChance = 0.3f;    // chance per evaluation to commit to a burst
        private float standoffEscapeCheckInterval = 0.5f; // how often to re-evaluate escape routes

        [Header("=== WALL AVOIDANCE ===")]
        private float wallDetectDistance = 2f;
        private float wallAvoidanceStrength = 0.6f;
        private LayerMask wallLayer;

        [Header("=== DECISION VARIETY ===")]
        private float decisionInterval = 0.15f;
        private float movementJitter = 15f;

        // ──────────────────────────────────────────────
        //  RUNTIME STATE
        // ──────────────────────────────────────────────

        private GameObject character;
        private TagMovementController movement;
        private Transform characterTransform;

        private float decisionTimer;
        private bool isRecoveringStamina;
        private float lungeTimer;           // AI-side cooldown between lunge attempts

        // Barge window: AI briefly charges toward the It player to connect a barge
        private float bargeChargeTimer;     // > 0 while AI is in the charge-at-chaser window
        private float aiBargeCooldownTimer; // prevents AI from re-entering barge window too quickly
        private const float AiBargeTriggerDist = 2.4f;  // how close chaser must be to trigger
        private const float AiBargeCooldown = 6f;

        // Standoff state
        private bool isInStandoff;
        private float standoffLeanTimer;
        private int currentLeanSide;       // -1 = left, 0 = center, 1 = right
        private float standoffEscapeTimer;
        private bool standoffBurstCommitted;

        // Current frame outputs
        private Vector2 currentDesiredDirection;
        private bool sprinting;
        private bool shuffling;
        private bool jumpRequest;
        private bool diveRequest;
        private bool specialRequest;
        private bool tagRequest;

        // Cached targets
        private Transform chaseTarget;
        private Transform threatSource;

        // Track which runners we last put scores on so we can clear them when they drop out
        private System.Collections.Generic.HashSet<StatusIconDisplay> scoredRunners
            = new System.Collections.Generic.HashSet<StatusIconDisplay>();

        // ──────────────────────────────────────────────
        //  CONSTRUCTOR
        // ──────────────────────────────────────────────

        public AIInputSource(LayerMask playerLayer, LayerMask wallLayer)
        {
            this.playerLayer = playerLayer;
            this.wallLayer = wallLayer;
        }

        /// <summary>
        /// Overload with full configuration for custom AI tuning.
        /// </summary>
        public AIInputSource(LayerMask playerLayer, LayerMask wallLayer,
            float awarenessRadius = 15f, float sprintChaseDistance = 8f,
            float lungeAttemptDistance = 2.5f, float tagAttemptDistance = 1.5f,
            float fleeDistance = 7f, float evasionTriggerDistance = 2.5f,
            float panicDistance = 3f, float wallDetectDistance = 2f,
            float wallAvoidanceStrength = 0.6f, float decisionInterval = 0.15f,
            float movementJitter = 15f, float sprintStaminaThreshold = 0.3f,
            float sprintRecoveryThreshold = 0.6f)
        {
            this.playerLayer = playerLayer;
            this.wallLayer = wallLayer;
            this.awarenessRadius = awarenessRadius;
            this.sprintChaseDistance = sprintChaseDistance;
            this.lungeAttemptDistance = lungeAttemptDistance;
            this.tagAttemptDistance = tagAttemptDistance;
            this.fleeDistance = fleeDistance;
            this.evasionTriggerDistance = evasionTriggerDistance;
            this.panicDistance = panicDistance;
            this.wallDetectDistance = wallDetectDistance;
            this.wallAvoidanceStrength = wallAvoidanceStrength;
            this.decisionInterval = decisionInterval;
            this.movementJitter = movementJitter;
            this.sprintStaminaThreshold = sprintStaminaThreshold;
            this.sprintRecoveryThreshold = sprintRecoveryThreshold;
        }

        // ──────────────────────────────────────────────
        //  IInputSource LIFECYCLE
        // ──────────────────────────────────────────────

        private StatusIconDisplay statusIcons;

        // Debug visualization
        private LineRenderer interceptLine;
        private LineRenderer cutoffLine;
        private Vector2 debugInterceptPoint;
        private Vector2 debugCutoffPoint;
        private bool debugShowIntercept;
        private bool debugShowCutoff;

        public void OnActivate(GameObject character)
        {
            this.character = character;
            this.characterTransform = character.transform;
            this.movement = character.GetComponent<TagMovementController>();
            this.statusIcons = character.GetComponent<StatusIconDisplay>();
            this.decisionTimer = 0f;
            this.isRecoveringStamina = false;

            interceptLine = CreateDebugLine("AIDebug_Intercept", Color.green);
            cutoffLine = CreateDebugLine("AIDebug_Cutoff", new Color(1f, 0.45f, 0f));

            ClearOutputs();
        }

        public void OnDeactivate()
        {
            ClearOutputs();

            if (interceptLine != null) Object.Destroy(interceptLine.gameObject);
            if (cutoffLine != null) Object.Destroy(cutoffLine.gameObject);
            interceptLine = null;
            cutoffLine = null;

            foreach (var display in scoredRunners)
                if (display != null) display.ClearTargetScore();
            scoredRunners.Clear();

            character = null;
            characterTransform = null;
            movement = null;
        }

        private LineRenderer CreateDebugLine(string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(characterTransform);

            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.startWidth = 0.06f;
            lr.endWidth = 0.06f;
            lr.startColor = color;
            lr.endColor = color;
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.sortingOrder = 120;
            lr.useWorldSpace = true;
            lr.enabled = false;
            return lr;
        }

        public void UpdateInput()
        {
            if (movement == null || movement.IsEliminated)
            {
                ClearOutputs();
                return;
            }

            // Clear single-frame requests
            jumpRequest = false;
            diveRequest = false;
            specialRequest = false;
            tagRequest = false;

            // Tick cooldowns
            if (lungeTimer > 0f) lungeTimer -= Time.deltaTime;
            if (aiBargeCooldownTimer > 0f) aiBargeCooldownTimer -= Time.deltaTime;
            if (bargeChargeTimer > 0f) bargeChargeTimer -= Time.deltaTime;

            UpdateDebugVisuals();

            // Throttle decisions
            decisionTimer -= Time.deltaTime;
            if (decisionTimer <= 0f)
            {
                decisionTimer = decisionInterval;
                MakeDecision();
            }
        }

        // ──────────────────────────────────────────────
        //  IInputSource OUTPUTS
        // ──────────────────────────────────────────────

        public Vector2 GetMoveInput() => currentDesiredDirection;
        public bool IsSprinting() => sprinting;
        public bool IsShuffling() => shuffling;
        public float GetLeanInput() => isInStandoff ? currentLeanSide : 0f;
        public bool JumpRequested() => jumpRequest;
        public bool DiveRequested() => diveRequest;
        public bool SpecialRequested() => specialRequest;
        public bool TagRequested() => tagRequest;

        // ──────────────────────────────────────────────
        //  DECISION MAKING
        // ──────────────────────────────────────────────

        private void MakeDecision()
        {
            ScanForTargets();

            if (movement.CurrentRole == TagMovementController.TagRole.It)
            {
                DecideAsChaser();
            }
            else
            {
                DecideAsRunner();
            }
        }

        // ──────────────────────────────────────────────
        //  CHASER AI
        // ──────────────────────────────────────────────

        private void DecideAsChaser()
        {
            if (chaseTarget == null)
            {
                Wander();
                ReportAIState(StatusIconDisplay.AIDecisionState.Wandering);
                return;
            }

            var targetMovement = chaseTarget.GetComponent<TagMovementController>();

            Vector2 toTarget = (Vector2)(chaseTarget.position - characterTransform.position);
            float distance = toTarget.magnitude;
            Vector2 direction = toTarget.normalized;

            // Walk-up tag — close enough to touch
            if (movement.CanAct() && distance <= tagAttemptDistance)
            {
                tagRequest = true;
            }

            // Lunge — commit when in the gap between touch range and lunge range,
            // target isn't in shuffle stance (no lunging into a standoff), and
            // our AI-side cooldown has expired.
            if (movement.CanAct()
                && distance > tagAttemptDistance
                && distance <= lungeAttemptDistance
                && lungeTimer <= 0f)
            {
                bool targetShuffling = targetMovement != null
                    && targetMovement.CurrentState == BaseMovementController.MovementState.Shuffling;

                if (!targetShuffling)
                {
                    specialRequest = true;
                    lungeTimer = lungeCooldown;
                }
            }

            // Shuffling target at close range — switch to stalk mode
            bool targetIsShuffling = targetMovement != null
                && targetMovement.CurrentState == BaseMovementController.MovementState.Shuffling;

            if (targetIsShuffling && distance < standoffEvalDistance)
            {
                DecideAsStalk(distance, direction, targetMovement);
                ReportAIState(StatusIconDisplay.AIDecisionState.Stalking);
                return;
            }

            // Determine chase direction.
            // Priority 1: cut off the runner if they're heading for a safe zone.
            // Priority 2: intercept their predicted future position.
            Vector2 chaseDir;
            StatusIconDisplay.AIDecisionState reportedState;

            debugShowIntercept = false;
            debugShowCutoff = false;

            if (IsRunnerHeadingToZone(chaseTarget, out SafeZone targetZone))
            {
                chaseDir = GetCutoffDirection(chaseTarget, targetZone, out debugCutoffPoint);
                debugShowCutoff = true;
                reportedState = StatusIconDisplay.AIDecisionState.CuttingOff;
            }
            else
            {
                Vector2 interceptPoint = GetInterceptPoint(chaseTarget, movement.GetEffectiveTopSpeed());
                chaseDir = ((Vector2)interceptPoint - (Vector2)characterTransform.position);
                if (chaseDir.sqrMagnitude > 0.01f) chaseDir = chaseDir.normalized;
                else chaseDir = direction;

                // Report Intercepting only when the predict point is meaningfully ahead of current pos
                bool isIntercepting = Vector2.Distance(interceptPoint, (Vector2)chaseTarget.position) > 0.4f;
                if (isIntercepting)
                {
                    debugInterceptPoint = interceptPoint;
                    debugShowIntercept = true;
                }
                reportedState = isIntercepting
                    ? StatusIconDisplay.AIDecisionState.Intercepting
                    : StatusIconDisplay.AIDecisionState.Chasing;
            }

            Vector2 steerDir = ApplyZoneAvoidance(chaseDir);
            currentDesiredDirection = ApplyWallAvoidance(steerDir);
            currentDesiredDirection = ApplyJitter(currentDesiredDirection);
            ReportAIState(reportedState);

            UpdateSprintDecision(distance, sprintChaseDistance);
        }

        // ──────────────────────────────────────────────
        //  STALK AI (chaser in standoff)
        // ──────────────────────────────────────────────

        /// <summary>
        /// Cautious approach when the target is in standoff/shuffle stance.
        /// The chaser slows down, tries to read the target's leans,
        /// and waits for a real commitment before lunging in.
        /// </summary>
        private void DecideAsStalk(float distance, Vector2 dirToTarget,
            TagMovementController targetMovement)
        {
            // Read the target's apparent movement (leans look like real movement)
            Vector2 apparentDir = targetMovement.GetApparentMovementDirection();
            float targetDeception = targetMovement.Profile != null
                ? targetMovement.Profile.deceptionRating : 0.5f;
            float myAwareness = movement.GetAwarenessMultiplier();

            // How likely we are to bite on a fake (0 = never fooled, 1 = always fooled)
            float fooledChance = targetDeception * (1f / myAwareness);

            // Slow approach — don't sprint at a player in stance
            sprinting = false;

            if (distance > standoffMaintainDistance * 1.2f)
            {
                // Close the gap slowly
                currentDesiredDirection = ApplyWallAvoidance(dirToTarget);
            }
            else
            {
                // In range — mirror the target's apparent movement
                // The lower our awareness, the more we react to fakes
                if (apparentDir.sqrMagnitude > 0.1f && Random.value < fooledChance)
                {
                    // Bite on the fake — shift toward where the lean suggests they're going
                    Vector2 predictedDir = (dirToTarget + apparentDir * 0.5f).normalized;
                    currentDesiredDirection = ApplyWallAvoidance(predictedDir);
                }
                else
                {
                    // Hold position, slight drift toward target
                    currentDesiredDirection = dirToTarget * 0.3f;
                }
            }

            // If the target drops shuffle (burst out), immediately sprint to chase
            if (!targetMovement.IsLeaning && targetMovement.CurrentState != BaseMovementController.MovementState.Shuffling)
            {
                sprinting = true;
                currentDesiredDirection = ApplyWallAvoidance(dirToTarget);
            }
        }

        // ──────────────────────────────────────────────
        //  RUNNER AI
        // ──────────────────────────────────────────────

        private void DecideAsRunner()
        {
            if (threatSource == null)
            {
                ExitStandoff();
                Wander();
                sprinting = false;
                ReportAIState(StatusIconDisplay.AIDecisionState.Wandering);
                return;
            }

            Vector2 fromThreat = (Vector2)(characterTransform.position - threatSource.position);
            float distance = fromThreat.magnitude;

            if (distance < fleeDistance)
            {
                // Check if we're in a safe zone — position away from threat within zone
                if (movement.InSafeZone)
                {
                    ExitStandoff();
                    Vector2 awayFromThreat = fromThreat.normalized;
                    currentDesiredDirection = awayFromThreat * 0.3f;
                    sprinting = false;
                    ReportAIState(StatusIconDisplay.AIDecisionState.RestingInZone);
                    return;
                }

                // Check if a safe zone is available and nearby
                SafeZone nearestZone = FindAvailableSafeZone();
                if (nearestZone != null && distance < panicDistance)
                {
                    Vector2 toZone = ((Vector2)nearestZone.transform.position - (Vector2)characterTransform.position);
                    float zoneDistance = toZone.magnitude;

                    if (zoneDistance < 8f)
                    {
                        ExitStandoff();
                        currentDesiredDirection = ApplyWallAvoidance(toZone.normalized);
                        sprinting = true;
                        isRecoveringStamina = false;
                        ReportAIState(StatusIconDisplay.AIDecisionState.FleeingToZone);

                        if (distance < evasionTriggerDistance)
                        {
                            specialRequest = true;
                        }
                        return;
                    }
                }

                // Evaluate: should we enter standoff or keep running?
                if (!isInStandoff && ShouldEnterStandoff(distance, fromThreat))
                {
                    EnterStandoff();
                }

                if (isInStandoff)
                {
                    DecideAsStandoff(distance, fromThreat);
                    ReportAIState(StatusIconDisplay.AIDecisionState.Standoff);
                    return;
                }

                // ── BARGE WINDOW ──
                // If chaser is close and barge is available, briefly charge through them.
                if (bargeChargeTimer > 0f)
                {
                    // Actively charging at the It player
                    currentDesiredDirection = -fromThreat.normalized;
                    sprinting = true;
                    isRecoveringStamina = false;
                    specialRequest = true;
                    ReportAIState(StatusIconDisplay.AIDecisionState.Fleeing);
                    return;
                }

                if (aiBargeCooldownTimer <= 0f && distance < AiBargeTriggerDist
                    && movement.BargeCooldownNormalized <= 0f)
                {
                    bargeChargeTimer = 0.3f;
                    aiBargeCooldownTimer = AiBargeCooldown;
                }

                // Normal flee with swerve
                Vector2 fleeDir = fromThreat.normalized;

                Vector2 toCenter = (arenaCenter - (Vector2)characterTransform.position);
                float distFromCenter = toCenter.magnitude;
                if (distFromCenter > 4f)
                {
                    float centerPull = Mathf.Clamp01((distFromCenter - 4f) / 4f) * 0.5f;
                    fleeDir = (fleeDir + toCenter.normalized * centerPull).normalized;
                }

                Vector2 perpendicular = Vector2.Perpendicular(fleeDir);
                float swerve = Mathf.Sin(Time.time * 2f + characterTransform.GetInstanceID()) * 0.3f;
                fleeDir = (fleeDir + perpendicular * swerve).normalized;

                currentDesiredDirection = ApplyWallAvoidance(fleeDir);
                currentDesiredDirection = ApplyJitter(currentDesiredDirection);
                ReportAIState(StatusIconDisplay.AIDecisionState.Fleeing);

                if (distance < evasionTriggerDistance)
                {
                    specialRequest = true;
                }

                if (distance < panicDistance)
                {
                    sprinting = true;
                    isRecoveringStamina = false;
                }
                else
                {
                    UpdateSprintDecision(distance, fleeDistance);
                }
            }
            else
            {
                ExitStandoff();
                Wander();
                sprinting = false;
                isRecoveringStamina = true;
                ReportAIState(StatusIconDisplay.AIDecisionState.Wandering);
            }
        }

        // ──────────────────────────────────────────────
        //  STANDOFF EVALUATION
        // ──────────────────────────────────────────────

        /// <summary>
        /// Evaluate whether to enter standoff mode.
        /// Triggers when: cornered (walls blocking escape), low stamina,
        /// or threat is too close to outrun.
        /// </summary>
        private bool ShouldEnterStandoff(float threatDistance, Vector2 fromThreat)
        {
            // Only consider standoff when threat is close
            if (threatDistance > standoffEvalDistance) return false;

            // Low stamina — can't outrun, better to stand and fake
            float normalizedStamina = movement.GetNormalizedStamina();
            if (normalizedStamina < standoffMinStamina)
                return true;

            // Cornered — count blocked escape routes
            int blockedRoutes = CountBlockedEscapeRoutes(fromThreat);
            if (blockedRoutes >= standoffWallThreshold)
                return true;

            return false;
        }

        /// <summary>
        /// Cast rays in escape directions to check how many are blocked by walls.
        /// </summary>
        private int CountBlockedEscapeRoutes(Vector2 fromThreat)
        {
            int blocked = 0;
            int rayCount = 6;
            Vector2 fleeDir = fromThreat.normalized;

            // Cast rays in a fan from the flee direction
            for (int i = 0; i < rayCount; i++)
            {
                float angle = -90f + (180f / (rayCount - 1)) * i; // -90 to +90 relative to flee
                Vector2 rayDir = (Quaternion.Euler(0, 0, angle) * fleeDir).normalized;

                RaycastHit2D hit = Physics2D.Raycast(
                    characterTransform.position, rayDir, 3f, wallLayer);

                if (hit.collider != null)
                {
                    blocked++;
                }
            }

            return blocked;
        }

        // ──────────────────────────────────────────────
        //  STANDOFF BEHAVIOR
        // ──────────────────────────────────────────────

        private void EnterStandoff()
        {
            isInStandoff = true;
            standoffLeanTimer = 0f;
            standoffEscapeTimer = 0f;
            standoffBurstCommitted = false;
            currentLeanSide = 0;
            shuffling = true; // enter defensive stance
        }

        private void ExitStandoff()
        {
            if (!isInStandoff) return;
            isInStandoff = false;
            standoffBurstCommitted = false;
            currentLeanSide = 0;
            shuffling = false;
        }

        /// <summary>
        /// Standoff behavior: face the threat, maintain distance,
        /// lean side to side to fake, then burst when the moment is right.
        /// </summary>
        private void DecideAsStandoff(float threatDistance, Vector2 fromThreat)
        {
            shuffling = true;
            sprinting = false;

            // Periodically re-evaluate if we can escape
            standoffEscapeTimer -= Time.deltaTime;
            if (standoffEscapeTimer <= 0f)
            {
                standoffEscapeTimer = standoffEscapeCheckInterval;

                // If we've recovered stamina or escape routes opened, break out
                float normalizedStamina = movement.GetNormalizedStamina();
                int blockedRoutes = CountBlockedEscapeRoutes(fromThreat);

                if (normalizedStamina > standoffMinStamina + 0.2f && blockedRoutes < standoffWallThreshold)
                {
                    // Found an opening — burst out!
                    standoffBurstCommitted = true;
                }
            }

            // BURST — commit to an escape direction
            if (standoffBurstCommitted)
            {
                ExitStandoff();
                sprinting = true;

                // Burst in the best available direction
                Vector2 bestEscape = FindBestEscapeDirection(fromThreat);
                currentDesiredDirection = bestEscape;
                return;
            }

            // MAINTAIN DISTANCE — drift backward if too close, forward if too far
            Vector2 toThreat = -fromThreat.normalized;
            if (threatDistance < standoffMaintainDistance * 0.7f)
            {
                // Too close — back up
                currentDesiredDirection = fromThreat.normalized * 0.5f;
            }
            else if (threatDistance > standoffMaintainDistance * 1.3f)
            {
                // Too far — close the gap slightly (confident stance)
                currentDesiredDirection = toThreat * 0.2f;
            }
            else
            {
                // Good distance — lean side to side
                currentDesiredDirection = Vector2.zero;
            }

            // LEAN FEINTS — side to side to bait the chaser
            standoffLeanTimer -= Time.deltaTime;
            if (standoffLeanTimer <= 0f)
            {
                // Pick a new lean direction
                float id = character != null ? character.GetInstanceID() : 0f;
                float randomVal = Mathf.PerlinNoise(Time.time * 3f, id * 0.1f);

                if (randomVal < 0.35f)
                    currentLeanSide = -1; // lean left
                else if (randomVal > 0.65f)
                    currentLeanSide = 1;  // lean right
                else
                    currentLeanSide = 0;  // center (pause between fakes)

                standoffLeanTimer = standoffLeanInterval
                    + Random.Range(-0.1f, 0.15f); // slight timing variation

                // Random chance to commit to a burst
                if (Random.value < standoffBurstChance && threatDistance < standoffMaintainDistance)
                {
                    standoffBurstCommitted = true;
                }
            }

            // Apply lean as lateral input relative to facing
            if (currentLeanSide != 0 && !standoffBurstCommitted)
            {
                Vector2 lateral = Vector2.Perpendicular(fromThreat.normalized) * currentLeanSide;
                currentDesiredDirection = (currentDesiredDirection + lateral * 0.7f);
                if (currentDesiredDirection.sqrMagnitude > 0.01f)
                    currentDesiredDirection = currentDesiredDirection.normalized;
            }
        }

        /// <summary>
        /// Find the best escape direction by raycasting and picking
        /// the most open direction away from the threat.
        /// </summary>
        private Vector2 FindBestEscapeDirection(Vector2 fromThreat)
        {
            Vector2 bestDir = fromThreat.normalized;
            float bestScore = 0f;
            int rayCount = 8;

            for (int i = 0; i < rayCount; i++)
            {
                float angle = (360f / rayCount) * i;
                Vector2 rayDir = (Quaternion.Euler(0, 0, angle) * Vector2.right).normalized;

                // Score based on: open space AND away from threat
                float awayFromThreat = Vector2.Dot(rayDir, fromThreat.normalized);
                float openSpace = 1f;

                RaycastHit2D hit = Physics2D.Raycast(
                    characterTransform.position, rayDir, 5f, wallLayer);

                if (hit.collider != null)
                {
                    openSpace = hit.distance / 5f;
                }

                // Bias toward center
                Vector2 toCenter = (arenaCenter - (Vector2)characterTransform.position).normalized;
                float centerBias = Vector2.Dot(rayDir, toCenter) * 0.3f;

                float score = (awayFromThreat * 0.5f) + (openSpace * 0.3f) + centerBias;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestDir = rayDir;
                }
            }

            return bestDir;
        }

        // ──────────────────────────────────────────────
        //  INTERCEPT / CUTOFF HELPERS
        // ──────────────────────────────────────────────

        /// <summary>
        /// Predict where the target will be based on their current velocity,
        /// so we steer toward the intercept point rather than chasing their
        /// current position. Caps lookahead to avoid wild predictions.
        /// </summary>
        private Vector2 GetInterceptPoint(Transform target, float myEffectiveSpeed)
        {
            var targetRb = target.GetComponent<Rigidbody2D>();
            if (targetRb == null) return target.position;

            Vector2 targetVelocity = targetRb.linearVelocity;

            // No meaningful movement to predict from
            if (targetVelocity.sqrMagnitude < 1f)
                return target.position;

            float distance = Vector2.Distance(characterTransform.position, target.position);
            if (myEffectiveSpeed < 0.1f) return target.position;

            float timeToReach = Mathf.Clamp(distance / myEffectiveSpeed, 0f, interceptLookaheadCap);
            return (Vector2)target.position + targetVelocity * timeToReach;
        }

        /// <summary>
        /// Returns true if the runner appears to be heading toward a safe zone,
        /// and sets targetZone to the zone they're aiming for.
        /// Uses runner velocity alignment with zone direction as the signal.
        /// </summary>
        private bool IsRunnerHeadingToZone(Transform runner, out SafeZone targetZone)
        {
            targetZone = null;

            var runnerRb = runner.GetComponent<Rigidbody2D>();
            if (runnerRb == null) return false;

            Vector2 runnerVelocity = runnerRb.linearVelocity;
            if (runnerVelocity.sqrMagnitude < 1f) return false;

            Vector2 runnerDir = runnerVelocity.normalized;
            float closestAlignedDist = float.MaxValue;

            var zones = Object.FindObjectsByType<SafeZone>(FindObjectsSortMode.None);
            foreach (var zone in zones)
            {
                if (zone.IsOnCooldown) continue;  // skip disabled zones; Ready zones are valid targets

                Vector2 toZone = (Vector2)zone.transform.position - (Vector2)runner.position;
                float zoneDist = toZone.magnitude;

                if (zoneDist > cutoffZoneScanRadius) continue;

                float alignment = Vector2.Dot(runnerDir, toZone.normalized);
                if (alignment > cutoffAlignmentThreshold && zoneDist < closestAlignedDist)
                {
                    closestAlignedDist = zoneDist;
                    targetZone = zone;
                }
            }

            return targetZone != null;
        }

        /// <summary>
        /// Get the direction to move to intercept the runner before they reach a safe zone.
        /// If we're already between the runner and the zone, just chase directly.
        /// Otherwise, aim for a point 65% of the way from runner to zone to cut the angle.
        /// cutoffPoint is the world-space target used (for debug visualization).
        /// </summary>
        private Vector2 GetCutoffDirection(Transform runner, SafeZone zone, out Vector2 cutoffPoint)
        {
            Vector2 runnerPos = runner.position;
            Vector2 zonePos = zone.transform.position;
            Vector2 myPos = characterTransform.position;

            // If we're already closer to the zone than the runner is, just chase them directly
            float myDistToZone = Vector2.Distance(myPos, zonePos);
            float runnerDistToZone = Vector2.Distance(runnerPos, zonePos);
            if (myDistToZone < runnerDistToZone * 0.85f)
            {
                cutoffPoint = runnerPos;
                return (runnerPos - myPos).normalized;
            }

            // Aim for a point between runner and zone to get in their path
            cutoffPoint = Vector2.Lerp(runnerPos, zonePos, 0.65f);
            Vector2 dir = cutoffPoint - myPos;
            return dir.sqrMagnitude > 0.01f ? dir.normalized : (runnerPos - myPos).normalized;
        }

        // ──────────────────────────────────────────────
        //  TARGET SCANNING
        // ──────────────────────────────────────────────

        private void ScanForTargets()
        {
            chaseTarget = null;
            threatSource = null;

            // Clear scores from runners that may have left awareness range
            foreach (var display in scoredRunners)
                if (display != null) display.ClearTargetScore();
            scoredRunners.Clear();

            Collider2D[] hits = Physics2D.OverlapCircleAll(
                characterTransform.position, awarenessRadius, playerLayer);

            float bestRunnerScore = float.MaxValue;
            float closestItDist = float.MaxValue;

            foreach (var hit in hits)
            {
                if (hit.gameObject == character) continue;

                var other = hit.GetComponent<TagMovementController>();
                if (other == null) continue;
                if (other.IsEliminated) continue;

                float dist = Vector2.Distance(characterTransform.position, other.transform.position);

                if (other.CurrentRole == TagMovementController.TagRole.Runner)
                {
                    // Skip runners already in safe zones — untouchable
                    if (other.InSafeZone) continue;

                    float score = ScoreRunnerTarget(other, dist);

                    // Display the score on the runner for debugging
                    var display = other.GetComponent<StatusIconDisplay>();
                    if (display != null)
                    {
                        display.SetTargetScore(score);
                        scoredRunners.Add(display);
                    }

                    if (score < bestRunnerScore)
                    {
                        bestRunnerScore = score;
                        chaseTarget = other.transform;
                    }
                }

                if (other.CurrentRole == TagMovementController.TagRole.It
                    && dist < closestItDist)
                {
                    closestItDist = dist;
                    threatSource = other.transform;
                }
            }
        }

        /// <summary>
        /// Score a runner as a chase target. Lower = more attractive.
        /// Factors: proximity (closer is better), distance to nearest safe zone
        /// (penalize runners near safety), and current stamina (prefer tired runners).
        /// </summary>
        private float ScoreRunnerTarget(TagMovementController runner, float distance)
        {
            float score = distance;

            // Penalize runners close to a usable safe zone — they can shelter quickly
            var zones = Object.FindObjectsByType<SafeZone>(FindObjectsSortMode.None);
            float nearestZoneDist = float.MaxValue;
            foreach (var zone in zones)
            {
                if (zone.IsOnCooldown) continue;
                float d = Vector2.Distance(runner.transform.position, zone.transform.position);
                if (d < nearestZoneDist) nearestZoneDist = d;
            }
            if (nearestZoneDist < float.MaxValue)
            {
                // Zone within ~4 units starts penalizing; max penalty at zone edge
                float zonePenalty = Mathf.Clamp(4f - nearestZoneDist, 0f, 4f) * 2.5f;
                score += zonePenalty;
            }

            // Prefer runners low on stamina — they're slower and can't burst away
            float staminaBonus = (1f - runner.GetNormalizedStamina()) * 3f;
            score -= staminaBonus;

            return score;
        }

        // ──────────────────────────────────────────────
        //  SPRINT MANAGEMENT
        // ──────────────────────────────────────────────

        private void UpdateSprintDecision(float targetDistance, float sprintTriggerDist)
        {
            float stamina = movement.GetNormalizedStamina();

            if (isRecoveringStamina)
            {
                if (stamina >= sprintRecoveryThreshold)
                    isRecoveringStamina = false;
                else
                {
                    sprinting = false;
                    return;
                }
            }

            bool shouldSprint = targetDistance <= sprintTriggerDist
                                && stamina > sprintStaminaThreshold;

            if (!shouldSprint && stamina <= sprintStaminaThreshold)
                isRecoveringStamina = true;

            sprinting = shouldSprint;
        }

        // ──────────────────────────────────────────────
        //  WALL AVOIDANCE
        // ──────────────────────────────────────────────

        private Vector2 ApplyWallAvoidance(Vector2 desiredDir)
        {
            Vector2 avoidance = Vector2.zero;
            int rayCount = 8;

            for (int i = 0; i < rayCount; i++)
            {
                float angle = (360f / rayCount) * i;
                Vector2 rayDir = Quaternion.Euler(0, 0, angle) * Vector2.right;

                RaycastHit2D hit = Physics2D.Raycast(
                    characterTransform.position, rayDir, wallDetectDistance, wallLayer);

                if (hit.collider != null)
                {
                    float proximity = 1f - (hit.distance / wallDetectDistance);
                    avoidance -= rayDir * proximity;
                }
            }

            if (avoidance.sqrMagnitude > 0.01f)
                return (desiredDir + avoidance.normalized * wallAvoidanceStrength).normalized;

            return desiredDir;
        }

        // ──────────────────────────────────────────────
        //  SAFE ZONE AVOIDANCE (when It)
        // ──────────────────────────────────────────────

        private float zoneAvoidanceRadius = 4f;
        private float zoneAvoidanceStrength = 0.8f;

        /// <summary>
        /// When It, steer around active safe zones to avoid pausing their timers.
        /// Only avoids zones that are currently protecting runners.
        /// </summary>
        private Vector2 ApplyZoneAvoidance(Vector2 desiredDir)
        {
            if (movement.CurrentRole != TagMovementController.TagRole.It)
                return desiredDir;

            var zones = Object.FindObjectsByType<SafeZone>(FindObjectsSortMode.None);
            Vector2 avoidance = Vector2.zero;

            foreach (var zone in zones)
            {
                // Only avoid active zones with runners inside
                if (!zone.IsActive || zone.RunnerCount == 0) continue;

                Vector2 toZone = (Vector2)zone.transform.position - (Vector2)characterTransform.position;
                float distance = toZone.magnitude;

                if (distance < zoneAvoidanceRadius)
                {
                    // Push away from zone, stronger when closer
                    float proximity = 1f - (distance / zoneAvoidanceRadius);
                    avoidance -= toZone.normalized * proximity;
                }
            }

            if (avoidance.sqrMagnitude > 0.01f)
                return (desiredDir + avoidance.normalized * zoneAvoidanceStrength).normalized;

            return desiredDir;
        }

        // ──────────────────────────────────────────────
        //  WANDER
        // ──────────────────────────────────────────────

        private Vector2 arenaCenter = Vector2.zero;
        private float centerBiasStrength = 0.4f;

        private void Wander()
        {
            float id = character != null ? character.GetInstanceID() * 0.1f : 0f;
            float wanderAngle = Mathf.PerlinNoise(Time.time * 0.5f, id) * 360f;
            Vector2 wanderDir = new Vector2(
                Mathf.Cos(wanderAngle * Mathf.Deg2Rad),
                Mathf.Sin(wanderAngle * Mathf.Deg2Rad));

            // Bias toward arena center to avoid drifting into corners
            Vector2 toCenter = (arenaCenter - (Vector2)characterTransform.position);
            float distFromCenter = toCenter.magnitude;
            if (distFromCenter > 3f)
            {
                // Stronger pull the further from center
                float pull = Mathf.Clamp01((distFromCenter - 3f) / 5f) * centerBiasStrength;
                wanderDir = (wanderDir + toCenter.normalized * pull).normalized;
            }

            currentDesiredDirection = ApplyWallAvoidance(wanderDir);
            sprinting = false;
        }

        // ──────────────────────────────────────────────
        //  JITTER
        // ──────────────────────────────────────────────

        private Vector2 ApplyJitter(Vector2 direction)
        {
            float jitter = Random.Range(-movementJitter, movementJitter);
            return (Quaternion.Euler(0, 0, jitter) * direction).normalized;
        }

        // ──────────────────────────────────────────────
        //  UTILITY
        // ──────────────────────────────────────────────

        // ──────────────────────────────────────────────
        //  DEBUG VISUALIZATION
        // ──────────────────────────────────────────────

        private void UpdateDebugVisuals()
        {
            if (characterTransform == null) return;

            Vector3 origin = characterTransform.position;

            // Green line → intercept point (where the runner will be)
            if (interceptLine != null)
            {
                interceptLine.enabled = debugShowIntercept;
                if (debugShowIntercept)
                {
                    interceptLine.SetPosition(0, origin);
                    interceptLine.SetPosition(1, debugInterceptPoint);
                }
            }

            // Orange line → cutoff point (blocking path to safe zone)
            if (cutoffLine != null)
            {
                cutoffLine.enabled = debugShowCutoff;
                if (debugShowCutoff)
                {
                    cutoffLine.SetPosition(0, origin);
                    cutoffLine.SetPosition(1, debugCutoffPoint);
                }
            }
        }

        private void ClearOutputs()
        {
            currentDesiredDirection = Vector2.zero;
            sprinting = false;
            shuffling = false;
            jumpRequest = false;
            diveRequest = false;
            specialRequest = false;
            tagRequest = false;
            bargeChargeTimer = 0f;
        }

        private void ReportAIState(StatusIconDisplay.AIDecisionState state)
        {
            if (statusIcons != null)
                statusIcons.SetAIDecisionState(state);
        }

        /// <summary>
        /// Find the nearest safe zone that this player can use (not on cooldown).
        /// Returns null if no zones are available.
        /// </summary>
        private SafeZone FindAvailableSafeZone()
        {
            var zones = Object.FindObjectsByType<SafeZone>(FindObjectsSortMode.None);
            SafeZone nearest = null;
            float nearestDist = float.MaxValue;

            foreach (var zone in zones)
            {
                if (!zone.IsAvailableForPlayer(movement)) continue;

                float dist = Vector2.Distance(characterTransform.position, zone.transform.position);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = zone;
                }
            }

            return nearest;
        }
    }
}