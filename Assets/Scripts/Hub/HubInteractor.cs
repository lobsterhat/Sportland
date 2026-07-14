using UnityEngine;
using UnityEngine.InputSystem;
using Sportland.Career;

namespace Sportland.Hub
{
    /// <summary>
    /// Lives on the hub player. Finds the nearest building in range, drives
    /// the prompt, and executes interactions against CareerManager — this is
    /// where the slice's action catalog lives (design/hub_actions.md §4).
    ///
    /// Controls (keyboard / PS4 pad): confirm = E / Cross, cancel = Esc /
    /// Circle, menu = R / Triangle. Pad buttons are read positionally
    /// (buttonSouth/East/North), so Xbox pads map sensibly too.
    /// </summary>
    public class HubInteractor : MonoBehaviour
    {
        private HubBuilding[] buildings;
        private HubHud hud;
        private HubPlayerController movement;
        private int creatorIndex;

        public void Init(HubBuilding[] buildings, HubHud hud)
        {
            this.buildings = buildings;
            this.hud = hud;
            movement = GetComponent<HubPlayerController>();
        }

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

        private void Update()
        {
            if (buildings == null || hud == null) return;

            var career = CareerManager.Instance;
            if (career == null) return;

            // Walking pauses while any panel is up — panels own the inputs.
            bool anyPanel = hud.RosterVisible || hud.CreatorVisible;
            if (movement != null) movement.enabled = !anyPanel;

            // Character creator is the most modal thing there is.
            if (hud.CreatorVisible)
            {
                hud.SetPrompt("");
                UpdateCreator(career);
                return;
            }

            // Menu (Triangle / R) toggles the roster anywhere; cancel (Circle /
            // Esc) closes it. While it's open it's modal: no world interaction.
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

        private static string PromptFor(HubBuilding building, CareerManager career)
        {
            if (building.type == HubBuildingType.Office && !career.PlayerCreated)
                return "[E] Office — create your character";
            return building.PromptText;
        }

        private void UpdateCreator(CareerManager career)
        {
            int count = Archetypes.All.Length;

            if (NavUpPressed())
            {
                creatorIndex = (creatorIndex - 1 + count) % count;
                hud.ShowCreator(creatorIndex);
            }
            else if (NavDownPressed())
            {
                creatorIndex = (creatorIndex + 1) % count;
                hud.ShowCreator(creatorIndex);
            }
            else if (ConfirmPressed())
            {
                var chosen = Archetypes.All[creatorIndex];
                career.ApplyArchetype(chosen);
                hud.CloseCreator();
                hud.Toast($"Skip: \"A {chosen.displayName}! Good choice. {chosen.actionsPerDay} actions a day, coach — spend them well.\"", 6f);
            }
            else if (CancelPressed())
            {
                hud.CloseCreator();
                hud.Toast("Skip: \"No rush — come back to the Office whenever you're ready.\"");
            }
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

        private void Interact(HubBuilding building, CareerManager career)
        {
            switch (building.type)
            {
                case HubBuildingType.Home:
                    career.EndDay();
                    hud.Toast($"You sleep. Day {career.day} — everyone recovered overnight.");
                    return;

                case HubBuildingType.Arena:
                    hud.Toast("No games scheduled yet — league play arrives with the calendar system.");
                    return;
            }

            // Character creation lives at the Office and is always free.
            if (building.type == HubBuildingType.Office && !career.PlayerCreated)
            {
                creatorIndex = 0;
                hud.ShowCreator(creatorIndex);
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
                case HubBuildingType.Office:
                    hud.Toast("Front-office work done. (Scouting and league business plug in here.)");
                    break;

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
