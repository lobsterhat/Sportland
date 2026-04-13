using UnityEngine;
using UnityEngine.UI;

namespace Sportland.Rendering
{
    /// <summary>
    /// Implements a pixel-art render pipeline via a fixed-resolution RenderTexture.
    ///
    /// How it works:
    ///   1. The game camera renders into a small RenderTexture (default 480×270)
    ///      using Point (nearest-neighbour) filtering — no blurring between pixels.
    ///   2. A display canvas stretches that buffer to fill the screen.
    ///   3. An AspectRatioFitter adds letterbox/pillarbox bars when the screen
    ///      aspect doesn't match 16:9, keeping pixels square at all resolutions.
    ///
    /// Result: the camera can zoom freely (orthographicSize changes) without ever
    /// producing the shimmer/unevenness that plagues pixel art at non-integer scales,
    /// because all art is sampled through the same fixed-resolution buffer.
    ///
    /// Setup: attach to Main Camera alongside CameraFollow. No other scene config needed.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class PixelArtRenderer : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        //  CONFIGURATION
        // ──────────────────────────────────────────────

        [Header("=== REFERENCE RESOLUTION ===")]
        [Tooltip("Width of the internal render buffer in pixels. " +
                 "480 = 4× integer scale at 1080p. Determines sprite pixel density.")]
        [SerializeField] private int referenceWidth  = 480;

        [Tooltip("Height of the internal render buffer in pixels.")]
        [SerializeField] private int referenceHeight = 270;

        [Header("=== DISPLAY ===")]
        [Tooltip("Letterbox/pillarbox bar colour when the screen aspect doesn't match.")]
        [SerializeField] private Color letterboxColor = Color.black;

        [Tooltip("Pixels to reserve at the bottom of the screen for the PlayerInfoBar. " +
                 "The game view is constrained to the remaining space above this.")]
        [SerializeField] private float hudBarHeight = 200f;

        // ──────────────────────────────────────────────
        //  RUNTIME
        // ──────────────────────────────────────────────

        private new Camera cam;
        private RenderTexture renderTexture;

        /// <summary>The RenderTexture the game camera draws into.</summary>
        public RenderTexture RenderTexture => renderTexture;

        /// <summary>Pixels reserved at the bottom for the PlayerInfoBar HUD.</summary>
        public float HudBarHeight => hudBarHeight;

        /// <summary>Reference width set in the Inspector.</summary>
        public int ReferenceWidth  => referenceWidth;

        /// <summary>Reference height set in the Inspector.</summary>
        public int ReferenceHeight => referenceHeight;

        // ──────────────────────────────────────────────
        //  LIFECYCLE
        // ──────────────────────────────────────────────

        private void Awake()
        {
            cam = GetComponent<Camera>();
            CreateRenderTexture();
            BuildDisplayCanvas();
        }

        private void OnDestroy()
        {
            if (cam != null)
                cam.targetTexture = null;

            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
            }
        }

        // ──────────────────────────────────────────────
        //  RENDER TEXTURE
        // ──────────────────────────────────────────────

        private void CreateRenderTexture()
        {
            renderTexture = new RenderTexture(referenceWidth, referenceHeight, 24,
                                              RenderTextureFormat.Default);

            // Point filtering = nearest-neighbour — no blending between pixels.
            // This is the single most important setting for pixel art.
            renderTexture.filterMode  = FilterMode.Point;
            renderTexture.antiAliasing = 1;   // no MSAA; would blur pixel edges
            renderTexture.wrapMode    = TextureWrapMode.Clamp;
            renderTexture.name        = "PixelArtBuffer";
            renderTexture.Create();

            cam.targetTexture = renderTexture;

            Debug.Log($"[PixelArtRenderer] Buffer: {referenceWidth}×{referenceHeight} " +
                      $"({(float)referenceWidth / referenceHeight:F2} aspect). " +
                      $"Scale at {Screen.width}×{Screen.height}: " +
                      $"{(float)Screen.width / referenceWidth:F2}×");
        }

        // ──────────────────────────────────────────────
        //  DISPLAY CANVAS
        // ──────────────────────────────────────────────

        private void BuildDisplayCanvas()
        {
            // ── Root canvas ──────────────────────────────
            var canvasGO = new GameObject("PixelArtDisplay");
            var canvas   = canvasGO.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 0; // game HUD canvases sit above this (sortingOrder > 0)

            canvasGO.AddComponent<CanvasScaler>();     // default pixel-for-pixel scaling
            canvasGO.AddComponent<GraphicRaycaster>();

            // ── Letterbox background (full canvas including bar region) ──
            var bgGO    = new GameObject("Letterbox");
            bgGO.transform.SetParent(canvasGO.transform, false);
            var bgImage = bgGO.AddComponent<Image>();
            bgImage.color         = letterboxColor;
            bgImage.raycastTarget = false;
            StretchToFill(bgGO.GetComponent<RectTransform>());

            // ── Game area — full canvas minus the HUD bar at the bottom ──
            // The PlayerInfoBar canvas fills the reserved bottom strip.
            var areaGO   = new GameObject("GameArea");
            areaGO.transform.SetParent(canvasGO.transform, false);
            var areaRect = areaGO.AddComponent<RectTransform>();
            areaRect.anchorMin = Vector2.zero;
            areaRect.anchorMax = Vector2.one;
            areaRect.offsetMin = new Vector2(0f, hudBarHeight); // leave bottom for HUD
            areaRect.offsetMax = Vector2.zero;

            // ── Game view (RT) inside the game area ──────
            var viewGO   = new GameObject("GameView");
            viewGO.transform.SetParent(areaGO.transform, false);

            var rawImage           = viewGO.AddComponent<RawImage>();
            rawImage.texture       = renderTexture;
            rawImage.raycastTarget = false;

            // AspectRatioFitter centres the RT within GameArea and adds bars as needed
            var fitter         = viewGO.AddComponent<AspectRatioFitter>();
            fitter.aspectMode  = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = (float)referenceWidth / referenceHeight;

            var rect       = viewGO.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot     = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(Screen.width, Screen.height - hudBarHeight);
        }

        // ──────────────────────────────────────────────
        //  UTILITY
        // ──────────────────────────────────────────────

        private static void StretchToFill(RectTransform rt)
        {
            rt.anchorMin  = Vector2.zero;
            rt.anchorMax  = Vector2.one;
            rt.offsetMin  = Vector2.zero;
            rt.offsetMax  = Vector2.zero;
        }
    }
}
