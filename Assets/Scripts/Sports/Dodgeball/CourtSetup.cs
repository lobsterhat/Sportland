using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Sportland.Sports.Dodgeball
{
    public enum Team { A, B }
    public enum PlayerRole { Infielder, Outfielder }

    /// <summary>
    /// Defines a single player's starting position and metadata.
    /// </summary>
    [System.Serializable]
    public struct PlayerSpawn
    {
        public string id;
        public Team team;
        public PlayerRole role;
        public Vector2 position;
    }

    /// <summary>
    /// Constructs the dodgeball playfield: court bounds, center divider,
    /// and the 12 initial player anchor points (3 infielders + 3 outfielders per team).
    ///
    /// Coordinate system:
    ///   - Origin (0,0) is at the center of the court.
    ///   - X axis runs along the court's long side (-9 to +9).
    ///   - Y axis runs along the court's short side (-4.5 to +4.5).
    ///   - 1 Unity unit = 1 meter.
    /// </summary>
    public class CourtSetup : MonoBehaviour
    {
        // ---- Court dimensions (volleyball court: 18m x 9m) ----
        public const float CourtWidth = 18f;   // X span
        public const float CourtHeight = 9f;   // Y span
        public const float HalfWidth = CourtWidth / 2f;   // 9
        public const float HalfHeight = CourtHeight / 2f; // 4.5

        /// <summary>Outer hard bound (half-width) of the play area — the court + the outfielder strip on either side. Players are clamped to this in PlayerMovement.LateUpdate so nothing can slide off-screen.</summary>
        public const float PlayAreaHalfWidth = HalfWidth + ZoneFactory.StripDepth;   // 12
        /// <summary>Outer hard bound (half-height) of the play area — the court + the outfielder strip top and bottom.</summary>
        public const float PlayAreaHalfHeight = HalfHeight + ZoneFactory.StripDepth; // 7.5

        // ---- Outfielder offsets from court boundary ----
        // How far behind the baseline outfielders stand:
        public const float BackOutfielderOffset = 1.5f;   // beyond ±9 on X
        // How far outside the sideline the side-outfielders stand:
        public const float SideOutfielderOffset = 1.5f;   // beyond ±4.5 on Y
        // How far in from the corner along the sideline:
        // (centered on each opposing half = ±4.5 from origin on X)
        public const float SideOutfielderX = 4.5f;

        [Header("Prefabs")]
        // Sprite to use as the player's visual child. We always build the player
        // root in code (Rigidbody2D + PlayerMovement + PlayerZoneTracker) and
        // attach this prefab — or a procedural circle if null — as child[0],
        // which PlayerMovement bobs for the jump arc. Team tint is applied at
        // runtime via DodgeballPlayerVisual.
        [SerializeField] private GameObject spritePrefab;
        // Optional prefab for the dodgeball. Must carry the Ball component
        // itself; if null, CourtSetup spawns a procedural ball at runtime.
        [SerializeField] private GameObject ballPrefab;
        [SerializeField] private GameObject courtPrefab;       // optional: visual court sprite
        [SerializeField] private GameObject centerLinePrefab;  // optional: visual divider

        [Header("Player Control")]
        // Spawn ID of the human-driven character. A_In_2 is Team A's center
        // infielder at (-4.5, 0).
        [SerializeField] private string playerControlledId = "A_In_2";
        [Tooltip("Hand every player to the CPU brain (no human input). Pure AI-vs-AI for observation; toggleable live from the Match controls panel.")]
        [SerializeField] private bool allAIControlled = false;

        [Header("Diagnostics")]
        [Tooltip("Spawn an on-screen readout of player/ball state for tuning.")]
        [SerializeField] private bool showDiagnosticsHud = true;
        [Tooltip("Spawn the debug cannon (C / R1) that fires balls at the controlled player.")]
        [SerializeField] private bool spawnDebugCannon = true;
        [Tooltip("Float a jersey number (e.g. A2) above each player for identification.")]
        [SerializeField] private bool showPlayerLabels = true;
        [Tooltip("Show the play-by-play log panel (top-right, under the cannon).")]
        [SerializeField] private bool showPlayByPlay = true;
        [Tooltip("Show the Match controls panel (mode dropdown + Reset button) top-right.")]
        [SerializeField] private bool showMatchControls = true;
        [SerializeField] private bool showTuningPanel = true;

        [Header("Match")]
        [Tooltip("Scoring rules asset. Leave null to use Start Mode below (built in code).")]
        [SerializeField] private GameMode gameMode;
        [Tooltip("Which built-in mode to start in when no Game Mode asset is assigned.")]
        [SerializeField] private GameMode.Preset startMode = GameMode.Preset.RunningHits;
        [Tooltip("Run the match scorer (score + clock + win condition).")]
        [SerializeField] private bool runMatch = true;
        [Tooltip("Switch modes at runtime for testing: M cycles; F1-F4 pick Running Hits / Elimination / Energy / Hybrid. Resets the match.")]
        [SerializeField] private bool allowRuntimeModeSwitch = true;

        [Header("Runtime")]
        [SerializeField] private List<GameObject> spawnedPlayers = new List<GameObject>();

        private DodgeballMatch match;
        private Ball ball;
        private GameMode.Preset currentPreset;

        /// <summary>The mode the match is currently running in (read by the controls panel).</summary>
        public GameMode.Preset CurrentPreset => currentPreset;

        /// <summary>True if all players are CPU-controlled (no human input).</summary>
        public bool AllAIControlled => allAIControlled;

        /// <summary>Toggle pure AI-vs-AI play live. On: remove the human input wherever it currently lives. Off: re-attach it to the spawn-time controlled player (or any active player if that one's eliminated).</summary>
        public void SetAllAI(bool value)
        {
            allAIControlled = value;
            if (value)
            {
                if (DodgeballPlayerInput.Current != null) Destroy(DodgeballPlayerInput.Current);
            }
            else if (DodgeballPlayerInput.Current == null)
            {
                var go = FindActiveSpawnedById(playerControlledId) ?? FirstActiveSpawnedPlayer();
                if (go != null) go.AddComponent<DodgeballPlayerInput>();
            }
        }

        private GameObject FindActiveSpawnedById(string id)
        {
            for (int i = 0; i < spawnedPlayers.Count; i++)
            {
                var go = spawnedPlayers[i];
                if (go == null || !go.activeSelf) continue;
                var tr = go.GetComponent<PlayerZoneTracker>();
                if (tr != null && tr.Spawn.id == id) return go;
            }
            return null;
        }

        private GameObject FirstActiveSpawnedPlayer()
        {
            for (int i = 0; i < spawnedPlayers.Count; i++)
                if (spawnedPlayers[i] != null && spawnedPlayers[i].activeSelf) return spawnedPlayers[i];
            return null;
        }

        private void Awake()
        {
            BuildCourt();
            SpawnAllPlayers();
            SpawnBall();
            if (showDiagnosticsHud) gameObject.AddComponent<DodgeballDiagnosticsHUD>();
            if (spawnDebugCannon) gameObject.AddComponent<DodgeballCannon>();
            if (showPlayerLabels) gameObject.AddComponent<DodgeballPlayerLabels>();
            if (showPlayByPlay) gameObject.AddComponent<DodgeballPlayByPlay>();
            if (showMatchControls) gameObject.AddComponent<DodgeballMatchControls>();
            if (showTuningPanel) gameObject.AddComponent<DodgeballTuningPanel>();
            if (runMatch)
            {
                match = gameObject.AddComponent<DodgeballMatch>();
                currentPreset = startMode;
                match.Configure(gameMode != null ? gameMode : GameMode.Create(startMode));
            }
        }

        private void Update()
        {
            if (!allowRuntimeModeSwitch || match == null) return;
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.f1Key.wasPressedThisFrame)      SwitchMode(GameMode.Preset.RunningHits);
            else if (kb.f2Key.wasPressedThisFrame) SwitchMode(GameMode.Preset.Elimination);
            else if (kb.f3Key.wasPressedThisFrame) SwitchMode(GameMode.Preset.Energy);
            else if (kb.f4Key.wasPressedThisFrame) SwitchMode(GameMode.Preset.Hybrid);
            else if (kb.mKey.wasPressedThisFrame)  SwitchMode(NextPreset(currentPreset));
        }

        private static GameMode.Preset NextPreset(GameMode.Preset p)
        {
            switch (p)
            {
                case GameMode.Preset.RunningHits: return GameMode.Preset.Elimination;
                case GameMode.Preset.Elimination: return GameMode.Preset.Energy;
                case GameMode.Preset.Energy:      return GameMode.Preset.Hybrid;
                default:                          return GameMode.Preset.RunningHits;
            }
        }

        // Rebuild the chosen mode and reset to a fresh match in it.
        public void SwitchMode(GameMode.Preset preset)
        {
            currentPreset = preset;
            RestartInMode(GameMode.Create(preset));
            Debug.Log($"[Dodgeball] Switched to mode: {preset}");
        }

        /// <summary>Restart the current mode in place (Reset button).</summary>
        public void RestartMatch() => SwitchMode(currentPreset);

        // Revive + reposition every spawned player (incl. any eliminated under a
        // previous mode), reset the ball to a loose ball at center, and reconfigure
        // the scorer — a clean fresh match in the new mode.
        private void RestartInMode(GameMode mode)
        {
            for (int i = 0; i < spawnedPlayers.Count; i++)
            {
                var go = spawnedPlayers[i];
                if (go == null) continue;
                var tracker = go.GetComponent<PlayerZoneTracker>();
                if (tracker != null) go.transform.position = tracker.Spawn.position;
                var body = go.GetComponent<Rigidbody2D>();
                if (body != null) body.linearVelocity = Vector2.zero;
                if (!go.activeSelf) go.SetActive(true);   // re-adds to PlayerZoneTracker.All
            }
            if (ball != null) ball.ResetLoose(Vector2.zero);
            if (match != null) match.Configure(mode);
            DodgeballPlayByPlay.Clear();   // fresh log for the new match
        }

        private void SpawnBall()
        {
            GameObject ballGO;
            if (ballPrefab != null)
            {
                ballGO = Instantiate(ballPrefab, transform);
                ballGO.name = ballPrefab.name;
            }
            else
            {
                ballGO = new GameObject("Ball");
                ballGO.transform.SetParent(transform, false);
                ballGO.AddComponent<Ball>();
            }
            ballGO.transform.localPosition = Vector3.zero;
            ball = ballGO.GetComponent<Ball>();
        }

        private void BuildCourt()
        {
            // Court visual + collider boundary
            if (courtPrefab != null)
            {
                var court = Instantiate(courtPrefab, Vector3.zero, Quaternion.identity, transform);
                court.name = "Court";
                court.transform.localScale = new Vector3(CourtWidth, CourtHeight, 1f);
            }

            // Center line
            if (centerLinePrefab != null)
            {
                var line = Instantiate(centerLinePrefab, Vector3.zero, Quaternion.identity, transform);
                line.name = "CenterLine";
                line.transform.localScale = new Vector3(0.1f, CourtHeight, 1f);
            }
        }

        private void SpawnAllPlayers()
        {
            int aCount = 0, bCount = 0;
            foreach (var spawn in GetAllSpawns())
            {
                int number = spawn.team == Team.A ? ++aCount : ++bCount;
                SpawnPlayer(spawn, number);
            }
        }

        private void SpawnPlayer(PlayerSpawn spawn, int number)
        {
            var go = BuildPlayer();
            go.name = spawn.id;
            go.transform.position = new Vector3(spawn.position.x, spawn.position.y, 0f);

            var tracker = go.GetComponent<PlayerZoneTracker>();
            if (tracker == null) tracker = go.AddComponent<PlayerZoneTracker>();
            tracker.Initialize(spawn);
            tracker.Number = number;

            // Ability ratings used by skill checks (catching, etc.) and AI
            // routing decisions (ScorePotential01). Randomized per-player with
            // a deterministic seed off spawn.id so each player has a stable
            // personality across game restarts; means are role-biased
            // (infielders lean stronger throwers, outfielders lean better
            // catchers — they retrieve a lot of loose balls).
            var gen = go.GetComponent<GeneralAttributes>() ?? go.AddComponent<GeneralAttributes>();
            var dba = go.GetComponent<DodgeballAttributes>() ?? go.AddComponent<DodgeballAttributes>();
            RandomizeStats(spawn, dba, gen);

            var visual = go.GetComponentInChildren<DodgeballPlayerVisual>();
            if (visual != null) visual.Configure(spawn.team, spawn.role, tracker);

            // Every player gets a CPU brain; the human input component
            // (added below) disables it on the controlled player while present.
            go.AddComponent<DodgeballAI>();
            if (!allAIControlled && spawn.id == playerControlledId)
            {
                go.AddComponent<DodgeballPlayerInput>();
            }

            spawnedPlayers.Add(go);
        }

        // Per-player stat differentiation. Deterministic RNG keyed off spawn.id
        // so a given player has a stable personality across restarts; role-
        // biased means so infielders feel like throwers and outfielders feel
        // like retrievers. Spread ±18 around the mean (clamped to 0..100)
        // gives meaningful variety without anyone being unusable.
        //
        // Star players: each team's A_In_1 / B_In_1 get a +starBoost shift on
        // every stat, so they're consistently the highest-scoring shooters
        // and the AI's best-player routing converges on them.
        private static void RandomizeStats(PlayerSpawn spawn, DodgeballAttributes dba, GeneralAttributes gen)
        {
            const float starBoost = 18f;
            var rng = new System.Random(spawn.id.GetHashCode());
            float Roll(float mean, float spread) =>
                Mathf.Clamp(mean + ((float)(rng.NextDouble() - 0.5) * 2f * spread), 0f, 100f);

            bool isInfielder = spawn.role == PlayerRole.Infielder;
            bool isStar = spawn.id == "A_In_1" || spawn.id == "B_In_1";
            float boost = isStar ? starBoost : 0f;

            dba.throwSpeed    = Roll((isInfielder ? 65f : 50f) + boost, 18f);
            dba.throwAccuracy = Roll((isInfielder ? 65f : 55f) + boost, 18f);
            dba.anticipation  = Roll(60f + boost, 18f);
            dba.catching      = Roll((isInfielder ? 60f : 70f) + boost, 15f);

            gen.luck              = Roll(50f + boost * 0.5f, 15f);
            gen.toughness         = Roll(50f + boost, 18f);
            gen.changeOfDirection = Roll(50f + boost * 0.5f, 18f);
            gen.endurance         = Roll(50f + boost * 0.5f, 15f);
            gen.maxEnergy         = Mathf.Lerp(80f, 120f, (float)rng.NextDouble()) + (isStar ? 20f : 0f);
        }

        // Builds the player root in code and attaches the sprite (or a
        // procedural placeholder) as the Visual child. PlayerMovement bobs
        // child[0] for the jump arc, so the visual must live there.
        private GameObject BuildPlayer()
        {
            var go = new GameObject("DodgeballPlayer");
            go.transform.SetParent(transform, false);

            GameObject visualGO;
            if (spritePrefab != null)
            {
                visualGO = Instantiate(spritePrefab, go.transform);
                visualGO.name = "Visual";
                visualGO.transform.localPosition = Vector3.zero;
            }
            else
            {
                visualGO = new GameObject("Visual");
                visualGO.transform.SetParent(go.transform, false);
            }

            if (visualGO.GetComponent<DodgeballPlayerVisual>() == null)
                visualGO.AddComponent<DodgeballPlayerVisual>();

            // PlayerMovement RequireComponent pulls in Rigidbody2D automatically.
            // Added after the visual child exists so PlayerMovement.Awake sees it.
            go.AddComponent<PlayerMovement>();

            return go;
        }

        /// <summary>
        /// Returns all 12 starting positions for both teams.
        /// Team A occupies the left half (negative X) and surrounds Team B's right half.
        /// Team B occupies the right half (positive X) and surrounds Team A's left half.
        /// </summary>
        public static IEnumerable<PlayerSpawn> GetAllSpawns()
        {
            // === Team A — infielders on left half ===
            yield return new PlayerSpawn { id = "A_In_1", team = Team.A, role = PlayerRole.Infielder, position = new Vector2(-2.5f,  2.5f) };
            yield return new PlayerSpawn { id = "A_In_2", team = Team.A, role = PlayerRole.Infielder, position = new Vector2(-4.5f,  0.0f) };
            yield return new PlayerSpawn { id = "A_In_3", team = Team.A, role = PlayerRole.Infielder, position = new Vector2(-2.5f, -2.5f) };

            // === Team A — outfielders surrounding Team B's right half ===
            yield return new PlayerSpawn { id = "A_Out_Back",   team = Team.A, role = PlayerRole.Outfielder, position = new Vector2( HalfWidth + BackOutfielderOffset, 0f) };
            yield return new PlayerSpawn { id = "A_Out_Top",    team = Team.A, role = PlayerRole.Outfielder, position = new Vector2( SideOutfielderX,  HalfHeight + SideOutfielderOffset) };
            yield return new PlayerSpawn { id = "A_Out_Bottom", team = Team.A, role = PlayerRole.Outfielder, position = new Vector2( SideOutfielderX, -HalfHeight - SideOutfielderOffset) };

            // === Team B — infielders on right half ===
            yield return new PlayerSpawn { id = "B_In_1", team = Team.B, role = PlayerRole.Infielder, position = new Vector2( 2.5f,  2.5f) };
            yield return new PlayerSpawn { id = "B_In_2", team = Team.B, role = PlayerRole.Infielder, position = new Vector2( 4.5f,  0.0f) };
            yield return new PlayerSpawn { id = "B_In_3", team = Team.B, role = PlayerRole.Infielder, position = new Vector2( 2.5f, -2.5f) };

            // === Team B — outfielders surrounding Team A's left half ===
            yield return new PlayerSpawn { id = "B_Out_Back",   team = Team.B, role = PlayerRole.Outfielder, position = new Vector2(-HalfWidth - BackOutfielderOffset, 0f) };
            yield return new PlayerSpawn { id = "B_Out_Top",    team = Team.B, role = PlayerRole.Outfielder, position = new Vector2(-SideOutfielderX,  HalfHeight + SideOutfielderOffset) };
            yield return new PlayerSpawn { id = "B_Out_Bottom", team = Team.B, role = PlayerRole.Outfielder, position = new Vector2(-SideOutfielderX, -HalfHeight - SideOutfielderOffset) };
        }

        /// <summary>
        /// True if the given world position is inside the given team's playable half.
        /// Useful for movement clamping later.
        /// </summary>
        public static bool IsInsideTeamHalf(Vector2 pos, Team team)
        {
            if (Mathf.Abs(pos.y) > HalfHeight) return false;
            return team == Team.A
                ? pos.x >= -HalfWidth && pos.x <= 0f
                : pos.x >= 0f && pos.x <=  HalfWidth;
        }
    }
}
