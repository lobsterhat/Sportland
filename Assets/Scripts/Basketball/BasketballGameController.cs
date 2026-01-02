using Sportland.Core;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Sportland.Basketball
{
    public class BasketballGameController : MonoBehaviour
    {
        [Header("UI")]
        public TextMeshProUGUI scoreText;
        public Button returnToHubButton;

        private int score = 0;

        private void Start()
        {
            UpdateScoreDisplay();

            if (returnToHubButton != null)
            {
                returnToHubButton.onClick.AddListener(ReturnToHub);
            }
        }

        public void AddScore(int points)
        {
            score += points;
            UpdateScoreDisplay();
        }

        private void UpdateScoreDisplay()
        {
            if (scoreText != null)
            {
                scoreText.text = $"Score: {score}";
            }
        }

        //private void ReturnToHub()
        //{
        //    Debug.Log("Returning to hub...");
        //    CoreGameManager.Instance.ReturnToHub();
        //}

        private void ReturnToHub()
        {
            Debug.Log("Returning to hub...");

            // Check if CoreGameManager exists
            if (CoreGameManager.Instance != null)
            {
                CoreGameManager.Instance.ReturnToHub();
            }
            else
            {
                Debug.LogError("CoreGameManager.Instance is null! Loading hub scene directly as fallback.");
                // Fallback: Load hub scene directly
                SceneManager.LoadScene("HubWorld");
            }
        }
    }
}