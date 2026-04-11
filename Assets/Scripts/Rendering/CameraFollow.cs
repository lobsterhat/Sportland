using UnityEngine;
using Sportland.InputHandling;

namespace Sportland.Rendering
{
    /// <summary>
    /// Smoothly follows the player-controlled character.
    /// Automatically re-acquires the target if control transfers to a different character.
    /// Clamps position to arena bounds so the camera never shows outside the walls.
    ///
    /// Setup: attach to Main Camera, set arena bounds to match playfield walls.
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

        [Header("=== ARENA BOUNDS ===")]
        [Tooltip("Clamp camera so it never reveals outside the arena walls.")]
        [SerializeField] private bool clampToArena = true;
        [SerializeField] private float arenaMinX = -15f;
        [SerializeField] private float arenaMaxX = 15f;
        [SerializeField] private float arenaMinY = -9f;
        [SerializeField] private float arenaMaxY = 9f;

        // ──────────────────────────────────────────────
        //  RUNTIME
        // ──────────────────────────────────────────────

        private new Camera camera;
        private Transform target;

        // ──────────────────────────────────────────────
        //  UNITY LIFECYCLE
        // ──────────────────────────────────────────────

        private void Awake()
        {
            camera = GetComponent<Camera>();
        }

        private void Start()
        {
            FindTarget();

            // Snap to target on first frame — no lerp from origin
            if (target != null)
                SnapToTarget();
        }

        private void LateUpdate()
        {
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

                pos.x = Mathf.Clamp(pos.x, arenaMinX + halfW, arenaMaxX - halfW);
                pos.y = Mathf.Clamp(pos.y, arenaMinY + halfH, arenaMaxY - halfH);
            }

            return pos;
        }

        private void SnapToTarget()
        {
            transform.position = ComputeDesiredPosition();
        }
    }
}
