using Sportland.Core.Athlete;
using Sportland.Core.GameManagement;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.WSA;

namespace Sportland.Core.GameManagement
{
    /// <summary>
    /// CoreGameManager - The persistent singleton that manages everything
    /// This stays loaded for the entire game session
    /// </summary>
    public class CoreGameManager : MonoBehaviour
    {
        // Singleton instance
        public static CoreGameManager Instance { get; private set; }

        [Header("Athlete Database")]
        public List<Athlete.Athlete> allAthletes = new List<Athlete.Athlete>();

        [Header("Sport Modules")]
        private Dictionary<SportType, ISportModule> loadedModules = new Dictionary<SportType, ISportModule>();
        private ISportModule currentSportModule;

        [Header("Game State")]
        public System.DateTime currentDate = new System.DateTime(2025, 9, 1); // Start of season
        public int currentDay = 1;

        [Header("Scene Names")]
        public string hubSceneName = "HubWorld";
        public string basketballSceneName = "Basketball";
        public string baseballSceneName = "Baseball";

        private void Awake()
        {
            // Singleton pattern
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            Debug.Log("CoreGameManager initialized");

            InitializeCore();
        }

        private void InitializeCore()
        {
            // Load all athlete assets from Resources folder
            LoadAthletes();

            Debug.Log($"Loaded {allAthletes.Count} athletes");
        }

        private void LoadAthletes()
        {
            // Load all Athlete ScriptableObjects from Resources/Athletes folder
            Athlete.Athlete[] loadedAthletes = Resources.LoadAll<Athlete.Athlete>("Athletes");
            allAthletes.AddRange(loadedAthletes);

            // If no athletes exist, create a test athlete
            if (allAthletes.Count == 0)
            {
                Debug.LogWarning("No athletes found! Create some in Resources/Athletes folder");
            }
        }

        // ===== SPORT LOADING SYSTEM =====

        /// <summary>
        /// Load and start a sport game
        /// </summary>
        public async void LoadSport(SportType sportType, GameContext context)
        {
            Debug.Log($"Loading sport: {sportType}");

            // Unload current sport if different
            if (currentSportModule != null && currentSportModule.GetSportType() != sportType)
            {
                await UnloadCurrentSport();
            }

            // Load sport module if not already loaded
            if (!loadedModules.ContainsKey(sportType))
            {
                ISportModule module = await LoadSportModule(sportType);
                if (module != null)
                {
                    loadedModules[sportType] = module;
                    module.Initialize(allAthletes);
                }
                else
                {
                    Debug.LogError($"Failed to load sport module: {sportType}");
                    return;
                }
            }

            currentSportModule = loadedModules[sportType];

            // Load sport scene
            await LoadSportScene(sportType);

            // Start the game
            currentSportModule.StartGame(context);
        }

        private async Task<ISportModule> LoadSportModule(SportType sportType)
        {
            // For now, we'll instantiate sport modules from prefabs in Resources
            // Later this can be more sophisticated

            string prefabPath = $"SportModules/{sportType}Module";
            GameObject modulePrefab = Resources.Load<GameObject>(prefabPath);

            if (modulePrefab == null)
            {
                Debug.LogError($"Sport module prefab not found: {prefabPath}");
                return null;
            }

            GameObject moduleObj = Instantiate(modulePrefab);
            ISportModule module = moduleObj.GetComponent<ISportModule>();

            if (module == null)
            {
                Debug.LogError($"GameObject does not implement ISportModule: {prefabPath}");
                Destroy(moduleObj);
                return null;
            }

            // Make it persistent (optional, depends on design)
            DontDestroyOnLoad(moduleObj);

            return module;
        }

        private async Task LoadSportScene(SportType sportType)
        {
            string sceneName = GetSceneNameForSport(sportType);

            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogError($"No scene configured for sport: {sportType}");
                return;
            }

            // Load scene additively
            await SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);

            Debug.Log($"Loaded scene: {sceneName}");
        }

        private string GetSceneNameForSport(SportType sportType)
        {
            switch (sportType)
            {
                case SportType.Basketball:
                    return basketballSceneName;
                case SportType.Baseball:
                    return baseballSceneName;
                default:
                    return null;
            }
        }

        private async Task UnloadCurrentSport()
        {
            if (currentSportModule == null) return;

            Debug.Log($"Unloading sport: {currentSportModule.GetSportType()}");

            // Cleanup sport module
            currentSportModule.Cleanup();

            // Unload scene
            string sceneName = GetSceneNameForSport(currentSportModule.GetSportType());
            if (!string.IsNullOrEmpty(sceneName))
            {
                await SceneManager.UnloadSceneAsync(sceneName);
            }

            currentSportModule = null;

            // Force garbage collection
            await Resources.UnloadUnusedAssets();
            System.GC.Collect();
        }

        // ===== RETURN TO HUB =====

        /// <summary>
        /// Called when sport game ends, return to hub
        /// </summary>
        public async void ReturnToHub()
        {
            if (currentSportModule != null)
            {
                // Get results from the game
                GameResult results = currentSportModule.GetGameResults();

                // Process results
                ProcessGameResults(results);

                // Unload sport
                await UnloadCurrentSport();
            }

            // Load hub scene
            SceneManager.LoadScene(hubSceneName);
        }

        private void ProcessGameResults(GameResult results)
        {
            if (results == null) return;

            Debug.Log($"Processing game results: {results.homeScore} - {results.awayScore}");

            // Update athlete stats
            foreach (var playerStats in results.playerStats)
            {
                Athlete.Athlete athlete = GetAthleteByID(playerStats.athleteID);
                if (athlete != null)
                {
                    UpdateAthleteStats(athlete, results.sport, playerStats);
                }
            }

            // Process flag triggers from significant events
            foreach (var gameEvent in results.significantEvents)
            {
                ProcessGameEvent(gameEvent);
            }

            // Update fatigue based on game duration
            UpdateFatigueFromGame(results);

            // Advance day
            AdvanceDay();
        }

        private void UpdateAthleteStats(Athlete.Athlete athlete, SportType sport, PlayerGameStats stats)
        {
            SportStats sportStats = athlete.GetSportStats(sport);
            if (sportStats == null)
            {
                athlete.InitializeSportStats(sport);
                sportStats = athlete.GetSportStats(sport);
            }

            sportStats.gamesPlayed++;
            sportStats.points += stats.points;

            Debug.Log($"Updated stats for {athlete.GetFullName()}: {stats.points} points");
        }

        private void ProcessGameEvent(GameEvent gameEvent)
        {
            // This is where flag triggers will go
            // For now, just log it
            Debug.Log($"Game event: {gameEvent.type} - {gameEvent.description}");

            // Example: If clutch shot, maybe add "Clutch Gene" flag
            if (gameEvent.type == GameEvent.EventType.ClutchShot)
            {
                Athlete.Athlete athlete = GetAthleteByID(gameEvent.athleteID);
                if (athlete != null)
                {
                    // Simple flag acquisition for now
                    athlete.AddFlag("ClutchGene");
                }
            }
        }

        private void UpdateFatigueFromGame(GameResult results)
        {
            // Add fatigue based on game duration and minutes played
            foreach (var playerStats in results.playerStats)
            {
                Athlete.Athlete athlete = GetAthleteByID(playerStats.athleteID);
                if (athlete != null)
                {
                    float fatigueGain = playerStats.minutesPlayed * 0.5f; // Adjust this formula
                    athlete.fatigue = Mathf.Clamp(athlete.fatigue + fatigueGain, 0, 100);
                }
            }
        }

        // ===== TIME PROGRESSION =====

        public void AdvanceDay()
        {
            currentDay++;
            currentDate = currentDate.AddDays(1);

            // Process daily recovery
            foreach (var athlete in allAthletes)
            {
                // Recover some fatigue each day
                athlete.fatigue = Mathf.Max(0, athlete.fatigue - 10f);
            }

            Debug.Log($"Advanced to Day {currentDay}: {currentDate.ToShortDateString()}");
        }

        // ===== HELPER METHODS =====

        public Athlete.Athlete GetAthleteByID(string athleteID)
        {
            return allAthletes.Find(a => a.athleteID == athleteID);
        }

        public List<Athlete.Athlete> GetAllAthletes()
        {
            return new List<Athlete.Athlete>(allAthletes);
        }
    }
}
