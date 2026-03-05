using UnityEngine;
using System.Collections.Generic;
using Sportland.Sports.Tag;

namespace Sportland.Sports.Tag
{
    /// <summary>
    /// A safe zone where non-It players cannot be tagged.
    /// 
    /// Rules:
    ///   - All players can freely enter/exit the zone
    ///   - When a non-It player is inside an active zone, they cannot be tagged
    ///   - If no non-It players are inside, It players can pass through freely
    ///   - When an It player enters an ACTIVE zone (one with runners in it),
    ///     the zone's countdown pauses — punishing the It player by wasting
    ///     their time while their own clocks keep running
    ///   - The zone deactivates when:
    ///       a) The countdown timer expires, OR
    ///       b) All non-It players leave the zone
    ///   - After deactivating, the zone enters a cooldown before reactivating
    /// 
    /// Setup:
    ///   1. Create a GameObject with a Collider2D (Is Trigger: checked)
    ///   2. Add this component
    ///   3. Add a SpriteRenderer for visual feedback
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class SafeZone : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        //  CONFIGURATION
        // ──────────────────────────────────────────────

        [Header("=== TIMING ===")]

        [Tooltip("How long the zone stays active once a non-It player enters (seconds).")]
        [SerializeField] private float activeTime = 5f;

        [Tooltip("Cooldown before the zone reactivates after deactivating (seconds).")]
        [SerializeField] private float cooldownTime = 10f;

        [Header("=== VISUAL FEEDBACK ===")]

        [Tooltip("Zone color when available and unoccupied (ready to use).")]
        [SerializeField] private Color readyColor = new Color(0.2f, 0.8f, 0.2f, 0.3f);

        [Tooltip("Zone color when active (runners inside, protected).")]
        [SerializeField] private Color activeColor = new Color(0.2f, 0.5f, 1f, 0.4f);

        [Tooltip("Zone color when active but paused (It player inside).")]
        [SerializeField] private Color pausedColor = new Color(0.8f, 0.5f, 1f, 0.4f);

        [Tooltip("Zone color when active and time is running low.")]
        [SerializeField] private Color warningColor = new Color(1f, 0.8f, 0.2f, 0.4f);

        [Tooltip("Zone color when on cooldown (disabled).")]
        [SerializeField] private Color cooldownColor = new Color(0.4f, 0.4f, 0.4f, 0.2f);

        [Tooltip("Fraction of active time remaining that triggers warning color.")]
        [SerializeField] private float warningThreshold = 0.3f;

        // ──────────────────────────────────────────────
        //  ZONE STATE
        // ──────────────────────────────────────────────

        public enum ZoneState
        {
            Ready,      // available, waiting for a runner to enter
            Active,     // runner(s) inside, countdown running
            Paused,     // runner(s) inside but It player is also inside — clock paused
            Cooldown    // disabled, recharging
        }

        public ZoneState CurrentState { get; private set; } = ZoneState.Ready;

        private SpriteRenderer spriteRenderer;
        private float activeTimeRemaining;
        private float cooldownTimeRemaining;

        // Track who's inside
        private HashSet<TagMovementController> runnersInside = new HashSet<TagMovementController>();
        private HashSet<TagMovementController> itPlayersInside = new HashSet<TagMovementController>();

        // ──────────────────────────────────────────────
        //  EVENTS
        // ──────────────────────────────────────────────

        public System.Action OnZoneActivated;
        public System.Action OnZoneDeactivated;
        public System.Action OnZonePaused;
        public System.Action OnZoneReady;

        // ──────────────────────────────────────────────
        //  INITIALIZATION
        // ──────────────────────────────────────────────

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();

            var col = GetComponent<Collider2D>();
            col.isTrigger = true;

            SetState(ZoneState.Ready);
        }

        // ──────────────────────────────────────────────
        //  UPDATE
        // ──────────────────────────────────────────────

        private void Update()
        {
            // Clean up any destroyed players
            runnersInside.RemoveWhere(p => p == null);
            itPlayersInside.RemoveWhere(p => p == null);

            // Handle role changes while inside the zone
            HandleRoleChanges();

            switch (CurrentState)
            {
                case ZoneState.Ready:
                    UpdateReady();
                    break;

                case ZoneState.Active:
                    UpdateActive();
                    break;

                case ZoneState.Paused:
                    UpdatePaused();
                    break;

                case ZoneState.Cooldown:
                    UpdateCooldown();
                    break;
            }

            UpdateVisuals();
            UpdatePlayerSafeZoneFlags();
        }

        // ──────────────────────────────────────────────
        //  STATE UPDATES
        // ──────────────────────────────────────────────

        private void UpdateReady()
        {
            // Waiting for a runner to enter — nothing to tick
        }

        private void UpdateActive()
        {
            // Countdown is running — no It players inside
            activeTimeRemaining -= Time.deltaTime;

            // Check if all runners left
            if (runnersInside.Count == 0)
            {
                Deactivate();
                return;
            }

            // Timer expired
            if (activeTimeRemaining <= 0f)
            {
                activeTimeRemaining = 0f;
                Deactivate();
                return;
            }

            // Check if an It player entered — pause
            if (itPlayersInside.Count > 0)
            {
                SetState(ZoneState.Paused);
                OnZonePaused?.Invoke();
            }
        }

        private void UpdatePaused()
        {
            // Clock is frozen while It player is inside

            // Check if all runners left
            if (runnersInside.Count == 0)
            {
                Deactivate();
                return;
            }

            // Resume if all It players left
            if (itPlayersInside.Count == 0)
            {
                SetState(ZoneState.Active);
            }
        }

        private void UpdateCooldown()
        {
            cooldownTimeRemaining -= Time.deltaTime;

            if (cooldownTimeRemaining <= 0f)
            {
                cooldownTimeRemaining = 0f;
                SetState(ZoneState.Ready);
                OnZoneReady?.Invoke();
            }
        }

        // ──────────────────────────────────────────────
        //  ACTIVATION / DEACTIVATION
        // ──────────────────────────────────────────────

        private void Activate()
        {
            activeTimeRemaining = activeTime;
            SetState(ZoneState.Active);
            OnZoneActivated?.Invoke();
        }

        private void Deactivate()
        {
            // Clear safe zone flags on all runners still inside
            foreach (var runner in runnersInside)
            {
                if (runner != null)
                {
                    runner.InSafeZone = false;
                }
            }

            cooldownTimeRemaining = cooldownTime;
            SetState(ZoneState.Cooldown);
            OnZoneDeactivated?.Invoke();
        }

        // ──────────────────────────────────────────────
        //  ROLE CHANGE HANDLING
        // ──────────────────────────────────────────────

        /// <summary>
        /// Players can change roles while inside the zone (e.g., a runner
        /// gets tagged outside then re-enters as It, or tag transfer happens).
        /// We need to keep our sets in sync with actual roles.
        /// </summary>
        private void HandleRoleChanges()
        {
            // Check runners who became It
            var runnersToRemove = new List<TagMovementController>();
            foreach (var player in runnersInside)
            {
                if (player.CurrentRole == TagMovementController.TagRole.It)
                {
                    runnersToRemove.Add(player);
                    itPlayersInside.Add(player);
                    player.InSafeZone = false;
                }
            }
            foreach (var player in runnersToRemove)
            {
                runnersInside.Remove(player);
            }

            // Check It players who became runners
            var itToRemove = new List<TagMovementController>();
            foreach (var player in itPlayersInside)
            {
                if (player.CurrentRole == TagMovementController.TagRole.Runner)
                {
                    itToRemove.Add(player);
                    // Only grant safe zone protection if zone is active
                    if (CurrentState == ZoneState.Active || CurrentState == ZoneState.Paused)
                    {
                        runnersInside.Add(player);
                        player.InSafeZone = true;
                    }
                }
            }
            foreach (var player in itToRemove)
            {
                itPlayersInside.Remove(player);
            }
        }

        // ──────────────────────────────────────────────
        //  TRIGGER DETECTION
        // ──────────────────────────────────────────────

        private void OnTriggerEnter2D(Collider2D other)
        {
            var player = other.GetComponent<TagMovementController>();
            if (player == null) return;

            if (player.CurrentRole == TagMovementController.TagRole.It)
            {
                itPlayersInside.Add(player);
            }
            else
            {
                // Runner entering
                if (CurrentState == ZoneState.Cooldown)
                {
                    // Zone is disabled — no protection
                    return;
                }

                runnersInside.Add(player);

                // Activate zone if it's ready
                if (CurrentState == ZoneState.Ready)
                {
                    Activate();
                }

                // Grant protection
                player.InSafeZone = true;
            }
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            var player = other.GetComponent<TagMovementController>();
            if (player == null) return;

            if (itPlayersInside.Contains(player))
            {
                itPlayersInside.Remove(player);
            }

            if (runnersInside.Contains(player))
            {
                runnersInside.Remove(player);
                player.InSafeZone = false;
            }
        }

        // ──────────────────────────────────────────────
        //  SAFE ZONE FLAG MANAGEMENT
        // ──────────────────────────────────────────────

        /// <summary>
        /// Ensure InSafeZone flags stay in sync with zone state.
        /// Runners only have protection when zone is Active or Paused.
        /// </summary>
        private void UpdatePlayerSafeZoneFlags()
        {
            bool zoneProtects = (CurrentState == ZoneState.Active || CurrentState == ZoneState.Paused);

            foreach (var runner in runnersInside)
            {
                if (runner != null)
                {
                    runner.InSafeZone = zoneProtects;
                }
            }
        }

        // ──────────────────────────────────────────────
        //  VISUALS
        // ──────────────────────────────────────────────

        private void UpdateVisuals()
        {
            Color targetColor;

            switch (CurrentState)
            {
                case ZoneState.Active:
                    if (activeTimeRemaining <= activeTime * warningThreshold)
                        targetColor = warningColor;
                    else
                        targetColor = activeColor;
                    break;

                case ZoneState.Paused:
                    targetColor = pausedColor;
                    break;

                case ZoneState.Cooldown:
                    targetColor = cooldownColor;
                    break;

                case ZoneState.Ready:
                default:
                    targetColor = readyColor;
                    break;
            }

            if (spriteRenderer != null)
            {
                spriteRenderer.color = targetColor;
            }
        }

        // ──────────────────────────────────────────────
        //  STATE MANAGEMENT
        // ──────────────────────────────────────────────

        private void SetState(ZoneState newState)
        {
            CurrentState = newState;
        }

        // ──────────────────────────────────────────────
        //  PUBLIC QUERIES
        // ──────────────────────────────────────────────

        /// <summary>
        /// Is this zone currently providing protection to runners inside?
        /// </summary>
        public bool IsActive => CurrentState == ZoneState.Active || CurrentState == ZoneState.Paused;

        /// <summary>
        /// Is this zone available for a runner to activate?
        /// </summary>
        public bool IsReady => CurrentState == ZoneState.Ready;

        /// <summary>
        /// Is this zone on cooldown?
        /// </summary>
        public bool IsOnCooldown => CurrentState == ZoneState.Cooldown;

        /// <summary>
        /// Normalized time remaining (1 = full, 0 = expired). 
        /// Useful for UI display.
        /// </summary>
        public float NormalizedTimeRemaining => activeTimeRemaining / activeTime;

        /// <summary>
        /// Check if a specific player can benefit from this zone.
        /// Used by AI decision-making.
        /// </summary>
        public bool IsAvailableForPlayer(TagMovementController player)
        {
            if (player.CurrentRole == TagMovementController.TagRole.It) return false;
            return CurrentState == ZoneState.Ready || CurrentState == ZoneState.Active || CurrentState == ZoneState.Paused;
        }

        /// <summary>
        /// Check if a specific player is currently safe in this zone.
        /// </summary>
        public bool IsPlayerSafe(TagMovementController player)
        {
            return runnersInside.Contains(player) && IsActive;
        }

        /// <summary>
        /// How many runners are currently inside.
        /// </summary>
        public int RunnerCount => runnersInside.Count;

        /// <summary>
        /// How many It players are currently inside.
        /// </summary>
        public int ItPlayerCount => itPlayersInside.Count;

        // ──────────────────────────────────────────────
        //  RESET
        // ──────────────────────────────────────────────

        /// <summary>
        /// Reset the zone to its initial ready state.
        /// Clears all tracked players and timers.
        /// </summary>
        public void ResetZone()
        {
            // Clear safe zone flags on any runners still tracked
            foreach (var runner in runnersInside)
            {
                if (runner != null)
                {
                    runner.InSafeZone = false;
                }
            }

            runnersInside.Clear();
            itPlayersInside.Clear();
            activeTimeRemaining = 0f;
            cooldownTimeRemaining = 0f;
            SetState(ZoneState.Ready);
        }
    }
}