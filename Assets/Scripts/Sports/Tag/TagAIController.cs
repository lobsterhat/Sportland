using UnityEngine;

namespace Sportland.Sports.Tag
{
    /// <summary>
    /// AI controller for Tag game. Drop this on an AI player alongside
    /// TagMovementController (instead of PlayerInputHandler).
    /// 
    /// Behavior:
    ///   When It  → Chase nearest runner, sprint to close distance, lunge when in range
    ///   When Runner → Flee from It player, use evasion burst when threatened
    ///   Always → Manage stamina, avoid walls, make human-like decisions
    /// </summary>
    [RequireComponent(typeof(TagMovementController))]
    public class TagAIController : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        //  CONFIGURATION
        // ──────────────────────────────────────────────

        [Header("=== AWARENESS ===")]

        [Tooltip("How far the AI can 'see' to find targets or threats.")]
        [SerializeField] private float awarenessRadius = 15f;

        [Tooltip("Layer mask for finding other players.")]
        [SerializeField] private LayerMask playerLayer;

        [Header("=== CHASE BEHAVIOR (when It) ===")]

        [Tooltip("Distance at which AI starts sprinting toward target.")]
        [SerializeField] private float sprintChaseDistance = 8f;

        [Tooltip("Distance at which AI attempts a lunge.")]
        [SerializeField] private float lungeAttemptDistance = 2.5f;

        [Tooltip("Distance at which AI attempts a walk-up tag.")]
        [SerializeField] private float tagAttemptDistance = 1.5f;

        [Tooltip("Minimum stamina (normalized 0-1) before AI stops sprinting to recover.")]
        [SerializeField] private float sprintStaminaThreshold = 0.3f;

        [Tooltip("Stamina must recover to this level before sprinting again.")]
        [SerializeField] private float sprintRecoveryThreshold = 0.6f;

        [Header("=== FLEE BEHAVIOR (when Runner) ===")]

        [Tooltip("Distance from It player that triggers flee response.")]
        [SerializeField] private float fleeDistance = 7f;

        [Tooltip("Distance from It player that triggers evasion burst.")]
        [SerializeField] private float evasionTriggerDistance = 2.5f;

        [Tooltip("Distance from It player that triggers desperate sprint regardless of stamina.")]
        [SerializeField] private float panicDistance = 3f;

        [Header("=== WALL AVOIDANCE ===")]

        [Tooltip("Distance at which AI starts steering away from walls.")]
        [SerializeField] private float wallDetectDistance = 2f;

        [Tooltip("How strongly wall avoidance influences movement direction (0-1).")]
        [SerializeField] private float wallAvoidanceStrength = 0.6f;

        [Tooltip("Layer mask for walls/obstacles.")]
        [SerializeField] private LayerMask wallLayer;

        [Header("=== DECISION VARIETY ===")]

        [Tooltip("How often the AI recalculates its decision (seconds). " +
                 "Adds slight delay to feel more human.")]
        [SerializeField] private float decisionInterval = 0.15f;

        [Tooltip("Random angle jitter added to movement to avoid robotic straight-line paths (degrees).")]
        [SerializeField] private float movementJitter = 15f;

        // ──────────────────────────────────────────────
        //  RUNTIME STATE
        // ──────────────────────────────────────────────

        private TagMovementController movement;
        private float decisionTimer;
        private bool isRecoveringStamina; // flag to prevent sprint flickering
        private Vector2 currentDesiredDirection;

        // Cached targets
        private Transform chaseTarget;   // nearest runner (when It)
        private Transform threatSource;  // the It player (when Runner)

        // ──────────────────────────────────────────────
        //  INITIALIZATION
        // ──────────────────────────────────────────────

        private void Awake()
        {
            movement = GetComponent<TagMovementController>();
        }

        // ──────────────────────────────────────────────
        //  UPDATE LOOP
        // ──────────────────────────────────────────────

        private void Update()
        {
            if (movement.IsEliminated)
            {
                movement.SetMoveInput(Vector2.zero);
                movement.SetSprinting(false);
                return;
            }

            // Throttle decisions for performance and human-like feel
            decisionTimer -= Time.deltaTime;
            if (decisionTimer <= 0f)
            {
                decisionTimer = decisionInterval;
                MakeDecision();
            }

            // Always feed current direction to movement
            movement.SetMoveInput(currentDesiredDirection);
        }

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
        //  CHASER AI (when It)
        // ──────────────────────────────────────────────

        private void DecideAsChaser()
        {
            if (chaseTarget == null)
            {
                // No target found — wander
                Wander();
                return;
            }

            Vector2 toTarget = (Vector2)(chaseTarget.position - transform.position);
            float distance = toTarget.magnitude;
            Vector2 direction = toTarget.normalized;

            // Try tag if very close
            if (distance <= tagAttemptDistance)
            {
                movement.TryTag();
            }

            // Try lunge if in range and we can act
            if (distance <= lungeAttemptDistance && distance > tagAttemptDistance)
            {
                movement.TryLunge();
            }

            // Steering: move toward target with wall avoidance
            currentDesiredDirection = ApplyWallAvoidance(direction);
            currentDesiredDirection = ApplyJitter(currentDesiredDirection);

            // Sprint management
            UpdateSprintDecision(distance, sprintChaseDistance, true);
        }

        // ──────────────────────────────────────────────
        //  RUNNER AI (when Runner)
        // ──────────────────────────────────────────────

        private void DecideAsRunner()
        {
            if (threatSource == null)
            {
                // No threat — wander casually
                Wander();
                movement.SetSprinting(false);
                return;
            }

            Vector2 fromThreat = (Vector2)(transform.position - threatSource.position);
            float distance = fromThreat.magnitude;

            if (distance < fleeDistance)
            {
                // Flee away from threat
                Vector2 fleeDir = fromThreat.normalized;

                // Add perpendicular component to avoid running in straight lines
                // (makes it harder for chaser to predict path)
                Vector2 perpendicular = Vector2.Perpendicular(fleeDir);
                float swerve = Mathf.Sin(Time.time * 2f) * 0.3f; // oscillating swerve
                fleeDir = (fleeDir + perpendicular * swerve).normalized;

                currentDesiredDirection = ApplyWallAvoidance(fleeDir);
                currentDesiredDirection = ApplyJitter(currentDesiredDirection);

                // Evasion burst when threatened
                if (distance < evasionTriggerDistance)
                {
                    movement.TryEvasionBurst();
                }

                // Sprint management — panic sprint if very close
                if (distance < panicDistance)
                {
                    movement.SetSprinting(true);
                    isRecoveringStamina = false;
                }
                else
                {
                    UpdateSprintDecision(distance, fleeDistance, false);
                }
            }
            else
            {
                // Safe distance — jog casually, conserve stamina
                Wander();
                movement.SetSprinting(false);
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

            Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, awarenessRadius, playerLayer);

            float closestRunnerDist = float.MaxValue;
            float closestItDist = float.MaxValue;

            foreach (var hit in hits)
            {
                if (hit.gameObject == gameObject) continue;

                var other = hit.GetComponent<TagMovementController>();
                if (other == null) continue;
                if (other.IsEliminated) continue;

                float dist = Vector2.Distance(transform.position, other.transform.position);

                // Find nearest runner (for chasing)
                if (other.CurrentRole == TagMovementController.TagRole.Runner && dist < closestRunnerDist)
                {
                    closestRunnerDist = dist;
                    chaseTarget = other.transform;
                }

                // Find the It player (for fleeing)
                if (other.CurrentRole == TagMovementController.TagRole.It && dist < closestItDist)
                {
                    closestItDist = dist;
                    threatSource = other.transform;
                }
            }
        }

        // ──────────────────────────────────────────────
        //  SPRINT MANAGEMENT
        // ──────────────────────────────────────────────

        /// <summary>
        /// Handles sprint toggling with hysteresis to prevent flickering.
        /// </summary>
        private void UpdateSprintDecision(float targetDistance, float sprintTriggerDist, bool isChasing)
        {
            float stamina = movement.GetNormalizedStamina();

            // Hysteresis: once we start recovering, don't sprint until fully recovered
            if (isRecoveringStamina)
            {
                if (stamina >= sprintRecoveryThreshold)
                {
                    isRecoveringStamina = false;
                }
                else
                {
                    movement.SetSprinting(false);
                    return;
                }
            }

            // Check if we should sprint
            bool shouldSprint = false;

            if (isChasing)
            {
                // Sprint when target is within chase range and we have stamina
                shouldSprint = targetDistance <= sprintTriggerDist && stamina > sprintStaminaThreshold;
            }
            else
            {
                // Sprint when threat is close and we have stamina
                shouldSprint = targetDistance <= sprintTriggerDist && stamina > sprintStaminaThreshold;
            }

            if (!shouldSprint && stamina <= sprintStaminaThreshold)
            {
                isRecoveringStamina = true;
            }

            movement.SetSprinting(shouldSprint);
        }

        // ──────────────────────────────────────────────
        //  WALL AVOIDANCE
        // ──────────────────────────────────────────────

        private Vector2 ApplyWallAvoidance(Vector2 desiredDir)
        {
            Vector2 avoidance = Vector2.zero;

            // Cast rays in several directions to detect nearby walls
            int rayCount = 8;
            for (int i = 0; i < rayCount; i++)
            {
                float angle = (360f / rayCount) * i;
                Vector2 rayDir = Quaternion.Euler(0, 0, angle) * Vector2.right;

                RaycastHit2D hit = Physics2D.Raycast(transform.position, rayDir, wallDetectDistance, wallLayer);
                if (hit.collider != null)
                {
                    // Push away from wall, stronger when closer
                    float proximity = 1f - (hit.distance / wallDetectDistance);
                    avoidance -= rayDir * proximity;
                }
            }

            if (avoidance.sqrMagnitude > 0.01f)
            {
                return (desiredDir + avoidance.normalized * wallAvoidanceStrength).normalized;
            }

            return desiredDir;
        }

        // ──────────────────────────────────────────────
        //  WANDER
        // ──────────────────────────────────────────────

        private void Wander()
        {
            // Gently drift in a slowly changing direction
            float wanderAngle = Mathf.PerlinNoise(Time.time * 0.5f, gameObject.GetInstanceID() * 0.1f) * 360f;
            Vector2 wanderDir = new Vector2(Mathf.Cos(wanderAngle * Mathf.Deg2Rad), Mathf.Sin(wanderAngle * Mathf.Deg2Rad));

            currentDesiredDirection = ApplyWallAvoidance(wanderDir);
            movement.SetSprinting(false);
        }

        // ──────────────────────────────────────────────
        //  JITTER (humanization)
        // ──────────────────────────────────────────────

        private Vector2 ApplyJitter(Vector2 direction)
        {
            float jitter = Random.Range(-movementJitter, movementJitter);
            return (Quaternion.Euler(0, 0, jitter) * direction).normalized;
        }

        // ──────────────────────────────────────────────
        //  DEBUG
        // ──────────────────────────────────────────────

        private void OnDrawGizmosSelected()
        {
            // Awareness radius
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, awarenessRadius);

            // Flee distance
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, fleeDistance);

            // Wall detection
            Gizmos.color = Color.gray;
            Gizmos.DrawWireSphere(transform.position, wallDetectDistance);

            // Current desired direction
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, transform.position + (Vector3)currentDesiredDirection * 2f);
        }
    }
}