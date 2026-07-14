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

        public void Init(HubBuilding[] buildings, HubHud hud)
        {
            this.buildings = buildings;
            this.hud = hud;
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

        private void Update()
        {
            if (buildings == null || hud == null) return;

            var career = CareerManager.Instance;
            if (career == null) return;

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
            hud.SetPrompt(nearest != null ? nearest.PromptText : "");

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
