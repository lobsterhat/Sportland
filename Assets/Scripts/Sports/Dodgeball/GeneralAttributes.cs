using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Cross-sport player attributes — things that would carry across any
    /// Sportland game. Sport-specific ratings live on their own component
    /// (e.g. DodgeballAttributes). For now this just holds Luck, which nudges
    /// the random rolls in skill checks; more general ratings (athleticism,
    /// composure, reflexes…) can be added here later.
    /// </summary>
    public class GeneralAttributes : MonoBehaviour
    {
        [Tooltip("0..100. Skews random rolls (e.g. catch luck) in the player's favor.")]
        [Range(0f, 100f)] public float luck = 50f;

        /// <summary>Luck on a 0..1 scale.</summary>
        public float Luck01 => Mathf.Clamp01(luck / 100f);

        [Tooltip("Energy / HP for the energy game mode (Mode 3). Configurable per player — hits drain it; 0 = out.")]
        public float maxEnergy = 100f;

        [Tooltip("0..100. Resilience: higher = less damage taken per hit. (May fold into a 'strength' stat later.)")]
        [Range(0f, 100f)] public float toughness = 50f;

        /// <summary>Toughness on a 0..1 scale.</summary>
        public float Toughness01 => Mathf.Clamp01(toughness / 100f);

        [Tooltip("0..100. How sharply this player can reverse direction at speed. Higher = shorter pivot delay when input flips >90° relative to current velocity (with a floor — even elite players have some momentum cost).")]
        [Range(0f, 100f)] public float changeOfDirection = 50f;

        /// <summary>ChangeOfDirection on a 0..1 scale.</summary>
        public float ChangeOfDirection01 => Mathf.Clamp01(changeOfDirection / 100f);

        [Tooltip("0..100. Fatigue resistance: higher = stamina drains slower and recovers faster (see PlayerStamina). Tired players throw weaker/wilder and catch worse.")]
        [Range(0f, 100f)] public float endurance = 50f;

        /// <summary>Endurance on a 0..1 scale.</summary>
        public float Endurance01 => Mathf.Clamp01(endurance / 100f);
    }
}
