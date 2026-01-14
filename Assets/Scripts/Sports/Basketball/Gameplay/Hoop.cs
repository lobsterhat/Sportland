using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Sportland.Sports.Basketball.Stats;

namespace Sportland.Sports.Basketball.Gameplay
{
    public class Hoop : MonoBehaviour
    {
        [Header("Court Position")]
        [SerializeField] private Vector2 courtPosition = new(13.11f, 0);
        [SerializeField] private float rimHeight = 2.5f;
        [SerializeField] private Vector2 rimScale = new Vector2(0.73f, 0.57f);

        [Header("Scoring")]
        [SerializeField] private int points = 2;

        [Header("Rim Physics")]
        [SerializeField] private float timeBetweenRimContacts = 0.15f;

        public Vector2 CourtPosition => courtPosition;
        public float RimHeight => rimHeight;

        private Ball ball;
        private float ballPreviousHeight = 0f;

        private ShotOutcome currentOutcome;
        private bool waitingForBall = false;
        private bool processingRimSequence = false;

        private void Awake()
        {
            transform.position = courtPosition;
        }

        private void Start()
        {
            ball = FindAnyObjectByType<Ball>();
        }

        private void Update()
        {
            if (ball == null || ball.isHeld)
            {
                ResetState();
                return;
            }

            if (waitingForBall && !processingRimSequence)
            {
                CheckBallArrival();
            }

            ballPreviousHeight = ball.height;
        }

        private void ResetState()
        {
            currentOutcome = null;
            waitingForBall = false;
            processingRimSequence = false;
            ballPreviousHeight = 0f;
            StopAllCoroutines();
        }

        public void SetShotOutcome(ShotOutcome outcome)
        {
            currentOutcome = outcome;
            waitingForBall = true;
            processingRimSequence = false;
        }

        private void CheckBallArrival()
        {
            if (currentOutcome == null) return;

            bool isFalling = ball.verticalVelocity < 0;
            bool crossingRimHeight = ballPreviousHeight > rimHeight && ball.height <= rimHeight;

            if (crossingRimHeight && isFalling)
            {
                // For Swish, validate ball is within rim boundaries
                if (currentOutcome.result == ShotResult.Swish)
                {
                    float halfWidth = rimScale.x / 2f;
                    float halfDepth = rimScale.y / 2f;

                    // Account for ball radius - if ball center is within (rim + ball radius),
                    // the ball can pass through the rim opening
                    float ballRadius = ball.radius;
                    bool withinRimX = Mathf.Abs(ball.courtPosition.x - courtPosition.x) <= (halfWidth + ballRadius);
                    bool withinRimY = Mathf.Abs(ball.courtPosition.y - courtPosition.y) <= (halfDepth + ballRadius);

                    if (withinRimX && withinRimY)
                    {
                        StartCoroutine(ProcessRimSequence());
                    }
                    // else: ball missed the rim - wait for next frame
                }
                else
                {
                    // Not a swish - process rim contacts regardless of position
                    StartCoroutine(ProcessRimSequence());
                }
            }
        }

        private IEnumerator ProcessRimSequence()
        {
            processingRimSequence = true;

            // Swish - ball goes straight through
            if (currentOutcome.result == ShotResult.Swish)
            {
                Score();
                yield break;
            }

            // Process each rim contact - apply bounces instead of teleporting
            for (int i = 0; i < currentOutcome.rimContacts.Count; i++)
            {
                RimContact contact = currentOutcome.rimContacts[i];

                // Wait for ball to naturally reach this contact point
                IEnumerator waitCoroutine = WaitForRimContact(contact);
                yield return StartCoroutine(waitCoroutine);

                // Check if we successfully reached the contact (true) or timed out (false)
                bool reachedContact = (bool)waitCoroutine.Current;

                if (!reachedContact)
                {
                    // Timed out - skip this contact and continue with next one
                    continue;
                }

                // Determine if this is a make and if there are more contacts
                bool isMake = (currentOutcome.result == ShotResult.RimAndIn || currentOutcome.result == ShotResult.BackboardAndIn);
                bool hasMoreContacts = i < currentOutcome.rimContacts.Count - 1;

                // Get next contact point if available
                Vector2? nextContactPoint = null;
                if (hasMoreContacts)
                {
                    nextContactPoint = GetRimContactPoint(currentOutcome.rimContacts[i + 1]);
                }

                // Apply bounce at this contact
                ApplyRimBounce(contact, isMake, nextContactPoint);

                Debug.Log($"RIM HIT: {contact} at ({ball.courtPosition.x:F2}, {ball.courtPosition.y:F2}, h:{ball.height:F2})");

                // Small delay between contacts to make bounces visible
                if (hasMoreContacts)
                {
                    yield return new WaitForSeconds(timeBetweenRimContacts);
                }
            }

            // Sequence complete - determine final outcome
            if (currentOutcome.result == ShotResult.Miss)
            {
                FinishMiss();
            }
            else
            {
                // For makes, validate ball is actually near the hoop before scoring
                float distanceFromHoop = Vector2.Distance(ball.courtPosition, courtPosition);
                bool nearHoop = distanceFromHoop < 0.5f; // Within half unit of hoop center
                bool atRimHeight = Mathf.Abs(ball.height - rimHeight) < 0.6f; // Within 0.6 units of rim height

                if (nearHoop && atRimHeight)
                {
                    Score();
                }
                else
                {
                    Debug.Log($"Ball not near hoop after rim sequence - treating as miss. Distance: {distanceFromHoop:F2}, Height diff: {Mathf.Abs(ball.height - rimHeight):F2}");
                    FinishMiss();
                }
            }
        }

        private IEnumerator WaitForRimContact(RimContact contact)
        {
            Vector2 contactPoint = GetRimContactPoint(contact);
            float timeout = 2f; // Safety timeout
            float elapsed = 0f;

            // Wait until ball is near the contact point AND at rim height
            while (elapsed < timeout)
            {
                float distanceToContact = Vector2.Distance(ball.courtPosition, contactPoint);
                bool nearContact = distanceToContact < 0.4f;
                bool atRimHeight = Mathf.Abs(ball.height - rimHeight) < 0.4f;

                // Require BOTH conditions - ball must be near the contact point and at rim height
                if (nearContact && atRimHeight)
                {
                    yield return true; // Successfully reached contact
                    yield break;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            // Timeout - ball didn't reach this contact point
            Debug.Log($"TIMEOUT waiting for {contact} - skipping this contact");
            yield return false; // Failed to reach contact
        }

        private void ApplyRimBounce(RimContact contact, bool isMake, Vector2? nextContactPoint)
        {
            // Get the rim surface normal at contact point
            Vector2 surfaceNormal = GetRimSurfaceNormal(contact);

            // Get ball's current velocity (combine court and vertical velocity)
            Vector2 incomingVelocity = ball.courtVelocity;

            // Physics-based bounce using coefficient of restitution
            float restitution = 0.75f;  // Basketball coefficient of restitution
            float friction = 0.95f;      // Tangential friction preservation

            // Decompose velocity into normal and tangential components
            float normalSpeed = Vector2.Dot(incomingVelocity, surfaceNormal);
            Vector2 normalVelocity = surfaceNormal * normalSpeed;
            Vector2 tangentialVelocity = incomingVelocity - normalVelocity;

            // Apply physics: reflect normal component with restitution, preserve tangential with friction
            Vector2 reflectedNormal = -normalVelocity * restitution;
            Vector2 preservedTangential = tangentialVelocity * friction;
            Vector2 physicsVelocity = reflectedNormal + preservedTangential;

            // For makes, ensure ball heads toward next contact or hoop center
            if (isMake)
            {
                Vector2 targetPosition;
                float blendStrength;
                float minVerticalVelocity;

                if (nextContactPoint.HasValue)
                {
                    // Next rim contact exists - guide strongly toward it
                    targetPosition = nextContactPoint.Value;
                    blendStrength = 0.85f; // Blend 85% toward target, 15% physics
                    minVerticalVelocity = 1.5f; // Pop up to give time to reach next contact
                }
                else
                {
                    // Last contact - guide toward hoop center to drop in
                    targetPosition = courtPosition;
                    blendStrength = 0.7f; // Blend 70% toward target, 30% physics
                    minVerticalVelocity = 0.8f; // Gentle pop before falling through
                }

                // Calculate desired direction and speed
                Vector2 toTarget = targetPosition - ball.courtPosition;
                float distance = toTarget.magnitude;
                // Speed based on distance - closer targets need slower approach
                float targetSpeed = Mathf.Max(1.5f, Mathf.Min(3.0f, distance * 2.0f));
                Vector2 targetVelocity = toTarget.normalized * targetSpeed;

                // Blend between physics and target velocity
                ball.courtVelocity = Vector2.Lerp(physicsVelocity, targetVelocity, blendStrength);

                // Apply vertical bounce with minimum to ensure ball stays up
                ball.verticalVelocity = Mathf.Max(minVerticalVelocity, Mathf.Abs(ball.verticalVelocity) * 0.7f);
            }
            else
            {
                // Miss - use pure physics
                ball.courtVelocity = physicsVelocity;

                // Apply vertical bounce with energy retention
                ball.verticalVelocity = Mathf.Abs(ball.verticalVelocity) * 0.7f;
            }

            // Nudge ball slightly away from rim surface to prevent sticking
            ball.courtPosition += surfaceNormal * 0.05f;
        }

        private Vector2 GetRimContactPoint(RimContact contact)
        {
            float halfWidth = rimScale.x / 2f;
            float halfDepth = rimScale.y / 2f;

            switch (contact)
            {
                case RimContact.FrontRim:
                    return courtPosition + new Vector2(0, -halfDepth);
                case RimContact.BackRim:
                    return courtPosition + new Vector2(0, halfDepth);
                case RimContact.LeftRim:
                    return courtPosition + new Vector2(-halfWidth, 0);
                case RimContact.RightRim:
                    return courtPosition + new Vector2(halfWidth, 0);
                case RimContact.Backboard:
                    return courtPosition + new Vector2(0, halfDepth + 0.1f);
                default:
                    return courtPosition;
            }
        }

        private Vector2 GetRimSurfaceNormal(RimContact contact)
        {
            // Return the outward-facing normal vector for each rim surface
            // These point away from the hoop center
            switch (contact)
            {
                case RimContact.FrontRim:
                    return new Vector2(0, -1); // Points toward front of court
                case RimContact.BackRim:
                    return new Vector2(0, 1);  // Points toward back of court
                case RimContact.LeftRim:
                    return new Vector2(-1, 0); // Points left
                case RimContact.RightRim:
                    return new Vector2(1, 0);  // Points right
                case RimContact.Backboard:
                    return new Vector2(0, -1); // Backboard normal points toward front
                default:
                    return Vector2.up;
            }
        }

        private void Score()
        {
            waitingForBall = false;
            processingRimSequence = false;

            // Apply net physics - the net slows down the ball and affects trajectory
            if (ball != null)
            {
                Debug.Log($"BALL THROUGH HOOP at ({ball.courtPosition.x:F2}, {ball.courtPosition.y:F2}, h:{ball.height:F2})");

                // Net kills most horizontal movement - ball falls nearly straight down
                ball.courtVelocity *= 0.1f;

                // Net slows vertical velocity but ensures ball is falling
                ball.verticalVelocity *= 0.5f;
                if (ball.verticalVelocity > -2f)
                {
                    ball.verticalVelocity = -2f;
                }
            }

            BasketballGameController controller = FindAnyObjectByType<BasketballGameController>();
            if (controller != null)
                controller.AddScore(points);
        }

        private void FinishMiss()
        {

            waitingForBall = false;
            processingRimSequence = false;

            // Ball already has velocity from the last rim bounce (ApplyRimBounce)
            // Don't override it - let physics continue naturally
            // This allows backboard collisions to work correctly on rebounds
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere((Vector3)courtPosition, 0.5f);

            // Draw rim as oval using matrix transformation
            Gizmos.color = Color.red;
            Matrix4x4 oldMatrix = Gizmos.matrix;
            Vector3 rimCenter = new Vector3(courtPosition.x, courtPosition.y + rimHeight, 0);
            Gizmos.matrix = Matrix4x4.TRS(rimCenter, Quaternion.identity, new Vector3(rimScale.x, rimScale.y, 1f));
            Gizmos.DrawWireSphere(Vector3.zero, 0.5f);
            Gizmos.matrix = oldMatrix;
        }
    }
}