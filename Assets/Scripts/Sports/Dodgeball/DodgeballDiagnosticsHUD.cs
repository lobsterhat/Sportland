using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Lightweight on-screen readout for tuning movement and ball physics.
    /// Shows the human-controlled player's current speed, walk/run state,
    /// IsAccelerating, and the ball's current speed.
    ///
    /// Uses IMGUI so it doesn't need a Canvas / TMP / EventSystem setup —
    /// drop the component on any GameObject and it draws to the top-left
    /// of the game view.
    /// </summary>
    public class DodgeballDiagnosticsHUD : MonoBehaviour
    {
        [SerializeField] private bool showHud = true;
        [SerializeField] private Vector2 anchor = new Vector2(12f, 12f);
        [SerializeField] private int fontSize = 16;
        [SerializeField] private Color textColor = Color.white;
        [Tooltip("Translucent background drawn behind the text for legibility.")]
        [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.55f);

        private GUIStyle textStyle;
        private Texture2D bgTexture;
        private Ball cachedBall;

        private void OnGUI()
        {
            if (!showHud) return;

            EnsureStyles();

            string[] lines = BuildLines();

            float lineHeight = fontSize + 4f;
            float padding = 8f;
            float width = 280f;
            float height = lines.Length * lineHeight + padding * 2f;
            Rect bg = new Rect(anchor.x, anchor.y, width, height);

            // Background panel.
            GUI.DrawTexture(bg, bgTexture);

            for (int i = 0; i < lines.Length; i++)
            {
                Rect r = new Rect(anchor.x + padding,
                                  anchor.y + padding + i * lineHeight,
                                  width - padding * 2f,
                                  lineHeight);
                GUI.Label(r, lines[i], textStyle);
            }
        }

        private string[] BuildLines()
        {
            EnsureBall();
            float ballSpeed  = GetBallSpeed();
            float ballHeight = cachedBall != null ? cachedBall.Height : 0f;

            var input = DodgeballPlayerInput.Current;
            if (input == null)
            {
                return new[]
                {
                    "Player: (no controller)",
                    $"Ball speed:   {ballSpeed,5:F2} u/s",
                    $"Ball height:  {ballHeight,5:F2} u",
                };
            }

            var movement = input.GetComponent<PlayerMovement>();
            var rb       = input.GetComponent<Rigidbody2D>();

            float playerSpeed = rb != null ? rb.linearVelocity.magnitude : 0f;
            string state      = movement != null && movement.IsRunning ? "RUN" : "walk";
            string accelStr   = movement != null && movement.IsAccelerating ? "yes" : "no";

            return new[]
            {
                $"Player speed: {playerSpeed,5:F2} u/s",
                $"State:        {state}",
                $"Accelerating: {accelStr}",
                $"Ball speed:   {ballSpeed,5:F2} u/s",
                $"Ball height:  {ballHeight,5:F2} u",
            };
        }

        private void EnsureBall()
        {
            if (cachedBall == null) cachedBall = FindFirstObjectByType<Ball>();
        }

        private float GetBallSpeed()
        {
            if (cachedBall == null) return 0f;
            var rb = cachedBall.GetComponent<Rigidbody2D>();
            return rb != null ? rb.linearVelocity.magnitude : 0f;
        }

        private void EnsureStyles()
        {
            if (textStyle == null || textStyle.fontSize != fontSize)
            {
                textStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = fontSize,
                    richText = false,
                };
                textStyle.normal.textColor = textColor;
            }
            if (bgTexture == null)
            {
                bgTexture = new Texture2D(1, 1);
                bgTexture.SetPixel(0, 0, backgroundColor);
                bgTexture.Apply();
                bgTexture.hideFlags = HideFlags.HideAndDontSave;
            }
        }

        private void OnDestroy()
        {
            if (bgTexture != null) Destroy(bgTexture);
        }
    }
}
