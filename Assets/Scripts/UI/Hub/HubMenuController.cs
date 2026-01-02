using UnityEngine;
using UnityEngine.UI;
using Sportland.Core.GameManagement;
using Sportland.Core.Athlete;

namespace Sportland.UI.Hub
{
    public class HubMenuController : MonoBehaviour
    {
        public Button playBasketballButton;

        private void Start()
        {
            // Hook up button click
            if (playBasketballButton != null)
            {
                playBasketballButton.onClick.AddListener(OnPlayBasketballClicked);
            }
        }

        private void OnPlayBasketballClicked()
        {
            Debug.Log("Play Basketball clicked!");

            // Create a simple game context
            GameContext context = new GameContext
            {
                gameDate = CoreGameManager.Instance.currentDate,
                homeRoster = CoreGameManager.Instance.GetAllAthletes(),
                awayRoster = CoreGameManager.Instance.GetAllAthletes(),
                isPlayoffGame = false,
                venueName = "Test Arena"
            };

            // Load basketball!
            CoreGameManager.Instance.LoadSport(SportType.Basketball, context);
        }
    }
}