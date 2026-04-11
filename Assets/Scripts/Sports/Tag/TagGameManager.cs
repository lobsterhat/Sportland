using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace Sportland.Sports.Tag
{
    /// <summary>
    /// Manages an elimination-mode Tag game session.
    ///
    /// Rules:
    ///   - It players (alwaysIt) must tag all runners within the time limit
    ///   - Each tag permanently eliminates that runner
    ///   - It team wins if all runners are eliminated before time runs out
    ///   - Runners win if at least one survives the full timer
    ///
    /// Handles player registration, role assignment, tag-to-elimination routing,
    /// timer countdown, win/lose detection, and a minimal screen-space HUD.
    /// </summary>
    public class TagGameManager : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        //  CONFIGURATION
        // ──────────────────────────────────────────────

        [Header("=== GAME SETUP ===")]
        [Tooltip("Index of the player who starts as It when alwaysIt is not used. -1 = random.")]
        [SerializeField] private int startingItIndex = -1;

        [Tooltip("Total seconds runners have to survive.")]
        [SerializeField] private float gameDuration = 60f;

        [Header("=== VISUAL FEEDBACK ===")]
        [SerializeField] private Color itColor        = new Color(1f, 0.3f, 0.3f, 1f);
        [SerializeField] private Color runnerColor    = Color.white;
        [SerializeField] private Color immuneColor    = new Color(1f, 1f, 0.5f, 1f);
        [SerializeField] private Color eliminatedColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
        [SerializeField] private Color bargeStunnedColor = new Color(0.5f, 0.9f, 1f, 1f); // bright cyan flash

        // ──────────────────────────────────────────────
        //  RUNTIME STATE
        // ──────────────────────────────────────────────

        private List<TagMovementController> allPlayers  = new List<TagMovementController>();
        private List<SpriteRenderer>        playerSprites = new List<SpriteRenderer>();
        private TagMovementController       currentItPlayer;

        private float timeRemaining;
        private bool  gameOver;
        private int   totalRunners;
        private int   runnersRemaining;

        // ──────────────────────────────────────────────
        //  HUD
        // ──────────────────────────────────────────────

        private Text timerLabel;
        private Text runnerCountLabel;
        private Text resultLabel;

        // ──────────────────────────────────────────────
        //  EVENTS
        // ──────────────────────────────────────────────

        public System.Action<TagMovementController> OnPlayerEliminated;
        public System.Action<bool>                  OnGameOver; // true = It wins

        // ──────────────────────────────────────────────
        //  INITIALIZATION
        // ──────────────────────────────────────────────

        private void Start()
        {
            FindAllPlayers();
            AssignStartingIt();
            UpdateAllVisuals();
            BuildHUD();

            timeRemaining = gameDuration;
        }

        private void FindAllPlayers()
        {
            allPlayers.Clear();
            playerSprites.Clear();

            var found = FindObjectsByType<TagMovementController>(FindObjectsSortMode.None);

            foreach (var player in found)
            {
                allPlayers.Add(player);
                playerSprites.Add(player.GetComponentInChildren<SpriteRenderer>());

                // Listen for tags fired by this player (It fires OnTagged with the runner as arg)
                player.OnTagged    += HandleTagOccurred;
                player.OnBecameIt  += HandlePlayerBecameIt;
                player.OnFuseExpired += () => HandlePlayerEliminated(player);
            }

            // Count runners (non-alwaysIt characters)
            // We identify runners as those that will start as Runner after AssignStartingIt
            // For counting purposes, count everyone; we'll recount after assignment.
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
                itIndex = startingItIndex;
            else
                itIndex = Random.Range(0, allPlayers.Count);

            foreach (var player in allPlayers)
                player.BecomeRunner();

            allPlayers[itIndex].BecomeIt();
            currentItPlayer = allPlayers[itIndex];

            // Count runners after assignment
            runnersRemaining = 0;
            foreach (var player in allPlayers)
            {
                if (player.CurrentRole == TagMovementController.TagRole.Runner)
                    runnersRemaining++;
            }
            totalRunners = runnersRemaining;

            Debug.Log($"[TagGameManager] {currentItPlayer.gameObject.name} starts as It. " +
                      $"{totalRunners} runners to catch.");
        }

        // ──────────────────────────────────────────────
        //  UPDATE
        // ──────────────────────────────────────────────

        private void Update()
        {
            if (gameOver) return;

            // Tick timer
            timeRemaining -= Time.deltaTime;
            if (timeRemaining <= 0f)
            {
                timeRemaining = 0f;
                TriggerGameOver(itWins: false);
                return;
            }

            UpdateHUD();
            UpdateAllVisuals();
            CheckWinCondition();
        }

        // ──────────────────────────────────────────────
        //  EVENT HANDLERS
        // ──────────────────────────────────────────────

        /// <summary>
        /// Called when any It player fires OnTagged.
        /// The argument is the runner who was tagged.
        /// </summary>
        private void HandleTagOccurred(TagMovementController taggedPlayer)
        {
            if (gameOver) return;
            if (taggedPlayer.IsEliminated) return;
            if (taggedPlayer.CurrentRole != TagMovementController.TagRole.Runner) return;

            taggedPlayer.Eliminate();
            HandlePlayerEliminated(taggedPlayer);
        }

        private void HandlePlayerBecameIt(TagMovementController player)
        {
            currentItPlayer = player;
        }

        private void HandlePlayerEliminated(TagMovementController player)
        {
            runnersRemaining = Mathf.Max(0, runnersRemaining - 1);
            Debug.Log($"[TagGameManager] {player.gameObject.name} eliminated! " +
                      $"{runnersRemaining}/{totalRunners} runners remaining.");
            OnPlayerEliminated?.Invoke(player);
        }

        // ──────────────────────────────────────────────
        //  WIN CONDITION
        // ──────────────────────────────────────────────

        private void CheckWinCondition()
        {
            if (runnersRemaining <= 0)
                TriggerGameOver(itWins: true);
        }

        private void TriggerGameOver(bool itWins)
        {
            if (gameOver) return;
            gameOver = true;
            timeRemaining = Mathf.Max(0f, timeRemaining);

            string message = itWins
                ? "IT WINS!\nAll runners tagged!"
                : "RUNNERS WIN!\nSurvived the clock!";

            Debug.Log($"[TagGameManager] GAME OVER — {(itWins ? "It wins" : "Runners win")}");

            if (resultLabel != null)
            {
                resultLabel.text  = message;
                resultLabel.color = itWins ? new Color(1f, 0.35f, 0.35f) : new Color(0.35f, 1f, 0.55f);
                resultLabel.gameObject.SetActive(true);
            }

            UpdateHUD();
            OnGameOver?.Invoke(itWins);
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
                    playerSprites[i].color = eliminatedColor;
                else if (player.IsBargeStunned)
                    playerSprites[i].color = bargeStunnedColor;
                else if (player.IsImmune)
                    playerSprites[i].color = immuneColor;
                else if (player.CurrentRole == TagMovementController.TagRole.It)
                    playerSprites[i].color = itColor;
                else
                    playerSprites[i].color = runnerColor;
            }
        }

        // ──────────────────────────────────────────────
        //  HUD
        // ──────────────────────────────────────────────

        private void BuildHUD()
        {
            var canvasGO = new GameObject("GameHUD");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            Font font = ResolveFont();

            // ── TIMER (top-centre) ──
            timerLabel = CreateLabel(canvas.transform, "Timer",
                anchorMin:        new Vector2(0.5f, 1f),
                anchorMax:        new Vector2(0.5f, 1f),
                pivot:            new Vector2(0.5f, 1f),
                anchoredPosition: new Vector2(0f, -20f),
                sizeDelta:        new Vector2(240f, 80f),
                fontSize:         64,
                color:            Color.white,
                style:            FontStyle.Bold,
                font:             font);

            // ── RUNNER COUNT (below timer) ──
            runnerCountLabel = CreateLabel(canvas.transform, "RunnerCount",
                anchorMin:        new Vector2(0.5f, 1f),
                anchorMax:        new Vector2(0.5f, 1f),
                pivot:            new Vector2(0.5f, 1f),
                anchoredPosition: new Vector2(0f, -100f),
                sizeDelta:        new Vector2(320f, 48f),
                fontSize:         32,
                color:            new Color(0.85f, 0.85f, 0.85f),
                style:            FontStyle.Normal,
                font:             font);

            // ── RESULT (centre screen, hidden until game over) ──
            resultLabel = CreateLabel(canvas.transform, "Result",
                anchorMin:        new Vector2(0.5f, 0.5f),
                anchorMax:        new Vector2(0.5f, 0.5f),
                pivot:            new Vector2(0.5f, 0.5f),
                anchoredPosition: Vector2.zero,
                sizeDelta:        new Vector2(700f, 160f),
                fontSize:         72,
                color:            Color.yellow,
                style:            FontStyle.Bold,
                font:             font);
            resultLabel.gameObject.SetActive(false);

            UpdateHUD();
        }

        private void UpdateHUD()
        {
            if (timerLabel != null)
            {
                int secs = Mathf.CeilToInt(timeRemaining);
                int m    = secs / 60;
                int s    = secs % 60;
                timerLabel.text  = $"{m}:{s:00}";

                // Turn timer red in the last 10 seconds
                timerLabel.color = timeRemaining <= 10f && !gameOver
                    ? new Color(1f, 0.3f, 0.3f)
                    : Color.white;
            }

            if (runnerCountLabel != null)
            {
                runnerCountLabel.text = gameOver
                    ? ""
                    : $"{runnersRemaining} / {totalRunners}  runners remaining";
            }
        }

        private Text CreateLabel(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 sizeDelta,
            int fontSize, Color color, FontStyle style, Font font)
        {
            var go   = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rect             = go.AddComponent<RectTransform>();
            rect.anchorMin       = anchorMin;
            rect.anchorMax       = anchorMax;
            rect.pivot           = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta       = sizeDelta;

            var text         = go.AddComponent<Text>();
            text.font        = font;
            text.fontSize    = fontSize;
            text.fontStyle   = style;
            text.alignment   = TextAnchor.MiddleCenter;
            text.color       = color;
            text.text        = "";

            // Readable drop shadow via an Outline component
            var outline      = go.AddComponent<Outline>();
            outline.effectColor    = new Color(0f, 0f, 0f, 0.7f);
            outline.effectDistance = new Vector2(2f, -2f);

            return text;
        }

        private static Font ResolveFont()
        {
            // Unity 2022+ ships LegacyRuntime.ttf as the default built-in font
            var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f != null) return f;
            // Older versions shipped Arial.ttf
            f = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (f != null) return f;
            // Last resort: ask the OS
            return Font.CreateDynamicFontFromOSFont("Arial", 12);
        }

        // ──────────────────────────────────────────────
        //  CLEANUP
        // ──────────────────────────────────────────────

        private void OnDestroy()
        {
            foreach (var player in allPlayers)
            {
                if (player == null) continue;
                player.OnTagged   -= HandleTagOccurred;
                player.OnBecameIt -= HandlePlayerBecameIt;
            }
        }
    }
}
