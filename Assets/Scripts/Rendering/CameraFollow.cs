using UnityEngine;
using Sportland.InputHandling;

namespace Sportland.Rendering
{
    /// <summary>
    /// Smoothly follows the player-controlled character.
    /// Automatically re-acquires the target if control transfers to a different character.
    /// Clamps position to arena bounds so the camera never reveals outside the walls.
    ///
    /// Scroll-wheel zoom adjusts orthographicSize within a tunable range.
    /// When the camera is zoomed out beyond the arena, it centres itself rather than
    /// producing a broken clamp (min > max) at the boundary edges.
    ///
    /// Setup: attach to Main Camera alongside PixelArtRenderer.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraFollow : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        //  CONFIGURATION
        // ──────────────────────────────────────────────

        [Header("=== TRACKING ===")]
        [Tooltip("How quickly the camera catches up to the target. Higher = snappier.")]
        [SerializeField] private float smoothSpeed = 6f;

        [Header("=== ZOOM ===")]
        [Tooltip("Smallest orthographic size (most zoomed in).")]
        [SerializeField] private float minZoom = 4f;

        [Tooltip("Largest orthographic size (most zoomed out). " +
                 "Values larger than the arena half-height just centre the camera.")]
        [SerializeField] private float maxZoom = 11f;

        [Tooltip("How many units the zoom changes per scroll tick.")]
        [SerializeField] private float zoomSpeed = 2f;

        [Tooltip("How quickly the camera lerps to the target zoom level.")]
        [SerializeField] private float zoomSmoothing = 8f;

        [Header("=== ARENA BOUNDS ===")]
        [Tooltip("Clamp camera so it never reveals outside the arena walls.")]
        [SerializeField] private bool clampToArena = true;
        [SerializeField] private float arenaMinX = -15f;
        [SerializeField] private float arenaMaxX =  15f;
        [SerializeField] private float arenaMinY =  -9f;
        [SerializeField] private float arenaMaxY =   9f;

        // ──────────────────────────────────────────────
        //  RUNTIME
        // ──────────────────────────────────────────────

        private new Camera camera;
        private Transform target;
        private float targetZoom;

        // ──────────────────────────────────────────────
        //  UNITY LIFECYCLE
        // ──────────────────────────────────────────────

        private void Awake()
        {
            camera = GetComponent<Camera>();
        }

        private void Start()
        {
            targetZoom = camera.orthographicSize;
            FindTarget();

            if (target != null)
                SnapToTarget();
        }

        private void LateUpdate()
        {
            HandleZoom();

            // Re-acquire if the target was lost (role swap, scene change, etc.)
            if (target == null)
            {
                FindTarget();
                if (target == null) return;
            }

            Vector3 desired = ComputeDesiredPosition();
            transform.position = Vector3.Lerp(transform.position, desired, smoothSpeed * Time.deltaTime);
        }

        // ──────────────────────────────────────────────
        //  ZOOM
        // ──────────────────────────────────────────────

        private void HandleZoom()
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f)
                targetZoom = Mathf.Clamp(targetZoom - scroll * zoomSpeed, minZoom, maxZoom);

            camera.orthographicSize = Mathf.Lerp(
                camera.orthographicSize, targetZoom, zoomSmoothing * Time.deltaTime);
        }

        // ──────────────────────────────────────────────
        //  TARGET ACQUISITION
        // ──────────────────────────────────────────────

        private void FindTarget()
        {
            var brokers = Object.FindObjectsByType<InputBroker>(FindObjectsSortMode.None);
            foreach (var broker in brokers)
            {
                if (broker.IsPlayerControlled)
                {
                    target = broker.transform;
                    return;
                }
            }
            target = null;
        }

        // ──────────────────────────────────────────────
        //  POSITION CALCULATION
        // ──────────────────────────────────────────────

        private Vector3 ComputeDesiredPosition()
        {
            Vector3 pos = new Vector3(target.position.x, target.position.y, transform.position.z);

            if (clampToArena)
            {
                float halfH = camera.orthographicSize;
                float halfW = halfH * camera.aspect;

                float arenaCentreX  = (arenaMinX + arenaMaxX) * 0.5f;
                float arenaCentreY  = (arenaMinY + arenaMaxY) * 0.5f;
                float arenaHalfH    = (arenaMaxY - arenaMinY) * 0.5f;
                float arenaHalfW    = (arenaMaxX - arenaMinX) * 0.5f;

                // When zoomed out beyond the arena, centre rather than producing
                // an inverted clamp (min > max) which gives undefined behaviour.
                if (halfH >= arenaHalfH)
                    pos.y = arenaCentreY;
                else
                    pos.y = Mathf.Clamp(pos.y, arenaMinY + halfH, arenaMaxY - halfH);

                if (halfW >= arenaHalfW)
                    pos.x = arenaCentreX;
                else
                    pos.x = Mathf.Clamp(pos.x, arenaMinX + halfW, arenaMaxX - halfW);
            }

            return pos;
        }

        private void SnapToTarget()
        {
            transform.position = ComputeDesiredPosition();
        }
    }
}
