using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Per-player stamina / fatigue. Strenuous actions (throwing, catching,
    /// jumping, diving, sustained running) drain a 0..1 pool; resting refills
    /// it. The endurance stat eases the drain and speeds recovery. Low stamina
    /// folds into the DodgeballAttributes.Effective pipeline (same as Special
    /// Abilities), so a tired player throws weaker + wilder and catches worse —
    /// which also self-limits rapid catch→throw→catch ping-pong.
    /// </summary>
    [RequireComponent(typeof(PlayerZoneTracker))]
    public class PlayerStamina : MonoBehaviour
    {
        [Range(0f, 1f)] public float current = 1f;

        [Header("Costs (fraction of the pool)")]
        [SerializeField] private float throwCost = 0.18f;
        [SerializeField] private float passCost = 0.06f;
        [SerializeField] private float catchCost = 0.12f;
        [SerializeField] private float jumpCost = 0.10f;
        [SerializeField] private float diveCost = 0.14f;
        [Tooltip("Drained per second while running (sprint). A run-jump attack stacks: run drain + jump + throw.")]
        [SerializeField] private float runDrainPerSec = 0.08f;
        [Tooltip("Stamina drained by taking a FULL-SPEED direct hit (a mishandle scales down by its contactMul). Every game mode — you tire from being hit even when the rebound is caught.")]
        [SerializeField] private float impactCost = 0.15f;
        [Tooltip("Ball speed (u/s) treated as 'full speed' for the impact drain.")]
        [SerializeField] private float impactRefSpeed = 36f;

        [Header("Recovery")]
        [Tooltip("Refilled per second while not exerting (walking / idle / set).")]
        [SerializeField] private float recoverPerSec = 0.12f;
        [Tooltip("How much the endurance stat eases drain / speeds recovery. 0 = endurance ignored, 1 = full effect.")]
        [Range(0f, 1f)] [SerializeField] private float enduranceInfluence = 0.6f;

        [Header("Fatigue effect (multiplier at ZERO stamina; 1 at full)")]
        [Range(0f, 1f)] [SerializeField] private float tiredThrowFloor = 0.6f;   // throw speed + accuracy
        [Range(0f, 1f)] [SerializeField] private float tiredCatchFloor = 0.7f;   // catching

        private PlayerZoneTracker tracker;
        private PlayerMovement movement;
        private GeneralAttributes gen;
        private Ball ball;
        private bool subscribed;
        private bool wasAirborne, wasDiving;

        public float Stamina01 => current;

        private void Awake()
        {
            tracker = GetComponent<PlayerZoneTracker>();
            movement = GetComponent<PlayerMovement>();
            gen = GetComponent<GeneralAttributes>();
        }

        private void OnDestroy()
        {
            if (ball != null && subscribed)
            {
                ball.OnReleased -= OnReleased;
                ball.OnCaught -= OnCaught;
            }
        }

        private float Endurance01 => gen != null ? gen.Endurance01 : 0.5f;
        private float DrainMul   => 1f - Endurance01 * enduranceInfluence;   // high endurance drains less
        private float RecoverMul => 1f + Endurance01 * enduranceInfluence;   // and recovers more

        public void Drain(float amount) => current = Mathf.Clamp01(current - Mathf.Max(0f, amount) * DrainMul);

        /// <summary>
        /// Stamina lost from taking a ball impact — universal across all game modes
        /// (you tire from being hit even if a teammate catches the rebound and saves
        /// the point). Scales with ball speed and the contact (direct vs glancing);
        /// endurance softens it like any other drain.
        /// </summary>
        public void TakeImpact(float speed, float contactMul)
            => Drain(impactCost * Mathf.Clamp01(speed / Mathf.Max(0.01f, impactRefSpeed)) * Mathf.Clamp01(contactMul));

        private void Update()
        {
            EnsureSubscribed();
            if (movement == null) return;

            // Jump / dive cost a chunk on the rising edge.
            bool air = movement.IsAirborne;
            if (air && !wasAirborne) Drain(jumpCost);
            wasAirborne = air;

            bool diving = movement.IsDiving;
            if (diving && !wasDiving) Drain(diveCost);
            wasDiving = diving;

            // Sustained running drains; otherwise (walking / idle) recover.
            if (movement.IsRunning && !air && !diving)
                Drain(runDrainPerSec * Time.deltaTime);
            else if (!air && !diving)
                current = Mathf.Clamp01(current + recoverPerSec * RecoverMul * Time.deltaTime);
        }

        private void EnsureSubscribed()
        {
            if (subscribed) return;
            if (ball == null) ball = FindAnyObjectByType<Ball>();
            if (ball == null) return;
            ball.OnReleased += OnReleased;
            ball.OnCaught += OnCaught;
            subscribed = true;
        }

        private void OnReleased(PlayerZoneTracker thrower, PlayerZoneTracker target, bool isThrow)
        {
            if (thrower == tracker) Drain(isThrow ? throwCost : passCost);
        }

        private void OnCaught(PlayerZoneTracker catcher)
        {
            if (catcher == tracker) Drain(catchCost);
        }

        /// <summary>
        /// Fatigue multiplier this stamina applies to a stat (1 = fresh). Tired
        /// players throw weaker/wilder and catch worse; other stats unaffected.
        /// Folded into DodgeballAttributes.Effective.
        /// </summary>
        public float MultiplierFor(AbilityStat stat)
        {
            switch (stat)
            {
                case AbilityStat.ThrowSpeed:
                case AbilityStat.ThrowAccuracy:
                    return Mathf.Lerp(tiredThrowFloor, 1f, current);
                case AbilityStat.Catching:
                    return Mathf.Lerp(tiredCatchFloor, 1f, current);
                default:
                    return 1f;
            }
        }
    }
}
