using UnityEngine;
using UnityEngine.SceneManagement;
using Sportland.Career;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Marks a spawned dodgeball player as a specific career athlete, so
    /// results and stats can flow back to the career layer.
    /// </summary>
    public class CareerAthleteTag : MonoBehaviour
    {
        public string athleteId;
        public string fullName;
    }

    /// <summary>
    /// Runs a league fixture inside the dodgeball scene. Added by CourtSetup
    /// when CareerMatchContext.Active: replaces the debug-rolled stats with
    /// the real rosters from the career layer, hands the controller to the
    /// player's chosen athlete, strips the debug tooling, and when the match
    /// ends writes the score into the context and returns to the hub.
    /// </summary>
    public class CareerMatchDirector : MonoBehaviour
    {
        [Tooltip("Seconds the FINAL scoreboard lingers before returning to the hub.")]
        public float finalScreenSeconds = 4f;

        private CourtSetup court;
        private DodgeballMatch match;
        private Ball ball;
        private bool finished;
        private float returnAt;
        private float nextControlCheck;
        private GUIStyle nameplateStyle;
        private Texture2D nameplateBg;

        private void Start()
        {
            court = GetComponent<CourtSetup>();
            match = GetComponent<DodgeballMatch>();
            ball = Object.FindFirstObjectByType<Ball>();

            StripDebugTooling();
            ApplyRosters();

            // NOTE: possession-follows-control is native to the module now —
            // DodgeballPlayerInput.OnBallAttached transfers to any teammate
            // who gains the ball. The director must NOT also transfer on that
            // event: two transfers in one invocation add duplicate input
            // components and the intermediate one's deferred OnDestroy
            // re-enables the CPU brain on the newly controlled player, which
            // fights the human's stick. The watchdog below only repairs
            // states the event flow can't produce on its own.
        }

        /// <summary>
        /// Control safety net, checked a few times a second: the human must
        /// always be driving an active player, and if our team carries the
        /// ball the carrier must be the one under control. Also clears any
        /// stray duplicate input components and makes sure the controlled
        /// player's CPU brain stays off.
        /// </summary>
        private void EnsureControl()
        {
            var current = DodgeballPlayerInput.Current;
            bool currentValid = current != null && current.gameObject.activeInHierarchy;

            // Duplicate-input repair: exactly one input component may exist.
            if (currentValid)
            {
                foreach (var input in Object.FindObjectsByType<DodgeballPlayerInput>(FindObjectsSortMode.None))
                    if (input != current) Destroy(input);

                var brain = current.GetComponent<DodgeballAI>();
                if (brain != null && brain.enabled) brain.enabled = false;
            }

            // Our carrier must be the controlled player (the arcade rule).
            var carrier = ball != null ? ball.Carrier : null;
            if (carrier != null && carrier.Spawn.team == Team.A && carrier.gameObject.activeInHierarchy)
            {
                if (!currentValid || current.gameObject != carrier.gameObject)
                    DodgeballPlayerInput.TransferControl(carrier.gameObject);
                return;
            }

            // No valid control at all: hand it to the nearest active teammate
            // to the ball so the human is never a spectator.
            if (!currentValid)
            {
                GameObject best = null;
                float bestDist = float.MaxValue;
                Vector2 ballPos = ball != null ? (Vector2)ball.transform.position : Vector2.zero;
                foreach (var go in court.SpawnedPlayers)
                {
                    if (go == null || !go.activeInHierarchy) continue;
                    var t = go.GetComponent<PlayerZoneTracker>();
                    if (t == null || t.Spawn.team != Team.A) continue;
                    float d = ((Vector2)go.transform.position - ballPos).sqrMagnitude;
                    if (d < bestDist) { bestDist = d; best = go; }
                }
                if (best != null) DodgeballPlayerInput.TransferControl(best);
            }
        }

        /// <summary>League games aren't the tuning lab: remove the debug kit.</summary>
        private void StripDebugTooling()
        {
            Strip<DodgeballCannon>();
            Strip<DodgeballDiagnosticsHUD>();
            Strip<DodgeballTuningPanel>();
            Strip<DodgeballMatchControls>();
            Strip<DodgeballAttackLab>();
        }

        private void Strip<T>() where T : Component
        {
            var c = GetComponent<T>();
            if (c != null) Destroy(c);
        }

        /// <summary>
        /// Map career athletes onto the spawned players: Team A = our club
        /// (lineup slots IN1-3/OUT1-3), Team B = the rival's best six in the
        /// same shape. Ratings translate through the shared 0-20 scale.
        /// </summary>
        private void ApplyRosters()
        {
            GameObject controlTarget = null;

            foreach (var go in court.SpawnedPlayers)
            {
                if (go == null) continue;
                var tracker = go.GetComponent<PlayerZoneTracker>();
                if (tracker == null) continue;

                var spawn = tracker.Spawn;
                int slot = SlotIndex(spawn);
                var list = spawn.team == Team.A
                    ? CareerMatchContext.ourStarters
                    : CareerMatchContext.theirStarters;
                if (slot < 0 || slot >= list.Count || list[slot] == null) continue;

                var athlete = list[slot];
                ApplyAthlete(go, athlete);

                if (spawn.team == Team.A && athlete.id == CareerMatchContext.controlledAthleteId)
                    controlTarget = go;
            }

            // Hand the sticks to the chosen athlete (CourtSetup attached input
            // to its default spawn id before we ran).
            if (controlTarget != null && controlTarget.GetComponent<DodgeballPlayerInput>() == null)
                DodgeballPlayerInput.TransferControl(controlTarget);
        }

        /// <summary>Spawn id (e.g. "A_In_2", "B_Out_3") → starter slot 0-5.</summary>
        private static int SlotIndex(PlayerSpawn spawn)
        {
            char last = spawn.id[spawn.id.Length - 1];
            int n = last - '1';
            if (n < 0 || n > 2) return -1;
            return spawn.role == PlayerRole.Infielder ? n : 3 + n;
        }

        /// <summary>
        /// Career record → match components. Career general ratings live on
        /// the shared 0-20 scale; dodgeball-specific ratings are derived from
        /// them until athletes carry per-sport ratings of their own.
        /// </summary>
        private static void ApplyAthlete(GameObject go, CareerAthlete athlete)
        {
            float speed = athlete.GetGeneral(GeneralRating.Speed).value;
            float agility = athlete.GetGeneral(GeneralRating.Agility).value;
            float endurance = athlete.GetGeneral(GeneralRating.Endurance).value;
            float toughness = athlete.GetGeneral(GeneralRating.Toughness).value;

            var dba = go.GetComponent<DodgeballAttributes>();
            if (dba != null)
            {
                dba.throwSpeedRating = Mathf.Clamp(toughness * 0.6f + speed * 0.4f, 0f, 20f);
                dba.throwAccuracyRating = Mathf.Clamp(agility, 0f, 20f);
                dba.catchTechniqueRating = Mathf.Clamp((agility + endurance) * 0.5f, 0f, 20f);
                dba.anticipation = Mathf.Clamp(agility * 5f, 0f, 100f);
            }

            var gen = go.GetComponent<GeneralAttributes>();
            if (gen != null)
            {
                gen.toughness = Mathf.Clamp(toughness * 5f, 0f, 100f);
                gen.endurance = Mathf.Clamp(endurance * 5f, 0f, 100f);
                gen.changeOfDirection = Mathf.Clamp(agility * 5f, 0f, 100f);
                gen.luck = 50f;
            }

            var tag = go.AddComponent<CareerAthleteTag>();
            tag.athleteId = athlete.id;
            tag.fullName = athlete.FullName;
        }

        private void Update()
        {
            if (match == null || !CareerMatchContext.Active) return;

            if (Time.time >= nextControlCheck)
            {
                nextControlCheck = Time.time + 0.25f;
                EnsureControl();
            }

            if (!finished && match.IsOver)
            {
                finished = true;
                returnAt = Time.time + finalScreenSeconds;
            }

            if (finished && Time.time >= returnAt)
            {
                CareerMatchContext.ourScore = match.Score(Team.A);
                CareerMatchContext.theirScore = match.Score(Team.B);
                CareerMatchContext.ResultReady = true;
                CareerMatchContext.Active = false;
                SceneManager.LoadScene("HubWorld");
            }
        }

        // ── Controlled-player nameplate ─────────────────────────────────
        // Bottom-centre readout of who the human is driving: name plus the
        // dodgeball rating grades. Grows into the full player card (face,
        // stats, ability ratings) when match presentation gets its pass.

        private void OnGUI()
        {
            var current = DodgeballPlayerInput.Current;
            if (current == null) return;

            var tag = current.GetComponent<CareerAthleteTag>();
            var dba = current.GetComponent<DodgeballAttributes>();
            string name = tag != null ? tag.fullName : current.gameObject.name;
            string ratings = dba != null
                ? $"   THR {dba.ThrowSpeedGrade} · ACC {dba.ThrowAccuracyGrade} · CAT {dba.CatchTechniqueGrade}"
                : "";

            EnsureNameplateStyle();
            const float w = 460f, h = 28f;
            var r = new Rect((Screen.width - w) * 0.5f, Screen.height - h - 10f, w, h);
            GUI.DrawTexture(r, nameplateBg);
            GUI.Label(r, $"▶ {name}{ratings}", nameplateStyle);
        }

        private void EnsureNameplateStyle()
        {
            if (nameplateStyle == null)
            {
                nameplateStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 16,
                    alignment = TextAnchor.MiddleCenter,
                };
                nameplateStyle.normal.textColor = Color.white;
            }
            nameplateStyle.font = DodgeballUI.Font;
            if (nameplateBg == null)
            {
                nameplateBg = new Texture2D(1, 1);
                nameplateBg.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.6f));
                nameplateBg.Apply();
                nameplateBg.hideFlags = HideFlags.HideAndDontSave;
            }
        }
    }
}
