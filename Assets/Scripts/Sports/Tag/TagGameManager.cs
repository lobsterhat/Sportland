using UnityEngine;
using System.Collections.Generic;
using Sportland.Sports.Tag;

namespace Sportland.Sports.Tag
{
    /// <summary>
    /// Manages a Tag game session. Handles:
    ///   - Finding and registering all players in the scene
    ///   - Assigning initial It player
    ///   - Listening for tag transfers
    ///   - Visual feedback (color tinting) for roles
    ///   - Burning Fuse elimination tracking
    ///   - Win condition detection
    /// 
    /// Drop this on an empty GameObject in the scene.
    /// It will automatically find all TagMovementControllers.
    /// </summary>
    public class TagGameManager : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        //  CONFIGURATION
        // ──────────────────────────────────────────────

        [Header("=== GAME SETUP ===")]
        [Tooltip("Index of the player who starts as It. -1 = random.")]
        [SerializeField] private int startingItIndex = -1;

        [Header("=== VISUAL FEEDBACK ===")]
        [Tooltip("Color tint applied to the It player's sprite.")]
        [SerializeField] private Color itColor = new Color(1f, 0.3f, 0.3f, 1f);

        [Tooltip("Color tint applied to Runner sprites.")]
        [SerializeField] private Color runnerColor = Color.white;

        [Tooltip("Color tint applied to immune players (brief flash after tag transfer).")]
        [SerializeField] private Color immuneColor = new Color(1f, 1f, 0.5f, 1f);

        [Tooltip("Color tint applied to eliminated players.")]
        [SerializeField] private Color eliminatedColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);

        // ──────────────────────────────────────────────
        //  RUNTIME STATE
        // ──────────────────────────────────────────────

        private List<TagMovementController> allPlayers = new List<TagMovementController>();
        private List<SpriteRenderer> playerSprites = new List<SpriteRenderer>();
        private TagMovementController currentItPlayer;
        private int activePlayers;
        private bool gameOver;

        // ──────────────────────────────────────────────
        //  EVENTS
        // ──────────────────────────────────────────────

        public System.Action<TagMovementController> OnPlayerEliminated;
        public System.Action<TagMovementController> OnGameOver; // passes the winner

        // ──────────────────────────────────────────────
        //  INITIALIZATION
        // ──────────────────────────────────────────────

        private void Start()
        {
            FindAllPlayers();
            AssignStartingIt();
            UpdateAllVisuals();
        }

        private void FindAllPlayers()
        {
            allPlayers.Clear();
            playerSprites.Clear();

            var found = FindObjectsByType<TagMovementController>(FindObjectsSortMode.None);

            foreach (var player in found)
            {
                allPlayers.Add(player);

                // Find the sprite renderer (on the Sprite child)
                var sprite = player.GetComponentInChildren<SpriteRenderer>();
                playerSprites.Add(sprite);

                // Subscribe to events
                player.OnBecameIt += HandlePlayerBecameIt;
                player.OnFuseExpired += () => HandlePlayerEliminated(player);
            }

            activePlayers = allPlayers.Count;

            Debug.Log($"[TagGameManager] Found {allPlayers.Count} players.");
        }

        private void AssignStartingIt()
        {
            if (allPlayers.Count == 0)
            {
                Debug.LogError("[TagGameManager] No players found in scene!");
                return;
            }

            int itIndex;
            if (startingItIndex >= 0 && startingItIndex < allPlayers.Count)
            {
                itIndex = startingItIndex;
            }
            else
            {
                itIndex = Random.Range(0, allPlayers.Count);
            }

            // Set everyone as Runner first
            foreach (var player in allPlayers)
            {
                player.BecomeRunner();
            }

            // Set the chosen player as It
            allPlayers[itIndex].BecomeIt();
            currentItPlayer = allPlayers[itIndex];

            Debug.Log($"[TagGameManager] {currentItPlayer.gameObject.name} starts as It!");
        }

        // ──────────────────────────────────────────────
        //  UPDATE
        // ──────────────────────────────────────────────

        private void Update()
        {
            if (gameOver) return;

            UpdateAllVisuals();
            CheckWinCondition();
        }

        // ──────────────────────────────────────────────
        //  EVENT HANDLERS
        // ──────────────────────────────────────────────

        private void HandlePlayerBecameIt(TagMovementController player)
        {
            currentItPlayer = player;
            Debug.Log($"[TagGameManager] {player.gameObject.name} is now It!");
        }

        private void HandlePlayerEliminated(TagMovementController player)
        {
            activePlayers--;
            Debug.Log($"[TagGameManager] {player.gameObject.name} eliminated! " +
                      $"{activePlayers} players remaining.");

            OnPlayerEliminated?.Invoke(player);
        }

        // ──────────────────────────────────────────────
        //  WIN CONDITION
        // ──────────────────────────────────────────────

        private void CheckWinCondition()
        {
            // Find non-eliminated players
            TagMovementController lastSurvivor = null;
            int aliveCount = 0;

            foreach (var player in allPlayers)
            {
                if (!player.IsEliminated)
                {
                    aliveCount++;
                    lastSurvivor = player;
                }
            }

            // Game ends when only one player remains
            if (aliveCount <= 1 && !gameOver)
            {
                gameOver = true;

                if (lastSurvivor != null)
                {
                    Debug.Log($"[TagGameManager] GAME OVER! {lastSurvivor.gameObject.name} wins!");
                    OnGameOver?.Invoke(lastSurvivor);
                }
                else
                {
                    Debug.Log("[TagGameManager] GAME OVER! No survivors!");
                    OnGameOver?.Invoke(null);
                }
            }
        }

        // ──────────────────────────────────────────────
        //  VISUAL FEEDBACK
        // ──────────────────────────────────────────────

        private void UpdateAllVisuals()
        {
            for (int i = 0; i < allPlayers.Count; i++)
            {
                if (playerSprites[i] == null) continue;

                var player = allPlayers[i];

                if (player.IsEliminated)
                {
                    playerSprites[i].color = eliminatedColor;
                }
                else if (player.IsImmune)
                {
                    playerSprites[i].color = immuneColor;
                }
                else if (player.CurrentRole == TagMovementController.TagRole.It)
                {
                    playerSprites[i].color = itColor;
                }
                else
                {
                    playerSprites[i].color = runnerColor;
                }
            }
        }

        // ──────────────────────────────────────────────
        //  CLEANUP
        // ──────────────────────────────────────────────

        private void OnDestroy()
        {
            foreach (var player in allPlayers)
            {
                if (player != null)
                {
                    player.OnBecameIt -= HandlePlayerBecameIt;
                }
            }
        }
    }
}