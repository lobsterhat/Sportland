using UnityEngine;
using System.Collections.Generic;
using Sportland.Sports.Tag;

namespace Sportland.Sports.Tag
{
    /// <summary>
    /// Handles resetting the game state for testing purposes.
    /// Resets all players to starting positions, restores stamina,
    /// clears all timers, and resets safe zones.
    /// 
    /// Triggered by PS4 touchpad press or keyboard R key.
    /// 
    /// Drop this on the TagGameManager GameObject.
    /// It automatically captures starting positions on Start().
    /// </summary>
    public class GameResetManager : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        //  CONFIGURATION
        // ──────────────────────────────────────────────

        [Header("=== STARTING IT ===")]
        [Tooltip("The player who starts as It after a reset. Leave empty for random.")]
        [SerializeField] private TagMovementController startingItPlayer;

        // ──────────────────────────────────────────────
        //  RUNTIME STATE
        // ──────────────────────────────────────────────

        private struct PlayerStartState
        {
            public TagMovementController controller;
            public Vector3 startPosition;
        }

        private List<PlayerStartState> playerStartStates = new List<PlayerStartState>();
        private SafeZone[] safeZones;

        private Sportland.InputHandling.TagInputActions controls;

        // ──────────────────────────────────────────────
        //  INITIALIZATION
        // ──────────────────────────────────────────────

        private void Start()
        {
            controls = new Sportland.InputHandling.TagInputActions();
            controls.Enable();
            CaptureStartingState();
        }

        private void OnDestroy()
        {
            controls?.Disable();
        }

        private void CaptureStartingState()
        {
            playerStartStates.Clear();

            var players = FindObjectsByType<TagMovementController>(FindObjectsSortMode.None);
            foreach (var player in players)
            {
                playerStartStates.Add(new PlayerStartState
                {
                    controller = player,
                    startPosition = player.transform.position
                });
            }

            safeZones = FindObjectsByType<SafeZone>(FindObjectsSortMode.None);

            Debug.Log($"[GameReset] Captured starting state for {playerStartStates.Count} players and {safeZones.Length} safe zones.");
        }

        // ──────────────────────────────────────────────
        //  UPDATE — INPUT DETECTION
        // ──────────────────────────────────────────────

        private void Update()
        {
            if (controls != null && controls.Reset.WasPressedThisFrame())
            {
                ResetGame();
            }
        }

        // ──────────────────────────────────────────────
        //  RESET
        // ──────────────────────────────────────────────

        public void ResetGame()
        {
            Debug.Log("[GameReset] Resetting game...");

            ResetPlayers();
            ResetSafeZones();

            Debug.Log("[GameReset] Game reset complete.");
        }

        private void ResetPlayers()
        {
            foreach (var startState in playerStartStates)
            {
                var player = startState.controller;
                if (player == null) continue;

                // Reset position
                player.transform.position = startState.startPosition;

                // Reset velocity
                var rb = player.GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    rb.linearVelocity = Vector2.zero;
                }

                // Reset movement state (stamina, speed, etc.)
                player.ResetMovementState();

                // Reset to runner first
                player.BecomeRunner();

                // Clear safe zone flag
                player.InSafeZone = false;
                player.IsImmune = false;
            }

            // Assign starting It
            if (playerStartStates.Count > 0)
            {
                TagMovementController itPlayer;

                if (startingItPlayer != null)
                {
                    itPlayer = startingItPlayer;
                }
                else
                {
                    itPlayer = playerStartStates[Random.Range(0, playerStartStates.Count)].controller;
                }

                itPlayer.BecomeIt();
                Debug.Log($"[GameReset] {itPlayer.gameObject.name} is It.");
            }
        }

        private void ResetSafeZones()
        {
            foreach (var zone in safeZones)
            {
                if (zone != null)
                {
                    zone.ResetZone();
                }
            }
        }
    }
}