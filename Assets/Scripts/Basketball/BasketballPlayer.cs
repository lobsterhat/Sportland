using UnityEngine;

namespace Sportland.Basketball
{
    public class BasketballPlayer : MonoBehaviour
    {
        [Header("Court Position")]
        public Vector2 courtPosition;

        [Header("Rendering")]
        public SpriteRenderer playerSprite;
        public SpriteRenderer shadowSprite;
        public float spriteHeightOffset = 0.6f;

        [Header("Movement")]
        public float moveSpeed = 5f;

        [Header("Shooting")]
        public Ball ball;
        public Transform targetHoop;

        [Header("Jump Mechanics")]
        public float baseJumpHeight = 0.6f;
        public float jumpSkillModifier = 0.06f;
        public float jumpGravity = -12f;
        public float jumpSkill = 5f;

        [Header("Jump State")]
        public bool isJumping = false;
        public float jumpHeight = 0f;
        public float jumpVelocity = 0f;
        public float jumpApex = 0f;
        public bool passedApex = false;
        private Vector2 jumpMomentum;

        [Header("Jump English (Stationary)")]
        public float maxDriftSpeed = 1.5f;
        public float driftAccuracyPenalty = 0.15f;
        public bool isStationaryJump = false;
        private Vector2 driftVelocity;
        private float totalDriftDistance = 0f;

        [Header("Moving Jump")]
        public float movingTowardPenalty = 0.05f;
        public float movingLateralPenalty = 0.15f;
        public float movingAwayPenalty = 0.35f;

        [Header("Ball Position")]
        public float ballOverheadOffset = 1.8f;

        [Header("Shot Accuracy")]
        public float apexWindow = 0.1f;
        public float risingPenalty = 0.1f;
        public float fallingPenalty = 0.25f;
        public float hangtimeFlagReduction = 0.5f;
        public bool hasHangtimeFlag = false;

        [Header("Player Skills")]
        public float shootingSkill = 70f;

        private Vector2 moveInput;

        private void Awake()
        {
            if (courtPosition == Vector2.zero)
            {
                courtPosition = new Vector2(transform.position.x, transform.position.y);
            }
        }

        private void Update()
        {
            HandleInput();
            HandleMovement();
            HandleJump();
            HandleBallPickup();
            UpdateBallPosition();
            UpdateRendering();
        }

        private void HandleInput()
        {
            if (Input.GetKeyDown(KeyCode.R) && ball != null && !ball.isHeld)
            {
                ResetBall();
                return;
            }

            moveInput.x = Input.GetAxisRaw("Horizontal");
            moveInput.y = Input.GetAxisRaw("Vertical");

            if (ball != null && ball.isHeld)
            {
                if (Input.GetKeyDown(KeyCode.Space) && !isJumping)
                {
                    StartJump();
                }

                if (Input.GetKeyUp(KeyCode.Space) && isJumping)
                {
                    ReleaseShot();
                }

                if (isJumping && Input.GetKey(KeyCode.Space) && Input.GetKeyDown(KeyCode.F))
                {
                    // TODO: Implement pass
                    Debug.Log("Pass while jumping!");
                }
            }
        }

        private void HandleMovement()
        {
            if (isJumping)
            {
                if (isStationaryJump)
                {
                    Vector2 frameDrift = driftVelocity * Time.deltaTime;
                    courtPosition += frameDrift;
                    totalDriftDistance += frameDrift.magnitude;
                }
                else
                {
                    courtPosition += jumpMomentum * Time.deltaTime;
                }
            }
            else
            {
                courtPosition += moveSpeed * Time.deltaTime * moveInput.normalized;
            }
        }

        private void HandleJump()
        {
            if (!isJumping) return;

            float previousHeight = jumpHeight;

            jumpVelocity += jumpGravity * Time.deltaTime;
            jumpHeight += jumpVelocity * Time.deltaTime;

            if (previousHeight < jumpHeight)
            {
                jumpApex = jumpHeight;
            }
            else if (!passedApex && jumpHeight < previousHeight)
            {
                passedApex = true;
                //Debug.Log($"Passed apex at height: {jumpApex:F2}");
            }

            if (isStationaryJump && moveInput.magnitude > 0.1f)
            {
                driftVelocity = moveInput.normalized * maxDriftSpeed;
            }

            if (jumpHeight <= 0f)
            {
                jumpHeight = 0f;
                isJumping = false;
                passedApex = false;
                jumpApex = 0f;
                jumpMomentum = Vector2.zero;
                isStationaryJump = false;
                driftVelocity = Vector2.zero;
                totalDriftDistance = 0f;
               // Debug.Log("Landed");
            }
        }

        private void StartJump()
        {
            CancelInvoke("ResetBall");  // Cancel any pending reset

            isJumping = true;
            passedApex = false;
            jumpApex = 0f;
            jumpHeight = 0f;

            float maxHeight = baseJumpHeight + (jumpSkill * jumpSkillModifier);
            jumpVelocity = Mathf.Sqrt(2f * Mathf.Abs(jumpGravity) * maxHeight);

            if (moveInput.magnitude < 0.1f)
            {
                isStationaryJump = true;
                jumpMomentum = Vector2.zero;
                driftVelocity = Vector2.zero;
                totalDriftDistance = 0f;
                //Debug.Log("Stationary jump - english available");
            }
            else
            {
                isStationaryJump = false;
                jumpMomentum = moveInput.normalized * moveSpeed;
                //Debug.Log("Moving jump - momentum carried");
            }
        }

        private void ReleaseShot()
        {
            if (ball == null || !ball.isHeld) return;

            //Debug.Log($"Shot released at height: {jumpHeight:F2}, Apex: {jumpApex:F2}, Passed apex: {passedApex}");

            ShootBall();
        }

        private void HandleBallPickup()
        {
            if (ball != null && !ball.isHeld && !isJumping)
            {
                float distanceToBall = Vector2.Distance(courtPosition, ball.courtPosition);
                if (distanceToBall < 1.0f && ball.height < 0.5f)
                {
                    ball.SetHolder(transform);
                    //Debug.Log("Picked up ball!");
                }
            }
        }

        private void UpdateBallPosition()
        {
            if (ball == null || !ball.isHeld) return;

            ball.courtPosition = courtPosition;
            ball.height = jumpHeight + ballOverheadOffset;
        }

        private float CalculateJumpAccuracyModifier()
        {
            if (!isJumping || jumpApex == 0f)
            {
                return 0f;
            }

            float distanceFromApex = jumpApex - jumpHeight;

            if (distanceFromApex <= apexWindow)
            {
                //Debug.Log("Perfect apex release!");
                return 0f;
            }

            if (!passedApex)
            {
                float risingAmount = distanceFromApex / jumpApex;
                float penalty = risingPenalty * risingAmount;
                //Debug.Log($"Rising shot, penalty: {penalty:F2}");
                return -penalty;
            }

            float fallingAmount = distanceFromApex / jumpApex;
            float basePenalty = fallingPenalty * fallingAmount;

            if (hasHangtimeFlag)
            {
                basePenalty *= (1f - hangtimeFlagReduction);
                //Debug.Log($"Falling shot with Hangtime flag, reduced penalty: {basePenalty:F2}");
            }
            else
            {
               // Debug.Log($"Falling shot, penalty: {basePenalty:F2}");
            }

            return -basePenalty;
        }

        private float CalculateDriftAccuracyModifier()
        {
            if (!isStationaryJump || totalDriftDistance < 0.01f)
                return 0f;

            float maxDrift = maxDriftSpeed * 0.5f;
            float driftRatio = Mathf.Clamp01(totalDriftDistance / maxDrift);
            float penalty = driftAccuracyPenalty * driftRatio;

            //Debug.Log($"Drift penalty: {penalty:F2} (drifted {totalDriftDistance:F2} units)");
            return -penalty;
        }

        private float CalculateMovingJumpModifier()
        {
            if (isStationaryJump || jumpMomentum.magnitude < 0.1f)
                return 0f;

            if (!targetHoop.TryGetComponent<Hoop>(out var hoop)) return 0f;

            Vector2 toHoop = (hoop.CourtPosition - courtPosition).normalized;
            Vector2 moveDir = jumpMomentum.normalized;

            float dot = Vector2.Dot(moveDir, toHoop);

            float penalty;

            if (dot > 0.5f)
            {
                penalty = movingTowardPenalty;
                //Debug.Log($"Moving toward hoop, penalty: {penalty:F2}");
            }
            else if (dot > -0.5f)
            {
                penalty = movingLateralPenalty;
                //Debug.Log($"Moving laterally, penalty: {penalty:F2}");
            }
            else
            {
                penalty = movingAwayPenalty;
                //Debug.Log($"Moving away (fadeaway), penalty: {penalty:F2}");
            }

            return -penalty;
        }

        private void ShootBall()
{
    if (ball == null || targetHoop == null) return;

    Hoop hoop = targetHoop.GetComponent<Hoop>();
    if (hoop == null) return;

    float shotAccuracy = CalculateTotalShotChance();
    ShotOutcome outcome = ShotOutcomeCalculator.CalculateOutcome(courtPosition, hoop.CourtPosition, shotAccuracy);

    Debug.Log($"Shot outcome: {outcome.result}, Rim contacts: {outcome.rimContacts.Count}");
    foreach (var contact in outcome.rimContacts)
    {
        Debug.Log($"  - {contact}");
    }

    hoop.SetShotOutcome(outcome);
    LaunchBallToHoop(hoop.CourtPosition, hoop.RimHeight, outcome);

    Invoke("ResetBall", 5f);
}

private float CalculateTotalShotChance()
{
    float baseChance = GetShootingSkill() / 100f;
    float jumpMod = CalculateJumpAccuracyModifier();
    float driftMod = CalculateDriftAccuracyModifier();
    float movingMod = CalculateMovingJumpModifier();

    Hoop hoop = targetHoop.GetComponent<Hoop>();
    float distanceMod = 0f;
    if (hoop != null)
    {
        distanceMod = CalculateDistanceModifier(Vector2.Distance(courtPosition, hoop.CourtPosition)) / 100f;
    }

    float total = baseChance + jumpMod + driftMod + movingMod + distanceMod;
    return Mathf.Clamp(total, 0.05f, 0.95f);
}

private void LaunchBallToHoop(Vector2 hoopPos, float rimHeight, ShotOutcome outcome)
{
    Vector2 startPos = courtPosition;
    float startHeight = jumpHeight + ballOverheadOffset;
    float peakHeight = rimHeight + 1.5f;

    if (peakHeight < startHeight + 0.5f)
        peakHeight = startHeight + 0.5f;

    LaunchBallAtTarget(startPos, startHeight, hoopPos, rimHeight, peakHeight);
}

        private float GetShootingSkill()
        {
            return shootingSkill;
        }

        private float CalculateDistanceModifier(float distance)
        {
            float closeRange = 5f;
            if (distance <= closeRange)
                return 0f;

            float excessDistance = distance - closeRange;
            return -2f * excessDistance;
        }

        private float GetFatigueModifier()
        {
            return 0f;
        }

        private float GetFlagModifier()
        {
            return 0f;
        }

        private void LaunchBallAtTarget(Vector2 startPos, float startHeight, Vector2 targetPos, float targetHeight, float peakHeight)
        {
            float gravity = 9.8f;

            float upDistance = peakHeight - startHeight;
            float downDistance = peakHeight - targetHeight;

            float timeUp = Mathf.Sqrt(2f * upDistance / gravity);
            float timeDown = Mathf.Sqrt(2f * downDistance / gravity);
            float totalTime = timeUp + timeDown;

            float verticalVelocity = gravity * timeUp;
            Vector2 horizontalVelocity = (targetPos - startPos) / totalTime;

            ball.Launch(startPos, startHeight, horizontalVelocity, verticalVelocity);
        }

        private void ResetBall()
        {
            if (ball == null) return;

            //Debug.Log("Ball returned to player!");

            ball.SetHolder(transform);
            ball.courtPosition = courtPosition;
            ball.height = ballOverheadOffset;
            ball.courtVelocity = Vector2.zero;
            ball.verticalVelocity = 0f;

            Invoke(nameof(ResetBall), 5f);
        }

        private void UpdateRendering()
        {
            if (shadowSprite != null)
            {
                shadowSprite.transform.position = new Vector3(
                    courtPosition.x,
                    courtPosition.y,
                    0
                );
            }

            if (playerSprite != null)
            {
                playerSprite.transform.position = new Vector3(
                    courtPosition.x,
                    courtPosition.y + spriteHeightOffset + jumpHeight,
                    0
                );

                playerSprite.sortingOrder = 1000 - (int)(courtPosition.y * 100);
            }
        }
    }
}