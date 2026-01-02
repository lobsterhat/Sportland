using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Sportland.Basketball
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
        [SerializeField] private float rimBounceVertical = 4f;
        [SerializeField] private float rimBounceHorizontal = 1.5f;
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

            Debug.Log($"Hoop received outcome: {outcome.result} with {outcome.rimContacts.Count} contacts");
        }

        private void CheckBallArrival()
        {
            if (currentOutcome == null) return;

            bool isFalling = ball.verticalVelocity < 0;
            bool crossingRimHeight = ballPreviousHeight > rimHeight && ball.height <= rimHeight;

            if (crossingRimHeight && isFalling)
            {
                StartCoroutine(ProcessRimSequence());
            }
        }

        private IEnumerator ProcessRimSequence()
        {
            processingRimSequence = true;

            // Stop ball movement during rim sequence
            ball.courtVelocity = Vector2.zero;
            ball.verticalVelocity = 0f;

            // Swish - ball goes straight through
            if (currentOutcome.result == ShotResult.Swish)
            {
                Debug.Log("SWISH!");
                Score();
                yield break;
            }

            // Process each rim contact
            for (int i = 0; i < currentOutcome.rimContacts.Count; i++)
            {
                RimContact contact = currentOutcome.rimContacts[i];
                Debug.Log($"Ball hit {contact}!");

                // Move ball to contact point on rim
                Vector2 contactPoint = GetRimContactPoint(contact);
                ball.courtPosition = contactPoint;
                ball.height = rimHeight;

                yield return new WaitForSeconds(timeBetweenRimContacts);
            }

            // Sequence complete - determine final outcome
            if (currentOutcome.result == ShotResult.Miss)
            {
                FinishMiss();
            }
            else
            {
                Score();
            }
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

        private Vector2 GetBounceDirection(RimContact contact)
        {
            float variance = Random.Range(-0.3f, 0.3f);

            switch (contact)
            {
                case RimContact.FrontRim:
                    return new Vector2(variance, -1f).normalized;
                case RimContact.BackRim:
                    return new Vector2(variance, 1f).normalized;
                case RimContact.LeftRim:
                    return new Vector2(-1f, variance).normalized;
                case RimContact.RightRim:
                    return new Vector2(1f, variance).normalized;
                case RimContact.Backboard:
                    return new Vector2(variance, -0.5f).normalized;
                default:
                    return Vector2.down;
            }
        }

        private void Score()
        {
            Debug.Log($"SCORE! +{points} points! ({currentOutcome.result})");

            waitingForBall = false;
            processingRimSequence = false;

            if (ball != null)
                ball.CaptureAtHoop(courtPosition, rimHeight);

            BasketballGameController controller = FindAnyObjectByType<BasketballGameController>();
            if (controller != null)
                controller.AddScore(points);
        }

        private void FinishMiss()
        {
            Debug.Log("Shot missed!");

            waitingForBall = false;
            processingRimSequence = false;

            // Get final bounce direction from last contact
            RimContact lastContact = currentOutcome.rimContacts.Count > 0
                ? currentOutcome.rimContacts[currentOutcome.rimContacts.Count - 1]
                : RimContact.FrontRim;

            Vector2 bounceDir = GetBounceDirection(lastContact);

            float energyMultiplier = Mathf.Pow(0.7f, currentOutcome.rimContacts.Count);
            float horizontalSpeed = rimBounceHorizontal * energyMultiplier * Random.Range(0.8f, 1.2f);
            float verticalSpeed = rimBounceVertical * energyMultiplier * Random.Range(0.8f, 1.2f);

            ball.courtVelocity = bounceDir * horizontalSpeed;
            ball.verticalVelocity = verticalSpeed;
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