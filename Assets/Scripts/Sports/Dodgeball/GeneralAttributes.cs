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
    }
}
