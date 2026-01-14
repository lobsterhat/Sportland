using UnityEngine;
using Sportland.Sports.Basketball.Stats;

namespace Sportland.Sports.Basketball.Gameplay
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
        public float dunkSkill = 50f;  // 0-100, determines dunk ability and range
        public float dunkRange = 2.5f; // Max distance to attempt dunk
        public bool dunksEnabled = true; // Toggle dunks on/off for testing

        [Header("Debug Controls")]
        public bool debugModeEnabled = false;
        public ShotType forcedShotType = ShotType.StandardJumpShot;
        public ShotResult forcedOutcome = ShotResult.Swish;
        public ShotTarget forcedShotTarget = ShotTarget.FrontOutside;

        [Header("Dunk State")]
        private bool isDunking = false;
        private Vector2 dunkDriftVelocity;
        private bool isHangingOnRim = false;
        private float rimHangTimer = 0f;
        public float rimHangDuration = 0.5f;  // How long to hang on rim

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

            // Debug controls (number keys)
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                debugModeEnabled = !debugModeEnabled;
            }

            if (debugModeEnabled)
            {
                // Cycle shot type with Alpha2
                if (Input.GetKeyDown(KeyCode.Alpha2))
                {
                    forcedShotType = (ShotType)(((int)forcedShotType + 1) % System.Enum.GetValues(typeof(ShotType)).Length);
                }

                // Cycle outcome with Alpha3
                if (Input.GetKeyDown(KeyCode.Alpha3))
                {
                    forcedOutcome = (ShotResult)(((int)forcedOutcome + 1) % System.Enum.GetValues(typeof(ShotResult)).Length);
                }

                // Cycle shot target with Alpha4
                if (Input.GetKeyDown(KeyCode.Alpha4))
                {
                    forcedShotTarget = (ShotTarget)(((int)forcedShotTarget + 1) % System.Enum.GetValues(typeof(ShotTarget)).Length);
                }
            }

            moveInput.x = Input.GetAxisRaw("Horizontal");
            moveInput.y = Input.GetAxisRaw("Vertical");

            if (ball != null && ball.isHeld)
            {
                if (Input.GetKeyDown(KeyCode.Space) && !isJumping && !isDunking)
                {
                    // Check if player should attempt dunk instead of regular jump
                    if (CanAttemptDunk())
                    {
                        StartDunk();
                    }
                    else
                    {
                        StartJump();
                    }
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
            if (isHangingOnRim)
            {
                // No movement while hanging on rim
                return;
            }
            else if (isDunking)
            {
                // Drift toward basket during dunk
                courtPosition += dunkDriftVelocity * Time.deltaTime;
            }
            else if (isJumping)
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

            // Handle rim hang state
            if (isHangingOnRim)
            {
                rimHangTimer += Time.deltaTime;

                // Keep player at rim height, no movement
                jumpVelocity = 0f;
                dunkDriftVelocity = Vector2.zero;

                // Release from rim after hang duration
                if (rimHangTimer >= rimHangDuration)
                {
                    Debug.Log("Released from rim!");
                    isHangingOnRim = false;
                    isDunking = false;
                    rimHangTimer = 0f;
                    // Let gravity take over
                }
                return;
            }

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

            if (isStationaryJump && moveInput.magnitude > 0.1f && !isDunking)
            {
                driftVelocity = moveInput.normalized * maxDriftSpeed;
            }

            // Check for dunk release at rim
            if (isDunking && !isHangingOnRim && targetHoop != null)
            {
                if (targetHoop.TryGetComponent<Hoop>(out var hoop))
                {
                    float distanceToRim = Vector2.Distance(courtPosition, hoop.CourtPosition);
                    float heightAtRim = jumpHeight + ballOverheadOffset;

                    // Release dunk when near rim and at/above rim height
                    if (distanceToRim < 0.5f && heightAtRim >= hoop.RimHeight - 0.3f)
                    {
                        FinishDunk();
                        return;
                    }
                }
            }

            if (jumpHeight <= 0f)
            {
                jumpHeight = 0f;
                isJumping = false;
                isDunking = false;
                isHangingOnRim = false;
                passedApex = false;
                jumpApex = 0f;
                jumpMomentum = Vector2.zero;
                isStationaryJump = false;
                driftVelocity = Vector2.zero;
                dunkDriftVelocity = Vector2.zero;
                rimHangTimer = 0f;
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

        private bool CanAttemptDunk()
        {
            if (!dunksEnabled) return false; // Check if dunks are enabled
            if (targetHoop == null || dunkSkill < 10f) return false;

            if (!targetHoop.TryGetComponent<Hoop>(out var hoop)) return false;

            float distanceToBasket = Vector2.Distance(courtPosition, hoop.CourtPosition);

            // Can attempt dunk if within range (skill affects range)
            float effectiveDunkRange = dunkRange * (dunkSkill / 100f);
            return distanceToBasket <= effectiveDunkRange;
        }

        private void StartDunk()
        {
            CancelInvoke("ResetBall");

            isDunking = true;
            isJumping = true;
            isStationaryJump = false;
            passedApex = false;
            jumpApex = 0f;
            jumpHeight = 0f;

            // Dunk jump is higher/more explosive
            float dunkJumpHeight = baseJumpHeight + (jumpSkill * jumpSkillModifier) + 0.3f;
            jumpVelocity = Mathf.Sqrt(2f * Mathf.Abs(jumpGravity) * dunkJumpHeight);

            // Calculate drift toward basket - velocity to reach rim at apex
            if (targetHoop.TryGetComponent<Hoop>(out var hoop))
            {
                Vector2 toHoop = hoop.CourtPosition - courtPosition;
                float distanceToRim = toHoop.magnitude;

                // Time to reach apex (when we want to be at rim)
                float timeToApex = jumpVelocity / Mathf.Abs(jumpGravity);

                // Calculate velocity needed to cover distance in that time
                // But leave a small gap so we don't overshoot
                float targetDistance = Mathf.Max(0f, distanceToRim - 0.3f);
                dunkDriftVelocity = toHoop.normalized * (targetDistance / timeToApex);
            }
        }

        private void FinishDunk()
        {

            // Force dunk to always go in
            if (ball == null || targetHoop == null) return;

            Hoop hoop = targetHoop.GetComponent<Hoop>();
            if (hoop == null) return;

            // Start hanging on rim
            isHangingOnRim = true;
            rimHangTimer = 0f;

            // Stop all movement
            jumpVelocity = 0f;
            dunkDriftVelocity = Vector2.zero;

            // Create guaranteed make outcome (Swish = clean through)
            ShotOutcome outcome = new ShotOutcome
            {
                result = ShotResult.Swish,
                rimContacts = new System.Collections.Generic.List<RimContact>()
            };

            hoop.SetShotOutcome(outcome);

            // Slam ball down through rim
            Vector2 startPos = courtPosition;
            float startHeight = jumpHeight + ballOverheadOffset;

            // Dunk aims DOWN through the rim
            ball.Launch(startPos, startHeight, Vector2.zero, -5f); // Negative velocity = downward

            Invoke("ResetBall", 5f);
        }

        private void ReleaseShot()
        {
            if (ball == null || !ball.isHeld) return;

            //Debug.Log($"Shot released at height: {jumpHeight:F2}, Apex: {jumpApex:F2}, Passed apex: {passedApex}");

            ShootBall();
        }

        private ShotContext DetermineShotType()
        {
            ShotContext context = new ShotContext();

            if (!targetHoop.TryGetComponent<Hoop>(out var hoop))
            {
                context.type = ShotType.StandardJumpShot;
                return context;
            }

            Vector2 toHoop = hoop.CourtPosition - courtPosition;
            float distanceToBasket = toHoop.magnitude;
            context.distanceToBasket = distanceToBasket;

            // DEBUG MODE: Use forced shot type
            if (debugModeEnabled)
            {
                context.type = forcedShotType;
                context.releaseHeight = jumpHeight + ballOverheadOffset;
                context.releaseExtension = GetExtensionForShotType(forcedShotType);
                context.intentionalBank = (forcedShotType == ShotType.BankShot || forcedShotType == ShotType.Layup);
                context.isMoving = jumpMomentum.magnitude > 0.1f;
                context.movingTowardBasket = true;
                context.movingAwayFromBasket = false;
                return context;
            }

            // Determine movement state
            context.isMoving = jumpMomentum.magnitude > 0.1f || !isStationaryJump;

            if (context.isMoving)
            {
                Vector2 moveDir = jumpMomentum.normalized;
                Vector2 toHoopDir = toHoop.normalized;
                float dot = Vector2.Dot(moveDir, toHoopDir);

                context.movingTowardBasket = dot > 0.5f;
                context.movingAwayFromBasket = dot < -0.5f;
            }

            // Check for bank shot input (B key)
            context.intentionalBank = Input.GetKey(KeyCode.B);

            // Determine shot type based on distance and movement
            // DUNK: Very close AND jump height puts ball near/above rim AND dunks enabled
            if (dunksEnabled && distanceToBasket < 1.5f && (jumpHeight + ballOverheadOffset) >= (hoop.RimHeight - 0.5f))
            {
                context.type = ShotType.Dunk;
                context.releaseHeight = jumpHeight + ballOverheadOffset;
                context.releaseExtension = 0.3f; // Reach toward rim
            }
            // LAYUP: Close range while moving toward basket
            else if (distanceToBasket < 4.0f && context.movingTowardBasket)
            {
                context.type = ShotType.Layup;
                context.releaseHeight = jumpHeight + ballOverheadOffset;
                context.releaseExtension = 0.5f; // Extend arm toward basket
                context.intentionalBank = true; // Layups always use backboard
            }
            // FLOATER: Mid-range, moving toward, released before apex
            else if (distanceToBasket >= 3.0f && distanceToBasket < 8.0f &&
                     context.movingTowardBasket && !passedApex && jumpHeight < jumpApex * 0.7f)
            {
                context.type = ShotType.Floater;
                context.releaseHeight = jumpHeight + ballOverheadOffset;
                context.releaseExtension = 0.2f;
            }
            // FADEAWAY: Moving away from basket
            else if (context.movingAwayFromBasket)
            {
                context.type = ShotType.FadeawayJumpShot;
                context.releaseHeight = jumpHeight + ballOverheadOffset;
                context.releaseExtension = 0f;
            }
            // RUNNING JUMP SHOT: Moving with momentum
            else if (context.isMoving)
            {
                context.type = ShotType.RunningJumpShot;
                context.releaseHeight = jumpHeight + ballOverheadOffset;
                context.releaseExtension = 0f;
            }
            // STANDARD JUMP SHOT: Stationary
            else
            {
                context.type = ShotType.StandardJumpShot;
                context.releaseHeight = jumpHeight + ballOverheadOffset;
                context.releaseExtension = 0f;
            }

            // Override with bank shot if player requested it (but not for dunks)
            if (context.intentionalBank && context.type != ShotType.Dunk)
            {
                context.type = context.type == ShotType.Layup ? ShotType.Layup : ShotType.BankShot;
            }

            return context;
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

    // Determine shot type
    ShotContext shotContext = DetermineShotType();

    ShotOutcome outcome;

    // DEBUG MODE: Use forced outcome
    if (debugModeEnabled)
    {
        outcome = CreateForcedOutcome();
    }
    else
    {
        float shotAccuracy = CalculateTotalShotChance(shotContext);
        outcome = ShotOutcomeCalculator.CalculateOutcome(courtPosition, hoop.CourtPosition, shotAccuracy);
    }

    // Consolidated shot info log
    string contactsList = outcome.rimContacts.Count > 0
        ? string.Join(", ", outcome.rimContacts)
        : "None";

    if (debugModeEnabled)
    {
        Debug.Log($"SHOT: Type={shotContext.type}, Target={forcedShotTarget}, Outcome={outcome.result}, Contacts=[{contactsList}] [DEBUG MODE]");
    }
    else
    {
        Debug.Log($"SHOT: Type={shotContext.type}, Outcome={outcome.result}, Contacts=[{contactsList}]");
    }

    hoop.SetShotOutcome(outcome);
    LaunchBallToHoop(hoop.CourtPosition, hoop.RimHeight, outcome, shotContext);

    Invoke("ResetBall", 5f);
}

private ShotOutcome CreateForcedOutcome()
{
    ShotOutcome outcome = new ShotOutcome();
    outcome.rimContacts = new System.Collections.Generic.List<RimContact>();

    // Handle special targets that override outcome
    if (forcedShotTarget == ShotTarget.Swish)
    {
        outcome.result = ShotResult.Swish;
        // No rim contacts for swish
        return outcome;
    }

    if (forcedShotTarget == ShotTarget.Airball)
    {
        outcome.result = ShotResult.Miss;
        // No rim contacts for airball
        return outcome;
    }

    // Use the forced outcome for all other targets
    outcome.result = forcedOutcome;

    // Handle backboard target
    if (forcedShotTarget == ShotTarget.Backboard)
    {
        outcome.rimContacts.Add(RimContact.Backboard);

        // Add rim contact sequence based on outcome
        if (outcome.result == ShotResult.Miss)
        {
            // Backboard then miss - ball bounces off backboard and away
            // No additional contacts needed
        }
        else if (outcome.result == ShotResult.BackboardAndIn || outcome.result == ShotResult.RimAndIn)
        {
            // Backboard then make - add rim contacts for rattle
            outcome.rimContacts.Add(RimContact.FrontRim);
            outcome.rimContacts.Add(RimContact.BackRim);
        }
        else if (outcome.result == ShotResult.Swish)
        {
            // Backboard then swish (no rim contact, just glass and in)
            // No additional contacts
        }

        return outcome;
    }

    // Handle rim targets (all inside/outside edge combinations)
    RimContact rimSide = GetRimContactFromTarget(forcedShotTarget);

    // For Swish, don't add any rim contacts - ball goes straight through
    if (outcome.result == ShotResult.Swish)
    {
        return outcome;
    }

    outcome.rimContacts.Add(rimSide);

    // Add second contact based on outcome type
    if (outcome.result == ShotResult.RimAndIn)
    {
        // Rattle effect - hits opposite rim then goes in
        outcome.rimContacts.Add(GetOppositeRim(rimSide));
    }
    else if (outcome.result == ShotResult.BackboardAndIn)
    {
        // Hits rim, then backboard, then goes in
        outcome.rimContacts.Add(RimContact.Backboard);
        outcome.rimContacts.Add(GetOppositeRim(rimSide));
    }
    else if (outcome.result == ShotResult.Miss)
    {
        // Single rim hit then bounces out
        // Physics will determine bounce direction
    }

    return outcome;
}

private float GetExtensionForShotType(ShotType shotType)
{
    switch (shotType)
    {
        case ShotType.Dunk:
            return 0.3f;
        case ShotType.Layup:
            return 0.5f;
        case ShotType.Floater:
            return 0.2f;
        default:
            return 0f;
    }
}

private float CalculateTotalShotChance(ShotContext shotContext)
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

    // Shot type modifiers
    float shotTypeMod = GetShotTypeModifier(shotContext);

    float total = baseChance + jumpMod + driftMod + movingMod + distanceMod + shotTypeMod;
    return Mathf.Clamp(total, 0.05f, 0.95f);
}

private float GetShotTypeModifier(ShotContext shotContext)
{
    switch (shotContext.type)
    {
        case ShotType.Dunk:
            return 0.4f; // Dunks are nearly guaranteed

        case ShotType.Layup:
            return 0.25f; // Layups are high percentage shots

        case ShotType.Floater:
            return -0.05f; // Slightly harder (touch shot)

        case ShotType.HookShot:
            return -0.1f; // Harder to aim

        case ShotType.FadeawayJumpShot:
            // Already handled by movingMod
            return 0f;

        case ShotType.BankShot:
            return 0.05f; // Intentional bank shot bonus

        case ShotType.FreeThrow:
            return 0.2f; // Uncontested, practice shot

        case ShotType.RunningJumpShot:
        case ShotType.StandardJumpShot:
        default:
            return 0f;
    }
}

private void LaunchBallToHoop(Vector2 hoopPos, float rimHeight, ShotOutcome outcome, ShotContext shotContext)
{
    // Calculate release position based on shot type
    Vector2 toHoop = (hoopPos - courtPosition).normalized;
    Vector2 startPos = courtPosition + (toHoop * shotContext.releaseExtension);
    float startHeight = shotContext.releaseHeight;

    // Adjust arc based on shot type
    float peakHeight = GetPeakHeightForShotType(shotContext, rimHeight, startHeight);
    float targetHeight = GetTargetHeightForShotType(shotContext, rimHeight);

    if (peakHeight < startHeight + 0.5f)
        peakHeight = startHeight + 0.5f;

    // Determine target position based on debug mode or shot outcome
    Vector2 targetPos = hoopPos;

    // For made shots, aim at the scoring zone height (3.05) to ensure clean entry
    bool isMake = (outcome.result == ShotResult.Swish || outcome.result == ShotResult.RimAndIn || outcome.result == ShotResult.BackboardAndIn);
    if (isMake)
    {
        targetHeight = 3.05f; // Scoring zone height
    }

    // DEBUG MODE: Use forced shot target
    if (debugModeEnabled)
    {
        targetPos = GetTargetPosition(hoopPos, forcedShotTarget);

        // For rim/backboard targets in debug mode, use the first contact point if this is a make
        if (isMake && outcome.rimContacts.Count > 0)
        {
            targetPos = GetRimContactPosition(hoopPos, outcome.rimContacts[0]);
            targetHeight = rimHeight; // Aim at rim height for first contact
        }
    }
    // NORMAL MODE: Determine target from shot outcome
    else if (shotContext.type == ShotType.Layup)
    {
        // Layups aim at backboard
        targetPos = CalculateLayupBackboardTarget(hoopPos, rimHeight);
        targetHeight = rimHeight + 0.8f;
    }
    else if (outcome.result == ShotResult.RimAndIn && outcome.rimContacts.Count > 0)
    {
        // RimAndIn aims at first rim contact point at rim height
        targetPos = GetRimContactPosition(hoopPos, outcome.rimContacts[0]);
        targetHeight = rimHeight;
    }
    else if (outcome.result == ShotResult.BackboardAndIn)
    {
        // BackboardAndIn aims at backboard
        targetPos = CalculateLayupBackboardTarget(hoopPos, rimHeight);
        targetHeight = rimHeight + 0.6f;
    }
    else if (outcome.result == ShotResult.Miss && outcome.rimContacts.Count > 0)
    {
        // Misses aim at first rim contact point
        targetPos = GetRimContactPosition(hoopPos, outcome.rimContacts[0]);
    }

    LaunchBallAtTarget(startPos, startHeight, targetPos, targetHeight, peakHeight);
}

private Vector2 CalculateLayupBackboardTarget(Vector2 hoopPos, float rimHeight)
{
    // Find backboard position
    Backboard backboard = FindAnyObjectByType<Backboard>();
    if (backboard != null)
    {
        // Aim at a point on the backboard above and slightly to the side of the rim
        // The backboard is at courtPosition.x, so use that
        Vector2 backboardPos = new Vector2(backboard.transform.position.x, hoopPos.y);

        // Offset slightly toward the side player is approaching from
        float sideOffset = (courtPosition.y < hoopPos.y) ? 0.15f : -0.15f;
        return new Vector2(backboardPos.x, hoopPos.y + sideOffset);
    }

    // Fallback: estimate backboard is ~0.6 units beyond hoop
    return new Vector2(hoopPos.x + 0.6f, hoopPos.y);
}

private Vector2 GetRimContactPosition(Vector2 hoopPos, RimContact contact)
{
    // Rim dimensions (should match Hoop.cs rimScale)
    float halfWidth = 0.73f / 2f;  // X dimension
    float halfDepth = 0.57f / 2f;  // Y dimension

    switch (contact)
    {
        case RimContact.FrontRim:
            return hoopPos + new Vector2(0, -halfDepth);
        case RimContact.BackRim:
            return hoopPos + new Vector2(0, halfDepth);
        case RimContact.LeftRim:
            return hoopPos + new Vector2(-halfWidth, 0);
        case RimContact.RightRim:
            return hoopPos + new Vector2(halfWidth, 0);
        case RimContact.Backboard:
            // Backboard is beyond the hoop - use backboard target
            return CalculateLayupBackboardTarget(hoopPos, 0f);
        default:
            return hoopPos;
    }
}

private RimContact GetRimContactFromTarget(ShotTarget target)
{
    switch (target)
    {
        case ShotTarget.FrontOutside:
        case ShotTarget.FrontInside:
            return RimContact.FrontRim;
        case ShotTarget.BackOutside:
        case ShotTarget.BackInside:
            return RimContact.BackRim;
        case ShotTarget.LeftOutside:
        case ShotTarget.LeftInside:
            return RimContact.LeftRim;
        case ShotTarget.RightOutside:
        case ShotTarget.RightInside:
            return RimContact.RightRim;
        case ShotTarget.Backboard:
            return RimContact.Backboard;
        default:
            return RimContact.FrontRim;
    }
}

private RimContact GetOppositeRim(RimContact contact)
{
    switch (contact)
    {
        case RimContact.FrontRim: return RimContact.BackRim;
        case RimContact.BackRim: return RimContact.FrontRim;
        case RimContact.LeftRim: return RimContact.RightRim;
        case RimContact.RightRim: return RimContact.LeftRim;
        default: return contact;
    }
}

private Vector2 GetTargetPosition(Vector2 hoopPos, ShotTarget target)
{
    // Rim dimensions
    float halfWidth = 0.73f / 2f;
    float halfDepth = 0.57f / 2f;

    // Offset for inside/outside edge
    // Max 0.12 (ball radius) to allow Outside targets to pass through rim for Swish
    float edgeOffset = 0.10f; // How far inside/outside from rim edge

    switch (target)
    {
        case ShotTarget.Swish:
            return hoopPos;

        case ShotTarget.Airball:
            // Aim past the hoop
            return hoopPos + new Vector2(0.5f, 0.3f);

        case ShotTarget.Backboard:
            return CalculateLayupBackboardTarget(hoopPos, 0f);

        // Front rim targets
        case ShotTarget.FrontOutside:
            return hoopPos + new Vector2(0, -halfDepth - edgeOffset);
        case ShotTarget.FrontInside:
            return hoopPos + new Vector2(0, -halfDepth + edgeOffset);

        // Back rim targets
        case ShotTarget.BackOutside:
            return hoopPos + new Vector2(0, halfDepth + edgeOffset);
        case ShotTarget.BackInside:
            return hoopPos + new Vector2(0, halfDepth - edgeOffset);

        // Left rim targets
        case ShotTarget.LeftOutside:
            return hoopPos + new Vector2(-halfWidth - edgeOffset, 0);
        case ShotTarget.LeftInside:
            return hoopPos + new Vector2(-halfWidth + edgeOffset, 0);

        // Right rim targets
        case ShotTarget.RightOutside:
            return hoopPos + new Vector2(halfWidth + edgeOffset, 0);
        case ShotTarget.RightInside:
            return hoopPos + new Vector2(halfWidth - edgeOffset, 0);

        default:
            return hoopPos;
    }
}

private float GetPeakHeightForShotType(ShotContext shotContext, float rimHeight, float startHeight)
{
    switch (shotContext.type)
    {
        case ShotType.Dunk:
            return rimHeight + 0.5f; // Low arc, slam down

        case ShotType.Layup:
            return rimHeight + 1.5f; // Medium arc over rim

        case ShotType.Floater:
            return rimHeight + 2.0f; // Higher arc to get over defender

        case ShotType.HookShot:
            return rimHeight + 2.5f; // High arc

        case ShotType.BankShot:
            return rimHeight + 2.0f; // Medium-high arc for bank

        case ShotType.StandardJumpShot:
        case ShotType.RunningJumpShot:
        case ShotType.FadeawayJumpShot:
        default:
            return rimHeight + 2.5f; // Standard high arc
    }
}

private float GetTargetHeightForShotType(ShotContext shotContext, float rimHeight)
{
    switch (shotContext.type)
    {
        case ShotType.Dunk:
            return rimHeight - 0.5f; // Aim below rim, force through

        case ShotType.Layup:
            return rimHeight + 0.5f; // Soft touch over rim

        case ShotType.BankShot:
            return rimHeight + 0.3f; // Aim for backboard, then drop through

        default:
            return rimHeight + 0.3f; // Standard aim slightly above rim
    }
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