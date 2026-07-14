using UnityEngine;
using UnityEngine.InputSystem;
using Sportland.Career;

namespace Sportland.Hub
{
    /// <summary>
    /// Lives on the hub player. Finds the nearest building in range, drives
    /// the prompt, and runs the hub's screens (character creator, league
    /// sign-up, calendar, roster/pool/lineup) — a small state machine over
    /// HubHud's generic panel, with text built by HubScreens.
    ///
    /// Controls (keyboard / PS4 pad): confirm = E / Cross, cancel = Esc /
    /// Circle, menu = R / Triangle, auto-fill = F / Square. Pad buttons are
    /// read positionally, so Xbox pads map sensibly too. F9 resets the career.
    /// </summary>
    public class HubInteractor : MonoBehaviour
    {
        private enum Screen { None, Creator, LeagueSignup, Calendar, Roster }

        private HubBuilding[] buildings;
        private HubHud hud;
        private HubPlayerController movement;

        private Screen screen = Screen.None;
        private int creatorIndex;
        private int calendarPage;
        private RosterTab rosterTab;
        private int poolIndex;
        private int lineupSlot;
        private bool assignMode;
        private int pickIndex;

        public void Init(HubBuilding[] buildings, HubHud hud)
        {
            this.buildings = buildings;
            this.hud = hud;
            movement = GetComponent<HubPlayerController>();
        }

        // ── Input helpers ───────────────────────────────────────────────

        private static bool ConfirmPressed()
        {
            var pad = Gamepad.current;
            return Input.GetKeyDown(KeyCode.E) || (pad != null && pad.buttonSouth.wasPressedThisFrame);
        }

        private static bool CancelPressed()
        {
            var pad = Gamepad.current;
            return Input.GetKeyDown(KeyCode.Escape) || (pad != null && pad.buttonEast.wasPressedThisFrame);
        }

        private static bool MenuPressed()
        {
            var pad = Gamepad.current;
            return Input.GetKeyDown(KeyCode.R) || (pad != null && pad.buttonNorth.wasPressedThisFrame);
        }

        private static bool AutoFillPressed()
        {
            var pad = Gamepad.current;
            return Input.GetKeyDown(KeyCode.F) || (pad != null && pad.buttonWest.wasPressedThisFrame);
        }

        private static bool NavUpPressed()
        {
            var pad = Gamepad.current;
            return Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)
                || (pad != null && pad.dpad.up.wasPressedThisFrame);
        }

        private static bool NavDownPressed()
        {
            var pad = Gamepad.current;
            return Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)
                || (pad != null && pad.dpad.down.wasPressedThisFrame);
        }

        private static bool NavLeftPressed()
        {
            var pad = Gamepad.current;
            return Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)
                || (pad != null && pad.dpad.left.wasPressedThisFrame);
        }

        private static bool NavRightPressed()
        {
            var pad = Gamepad.current;
            return Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)
                || (pad != null && pad.dpad.right.wasPressedThisFrame);
        }

        // ── Frame loop ──────────────────────────────────────────────────

        private void Update()
        {
            if (buildings == null || hud == null) return;

            var career = CareerManager.Instance;
            if (career == null) return;

            bool anyPanel = screen != Screen.None || hud.RosterVisible;
            if (movement != null) movement.enabled = !anyPanel;

            if (screen != Screen.None)
            {
                hud.SetPrompt("");
                HandleScreen(career);
                return;
            }

            // Debug/testing: start over from nothing.
            if (Input.GetKeyDown(KeyCode.F9))
            {
                career.ResetCareer();
                hud.Toast("Fresh career started — the old save is gone.");
                return;
            }

            // Quick roster (Triangle / R) toggles anywhere in the world.
            if (MenuPressed())
            {
                hud.ToggleRoster(career.club);
                return;
            }

            if (hud.RosterVisible)
            {
                hud.SetPrompt("");
                if (CancelPressed())
                    hud.CloseRoster();
                return;
            }

            HubBuilding nearest = FindNearest();
            hud.SetPrompt(nearest != null ? PromptFor(nearest, career) : "");

            if (nearest != null && ConfirmPressed())
                Interact(nearest, career);
        }

        private HubBuilding FindNearest()
        {
            HubBuilding best = null;
            float bestDist = float.MaxValue;
            foreach (var b in buildings)
            {
                float d = Vector2.Distance(transform.position, b.transform.position);
                if (d <= b.interactionRadius && d < bestDist)
                {
                    best = b;
                    bestDist = d;
                }
            }
            return best;
        }

        private static string PromptFor(HubBuilding building, CareerManager career)
        {
            switch (building.type)
            {
                case HubBuildingType.Office:
                    if (!career.PlayerCreated) return "[E] Office — create your character";
                    if (career.league == null) return "[E] Office — league sign-up";
                    return "[E] Office — roster, recruiting & lineup";

                case HubBuildingType.Arena:
                    return career.league == null
                        ? "[E] Arena — no league yet (sign up at the Office)"
                        : "[E] Arena — schedule & calendar";

                default:
                    return building.PromptText;
            }
        }

        // ── Screens ─────────────────────────────────────────────────────

        private void OpenScreen(Screen s, CareerManager career)
        {
            screen = s;
            RedrawScreen(career);
        }

        private void CloseScreen()
        {
            screen = Screen.None;
            assignMode = false;
            hud.CloseScreen();
        }

        private void RedrawScreen(CareerManager career)
        {
            switch (screen)
            {
                case Screen.Creator:
                    hud.ShowScreen(HubScreens.Creator(creatorIndex));
                    break;
                case Screen.LeagueSignup:
                    hud.ShowScreen(HubScreens.LeagueSignup(career));
                    break;
                case Screen.Calendar:
                    hud.ShowScreen(HubScreens.Calendar(career, calendarPage));
                    break;
                case Screen.Roster:
                    hud.ShowScreen(rosterTab == RosterTab.Lineup
                        ? HubScreens.Lineup(career, lineupSlot, assignMode, pickIndex)
                        : HubScreens.Roster(career, rosterTab, poolIndex));
                    break;
            }
        }

        private void HandleScreen(CareerManager career)
        {
            switch (screen)
            {
                case Screen.Creator:      HandleCreator(career); break;
                case Screen.LeagueSignup: HandleLeagueSignup(career); break;
                case Screen.Calendar:     HandleCalendar(career); break;
                case Screen.Roster:       HandleRoster(career); break;
            }
        }

        private void HandleCreator(CareerManager career)
        {
            int count = Archetypes.All.Length;

            if (NavUpPressed())
            {
                creatorIndex = (creatorIndex - 1 + count) % count;
                RedrawScreen(career);
            }
            else if (NavDownPressed())
            {
                creatorIndex = (creatorIndex + 1) % count;
                RedrawScreen(career);
            }
            else if (ConfirmPressed())
            {
                var chosen = Archetypes.All[creatorIndex];
                career.ApplyArchetype(chosen);
                CloseScreen();
                hud.Toast($"Skip: \"A {chosen.displayName}! Good choice. {chosen.actionsPerDay} actions a day, coach — spend them well.\"", 6f);
            }
            else if (CancelPressed())
            {
                CloseScreen();
                hud.Toast("Skip: \"No rush — come back to the Office whenever you're ready.\"");
            }
        }

        private void HandleLeagueSignup(CareerManager career)
        {
            if (ConfirmPressed())
            {
                career.JoinDodgeballLeague();
                hud.Toast("Skip: \"We're in the Parks League! Now let's fill this roster — I took the liberty of opening the pool.\"", 6f);
                rosterTab = RosterTab.Pool;
                poolIndex = 0;
                OpenScreen(Screen.Roster, career);
            }
            else if (CancelPressed())
            {
                CloseScreen();
            }
        }

        private void HandleCalendar(CareerManager career)
        {
            int months = HubScreens.CalendarMonthCount(career.league);

            if (NavLeftPressed() && calendarPage > 0)
            {
                calendarPage--;
                RedrawScreen(career);
            }
            else if (NavRightPressed() && calendarPage < months - 1)
            {
                calendarPage++;
                RedrawScreen(career);
            }
            else if (CancelPressed())
            {
                CloseScreen();
            }
        }

        private void HandleRoster(CareerManager career)
        {
            if (assignMode)
            {
                HandleAssignPicker(career);
                return;
            }

            if (MenuPressed())
            {
                rosterTab = (RosterTab)(((int)rosterTab + 1) % 3);
                poolIndex = 0;
                lineupSlot = 0;
                RedrawScreen(career);
                return;
            }

            switch (rosterTab)
            {
                case RosterTab.Pool:
                    if (career.freeAgents.Count > 0)
                    {
                        if (NavUpPressed())
                        {
                            poolIndex = Mathf.Max(0, poolIndex - 1);
                            RedrawScreen(career);
                            return;
                        }
                        if (NavDownPressed())
                        {
                            poolIndex = Mathf.Min(career.freeAgents.Count - 1, poolIndex + 1);
                            RedrawScreen(career);
                            return;
                        }
                        if (ConfirmPressed())
                        {
                            var target = career.freeAgents[poolIndex];
                            career.TryRecruit(target, out string message);
                            hud.Toast(message);
                            poolIndex = Mathf.Clamp(poolIndex, 0, Mathf.Max(0, career.freeAgents.Count - 1));
                            RedrawScreen(career);
                            return;
                        }
                    }
                    break;

                case RosterTab.Lineup:
                    if (NavUpPressed())
                    {
                        lineupSlot = Mathf.Max(0, lineupSlot - 1);
                        RedrawScreen(career);
                        return;
                    }
                    if (NavDownPressed())
                    {
                        lineupSlot = Mathf.Min(9, lineupSlot + 1);
                        RedrawScreen(career);
                        return;
                    }
                    if (ConfirmPressed())
                    {
                        assignMode = true;
                        pickIndex = 0;
                        RedrawScreen(career);
                        return;
                    }
                    if (AutoFillPressed())
                    {
                        career.AutoFillLineup();
                        hud.Toast("Skip: \"Penciled in the best we've got. Shuffle them however you like.\"");
                        RedrawScreen(career);
                        return;
                    }
                    break;
            }

            if (CancelPressed())
                CloseScreen();
        }

        private void HandleAssignPicker(CareerManager career)
        {
            int optionCount = career.club.pool.Count + 1; // 0 = clear slot

            if (NavUpPressed())
            {
                pickIndex = Mathf.Max(0, pickIndex - 1);
                RedrawScreen(career);
            }
            else if (NavDownPressed())
            {
                pickIndex = Mathf.Min(optionCount - 1, pickIndex + 1);
                RedrawScreen(career);
            }
            else if (ConfirmPressed())
            {
                bool starter = lineupSlot < 6;
                int slot = starter ? lineupSlot : lineupSlot - 6;
                string id = pickIndex == 0 ? "" : career.club.pool[pickIndex - 1].id;
                career.AssignLineupSlot(starter, slot, id);
                assignMode = false;
                RedrawScreen(career);
            }
            else if (CancelPressed())
            {
                assignMode = false;
                RedrawScreen(career);
            }
        }

        // ── World interactions ──────────────────────────────────────────

        private void Interact(HubBuilding building, CareerManager career)
        {
            switch (building.type)
            {
                case HubBuildingType.Office:
                    if (!career.PlayerCreated)
                    {
                        creatorIndex = 0;
                        OpenScreen(Screen.Creator, career);
                    }
                    else if (career.league == null)
                    {
                        OpenScreen(Screen.LeagueSignup, career);
                    }
                    else
                    {
                        rosterTab = RosterTab.Squad;
                        poolIndex = 0;
                        lineupSlot = 0;
                        OpenScreen(Screen.Roster, career);
                    }
                    return;

                case HubBuildingType.Arena:
                    if (career.league == null)
                    {
                        hud.Toast("Skip: \"Nothing on the books yet — the Office handles league sign-ups.\"");
                    }
                    else
                    {
                        calendarPage = 0;
                        OpenScreen(Screen.Calendar, career);
                    }
                    return;

                case HubBuildingType.Home:
                    career.EndDay();
                    hud.Toast($"You sleep. Day {career.day} — everyone recovered overnight.");
                    return;
            }

            // Everything below costs an action.
            if (!career.TrySpendAction())
            {
                hud.Toast("You're out of actions for today. Head Home to end the day.");
                return;
            }

            switch (building.type)
            {
                case HubBuildingType.Practice:
                    career.RunPractice();
                    hud.Toast("Practice run — the squad worked hard and picked up some fatigue.");
                    break;

                case HubBuildingType.Hospital:
                    var treated = career.TreatMostTired();
                    hud.Toast(treated != null
                        ? $"Treatment: {treated.FullName} feels much fresher."
                        : "The treatment room sits empty.");
                    break;

                case HubBuildingType.Cafe:
                    string learned = career.RevealSomethingOverDinner();
                    hud.Toast(learned != null
                        ? $"Over dinner you learn — {learned}"
                        : "A pleasant dinner. You know this group well already.");
                    break;
            }
        }
    }
}
