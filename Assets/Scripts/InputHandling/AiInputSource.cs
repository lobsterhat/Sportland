using UnityEngine;
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
        private float awarenessRadius = 15f;
        private LayerMask playerLayer;

        [Header("=== CHASE BEHAVIOR ===")]
        private float sprintChaseDistance = 8f;
        private float lungeAttemptDistance = 2.5f;
        private float tagAttemptDistance = 1.5f;
        private float sprintStaminaThreshold = 0.3f;
        private float sprintRecoveryThreshold = 0.6f;

        [Header("=== FLEE BEHAVIOR ===")]
        private float fleeDistance = 7f;
        private float evasionTriggerDistance = 2.5f;
        private float panicDistance = 3f;

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

        // Current frame outputs
        private Vector2 currentDesiredDirection;
        private bool sprinting;
        private bool jumpRequest;
        private bool diveRequest;
        private bool specialRequest;
        private bool tagRequest;

        // Cached targets
        private Transform chaseTarget;
        private Transform threatSource;

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

        public void OnActivate(GameObject character)
        {
            this.character = character;
            this.characterTransform = character.transform;
            this.movement = character.GetComponent<TagMovementController>();
            this.decisionTimer = 0f;
            this.isRecoveringStamina = false;

            ClearOutputs();
        }

        public void OnDeactivate()
        {
            ClearOutputs();
            character = null;
            characterTransform = null;
            movement = null;
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
                return;
            }

            Vector2 toTarget = (Vector2)(chaseTarget.position - characterTransform.position);
            float distance = toTarget.magnitude;
            Vector2 direction = toTarget.normalized;

            // Try tag if very close
            if (distance <= tagAttemptDistance)
            {
                tagRequest = true;
            }

            // Try lunge if in range
            if (distance <= lungeAttemptDistance && distance > tagAttemptDistance)
            {
                specialRequest = true;
            }

            // Steering
            currentDesiredDirection = ApplyWallAvoidance(direction);
            currentDesiredDirection = ApplyJitter(currentDesiredDirection);

            // Sprint management
            UpdateSprintDecision(distance, sprintChaseDistance);
        }

        // ──────────────────────────────────────────────
        //  RUNNER AI
        // ──────────────────────────────────────────────

        private void DecideAsRunner()
        {
            if (threatSource == null)
            {
                Wander();
                sprinting = false;
                return;
            }

            Vector2 fromThreat = (Vector2)(characterTransform.position - threatSource.position);
            float distance = fromThreat.magnitude;

            if (distance < fleeDistance)
            {
                // Flee with swerve
                Vector2 fleeDir = fromThreat.normalized;
                Vector2 perpendicular = Vector2.Perpendicular(fleeDir);
                float swerve = Mathf.Sin(Time.time * 2f) * 0.3f;
                fleeDir = (fleeDir + perpendicular * swerve).normalized;

                currentDesiredDirection = ApplyWallAvoidance(fleeDir);
                currentDesiredDirection = ApplyJitter(currentDesiredDirection);

                // Evasion burst when threatened
                if (distance < evasionTriggerDistance)
                {
                    specialRequest = true;
                }

                // Panic sprint
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
                Wander();
                sprinting = false;
                isRecoveringStamina = true;
            }
        }

        // ──────────────────────────────────────────────
        //  TARGET SCANNING
        // ──────────────────────────────────────────────

        private void ScanForTargets()
        {
            chaseTarget = null;
            threatSource = null;

            Collider2D[] hits = Physics2D.OverlapCircleAll(
                characterTransform.position, awarenessRadius, playerLayer);

            float closestRunnerDist = float.MaxValue;
            float closestItDist = float.MaxValue;

            foreach (var hit in hits)
            {
                if (hit.gameObject == character) continue;

                var other = hit.GetComponent<TagMovementController>();
                if (other == null) continue;
                if (other.IsEliminated) continue;

                float dist = Vector2.Distance(characterTransform.position, other.transform.position);

                if (other.CurrentRole == TagMovementController.TagRole.Runner
                    && dist < closestRunnerDist)
                {
                    closestRunnerDist = dist;
                    chaseTarget = other.transform;
                }

                if (other.CurrentRole == TagMovementController.TagRole.It
                    && dist < closestItDist)
                {
                    closestItDist = dist;
                    threatSource = other.transform;
                }
            }
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
        //  WANDER
        // ──────────────────────────────────────────────

        private void Wander()
        {
            float id = character != null ? character.GetInstanceID() * 0.1f : 0f;
            float wanderAngle = Mathf.PerlinNoise(Time.time * 0.5f, id) * 360f;
            Vector2 wanderDir = new Vector2(
                Mathf.Cos(wanderAngle * Mathf.Deg2Rad),
                Mathf.Sin(wanderAngle * Mathf.Deg2Rad));

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

        private void ClearOutputs()
        {
            currentDesiredDirection = Vector2.zero;
            sprinting = false;
            jumpRequest = false;
            diveRequest = false;
            specialRequest = false;
            tagRequest = false;
        }
    }
}