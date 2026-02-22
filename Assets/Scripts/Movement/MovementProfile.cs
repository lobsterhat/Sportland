using UnityEngine;

/// <summary>
/// Data container for universal athlete movement parameters.
/// Swap profiles to create different archetypes (Speedster, Agile, Tank, etc.)
/// without changing any runtime code.
/// </summary>
[CreateAssetMenu(fileName = "NewMovementProfile", menuName = "Sportland/Movement Profile")]
public class MovementProfile : ScriptableObject
{
    [Header("=== LOCOMOTION ===")]

    [Tooltip("Maximum speed the athlete can reach at full sprint (units/sec)")]
    [Range(3f, 12f)]
    public float topSpeed = 7f;

    [Tooltip("How quickly the athlete reaches top speed (units/sec²). " +
             "High = explosive first step, low = slow to build.")]
    [Range(5f, 30f)]
    public float acceleration = 15f;

    [Tooltip("How quickly the athlete slows down when no input is given (units/sec²)")]
    [Range(10f, 40f)]
    public float deceleration = 20f;

    [Tooltip("Speed while jogging / conserving energy (fraction of topSpeed)")]
    [Range(0.3f, 0.7f)]
    public float jogSpeedRatio = 0.5f;

    [Header("=== AGILITY ===")]

    [Tooltip("Rotational speed when changing direction (degrees/sec). " +
             "Higher = snappier cuts.")]
    [Range(180f, 720f)]
    public float turnSpeed = 360f;

    [Tooltip("Fraction of current speed retained after a hard cut (90°+). " +
             "0.3 = lose 70% speed on a hard cut. 1.0 = no penalty.")]
    [Range(0.1f, 1f)]
    public float cutSpeedRetention = 0.4f;

    [Tooltip("Angle threshold (degrees) that qualifies as a 'hard cut' " +
             "and triggers speed penalty.")]
    [Range(45f, 135f)]
    public float hardCutAngleThreshold = 75f;

    [Tooltip("How quickly the athlete recovers speed after a hard cut (units/sec²). " +
             "Can differ from base acceleration to represent agility vs straight-line speed.")]
    [Range(5f, 30f)]
    public float cutRecoveryAcceleration = 12f;

    [Header("=== VERTICAL ===")]

    [Tooltip("Peak jump height (units)")]
    [Range(0.5f, 3f)]
    public float jumpHeight = 1.5f;

    [Tooltip("Time to reach jump apex (seconds). Lower = snappier jump.")]
    [Range(0.15f, 0.6f)]
    public float timeToJumpApex = 0.35f;

    [Tooltip("Recovery delay after landing before full movement resumes (seconds)")]
    [Range(0f, 0.3f)]
    public float landingRecoveryTime = 0.08f;

    [Header("=== DIVING ===")]

    [Tooltip("Horizontal distance covered by a dive (units)")]
    [Range(1f, 4f)]
    public float diveDistance = 2.5f;

    [Tooltip("Duration of the dive from launch to landing (seconds)")]
    [Range(0.2f, 0.8f)]
    public float diveDuration = 0.4f;

    [Tooltip("Time spent on the ground recovering after a dive (seconds). " +
             "This is the HIGH-RISK cost of a dive.")]
    [Range(0.3f, 2f)]
    public float diveRecoveryTime = 0.8f;

    [Header("=== ENDURANCE ===")]

    [Tooltip("Maximum stamina pool")]
    [Range(50f, 200f)]
    public float maxStamina = 100f;

    [Tooltip("Stamina drain per second while sprinting")]
    [Range(5f, 25f)]
    public float sprintStaminaDrain = 10f;

    [Tooltip("Stamina drain per second while jogging (should be 0 or very low)")]
    [Range(0f, 3f)]
    public float jogStaminaDrain = 0f;

    [Tooltip("Flat stamina cost for executing a jump")]
    [Range(2f, 15f)]
    public float jumpStaminaCost = 5f;

    [Tooltip("Flat stamina cost for executing a dive")]
    [Range(5f, 25f)]
    public float diveStaminaCost = 12f;

    [Tooltip("Flat stamina cost for executing a hard cut")]
    [Range(1f, 10f)]
    public float cutStaminaCost = 3f;

    [Tooltip("Stamina recovered per second while jogging or idle")]
    [Range(3f, 15f)]
    public float staminaRegenRate = 8f;

    [Tooltip("Delay after a stamina-draining action before regen begins (seconds)")]
    [Range(0.5f, 3f)]
    public float staminaRegenDelay = 1f;

    [Header("=== FATIGUE DEGRADATION ===")]
    // These define HOW MUCH each system degrades when stamina is fully depleted.
    // At full stamina, multiplier = 1.0. At zero stamina, multiplier = (1 - penalty).
    // Interpolation is non-linear (see BaseMovementController.GetFatigueMultiplier).

    [Tooltip("Max top speed penalty at zero stamina (0.3 = lose 30% top speed)")]
    [Range(0f, 0.5f)]
    public float fatigueSpeedPenalty = 0.25f;

    [Tooltip("Max acceleration penalty at zero stamina")]
    [Range(0f, 0.5f)]
    public float fatigueAccelerationPenalty = 0.3f;

    [Tooltip("Max agility penalty at zero stamina (cuts get wider, slower turns)")]
    [Range(0f, 0.6f)]
    public float fatigueAgilityPenalty = 0.4f;

    [Tooltip("Max jump height penalty at zero stamina")]
    [Range(0f, 0.4f)]
    public float fatigueJumpPenalty = 0.2f;

    [Tooltip("Additional dive recovery time at zero stamina (added seconds)")]
    [Range(0f, 1.5f)]
    public float fatigueDiveRecoveryPenalty = 0.5f;
}
