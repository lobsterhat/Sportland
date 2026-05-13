using System.Collections.Generic;
using UnityEngine;

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

        // ---- Outfielder offsets from court boundary ----
        // How far behind the baseline outfielders stand:
        public const float BackOutfielderOffset = 1.5f;   // beyond ±9 on X
        // How far outside the sideline the side-outfielders stand:
        public const float SideOutfielderOffset = 1.5f;   // beyond ±4.5 on Y
        // How far in from the corner along the sideline:
        // (centered on each opposing half = ±4.5 from origin on X)
        public const float SideOutfielderX = 4.5f;

        [Header("Prefabs")]
        [SerializeField] private GameObject playerPrefab;
        [SerializeField] private GameObject courtPrefab;       // optional: visual court sprite
        [SerializeField] private GameObject centerLinePrefab;  // optional: visual divider

        [Header("Runtime")]
        [SerializeField] private List<GameObject> spawnedPlayers = new List<GameObject>();

        private void Awake()
        {
            BuildCourt();
            SpawnAllPlayers();
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
            foreach (var spawn in GetAllSpawns())
            {
                SpawnPlayer(spawn);
            }
        }

        private void SpawnPlayer(PlayerSpawn spawn)
        {
            if (playerPrefab == null)
            {
                Debug.LogWarning($"[CourtSetup] playerPrefab not assigned; skipping spawn for {spawn.id}");
                return;
            }

            var go = Instantiate(
                playerPrefab,
                new Vector3(spawn.position.x, spawn.position.y, 0f),
                Quaternion.identity,
                transform
            );
            go.name = spawn.id;

            var tracker = go.GetComponent<PlayerZoneTracker>();
            if (tracker == null) tracker = go.AddComponent<PlayerZoneTracker>();
            tracker.Initialize(spawn);

            spawnedPlayers.Add(go);
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
