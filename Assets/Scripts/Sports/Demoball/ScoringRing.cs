using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sportland.Sports.Demoball
{
    /// <summary>
    /// The circular outer scoring ring. Entering the ring with the ball puts a carrier
    /// in position to attempt a touch-down. The entire ring is worth 1 point; a rotating
    /// bonus zone (one quarter of the ring) adds extra points.
    ///
    /// Bonus zone rotation is triggered externally by DemoballGameManager every 30 seconds.
    ///
    /// Touch-down is a deliberate action (not automatic on entry) — the carrier chooses
    /// when to touch down, potentially waiting for the bonus zone to rotate to their position.
    /// A carrier tackled inside the ring must surrender the ball before tagging up.
    ///
    /// Setup: requires a CircleCollider2D set to isTrigger. The component enforces this in Awake.
    /// </summary>
    [RequireComponent(typeof(CircleCollider2D))]
    public class ScoringRing : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        //  CONFIGURATION
        // ──────────────────────────────────────────────

        [Header("=== RING GEOMETRY ===")]
        [Tooltip("Outer radius of the field / scoring ring (world units).")]
        [SerializeField] private float ringRadius = 12f;

        [Tooltip("Fraction of the ring radius that counts as 'inside the ring'. " +
                 "E.g. 0.88 means the ring occupies the outer 12% of the field area.")]
        [SerializeField] private float innerEdgeFraction = 0.88f;

        [Header("=== BONUS ZONE ===")]
        [Tooltip("Arc width of the bonus zone in degrees. 90 = one quarter of the ring.")]
        [SerializeField] private float bonusZoneArcDegrees = 90f;

        [Header("=== VISUALS ===")]
        [SerializeField] private Color ringGizmoColor  = new Color(0.2f, 0.9f, 0.2f, 0.4f);
        [SerializeField] private Color bonusGizmoColor = new Color(1f,   0.85f, 0f,   0.45f);

        // ──────────────────────────────────────────────
        //  STATE
        // ──────────────────────────────────────────────

        /// <summary>Centre angle of the active bonus zone (degrees, 0 = right / east, CCW positive).</summary>
        public float BonusZoneCentreAngle { get; private set; }

        private readonly HashSet<DemoballMovementController> playersInRing =
            new HashSet<DemoballMovementController>();

        // ──────────────────────────────────────────────
        //  EVENTS
        // ──────────────────────────────────────────────

        public event Action<DemoballMovementController> OnPlayerEnterRing;
        public event Action<DemoballMovementController> OnPlayerExitRing;
        public event Action<float> OnBonusZoneRotated; // passes new centre angle

        // ──────────────────────────────────────────────
        //  UNITY LIFECYCLE
        // ──────────────────────────────────────────────

        private void Awake()
        {
            var col = GetComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius    = ringRadius;
        }

        // ──────────────────────────────────────────────
        //  BONUS ZONE
        // ──────────────────────────────────────────────

        /// <summary>
        /// Moves the bonus zone to a new random position around the ring.
        /// Called by DemoballGameManager every 30 seconds.
        /// </summary>
        public void RotateBonusZone()
        {
            BonusZoneCentreAngle = UnityEngine.Random.Range(0f, 360f);
            OnBonusZoneRotated?.Invoke(BonusZoneCentreAngle);
            // TODO: drive a visual arc renderer to highlight the bonus zone on screen
        }

        /// <summary>
        /// Returns true if the given world position falls within the active bonus zone arc.
        /// </summary>
        public bool IsInBonusZone(Vector2 worldPosition)
        {
            Vector2 dir   = worldPosition - (Vector2)transform.position;
            float   angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            if (angle < 0f) angle += 360f;

            float diff = Mathf.Abs(Mathf.DeltaAngle(angle, BonusZoneCentreAngle));
            return diff <= bonusZoneArcDegrees * 0.5f;
        }

        /// <summary>
        /// Returns true if the world position is inside the scoring ring annulus.
        /// </summary>
        public bool IsInRing(Vector2 worldPosition)
        {
            float dist = Vector2.Distance(worldPosition, transform.position);
            return dist >= ringRadius * innerEdgeFraction && dist <= ringRadius;
        }

        // ──────────────────────────────────────────────
        //  PLAYER TRACKING
        // ──────────────────────────────────────────────

        public bool IsPlayerInRing(DemoballMovementController player)
            => playersInRing.Contains(player);

        private void OnTriggerStay2D(Collider2D other)
        {
            var player = other.GetComponent<DemoballMovementController>();
            if (player == null) return;

            bool inAnnulus = IsInRing(other.transform.position);
            bool tracked   = playersInRing.Contains(player);

            if (inAnnulus && !tracked)
            {
                playersInRing.Add(player);
                player.EnterScoringRing(this);
                OnPlayerEnterRing?.Invoke(player);
            }
            else if (!inAnnulus && tracked)
            {
                playersInRing.Remove(player);
                player.ExitScoringRing();
                OnPlayerExitRing?.Invoke(player);
            }
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            var player = other.GetComponent<DemoballMovementController>();
            if (player == null) return;

            if (playersInRing.Remove(player))
            {
                player.ExitScoringRing();
                OnPlayerExitRing?.Invoke(player);
            }
        }

        // ──────────────────────────────────────────────
        //  GIZMOS
        // ──────────────────────────────────────────────

        private void OnDrawGizmos()
        {
            Gizmos.color = ringGizmoColor;
            DrawCircle(transform.position, ringRadius);
            DrawCircle(transform.position, ringRadius * innerEdgeFraction);

            Gizmos.color = bonusGizmoColor;
            DrawArc(transform.position, ringRadius, BonusZoneCentreAngle, bonusZoneArcDegrees, 24);
        }

        private static void DrawCircle(Vector3 centre, float radius, int segments = 48)
        {
            for (int i = 0; i < segments; i++)
            {
                float a0 = i       * Mathf.PI * 2f / segments;
                float a1 = (i + 1) * Mathf.PI * 2f / segments;
                Gizmos.DrawLine(
                    centre + new Vector3(Mathf.Cos(a0) * radius, Mathf.Sin(a0) * radius),
                    centre + new Vector3(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius));
            }
        }

        private static void DrawArc(Vector3 centre, float radius,
                                    float centreDeg, float arcDeg, int steps)
        {
            float startRad = (centreDeg - arcDeg * 0.5f) * Mathf.Deg2Rad;
            float endRad   = (centreDeg + arcDeg * 0.5f) * Mathf.Deg2Rad;

            Vector3 prev = centre + new Vector3(Mathf.Cos(startRad) * radius,
                                                Mathf.Sin(startRad) * radius, 0f);
            for (int i = 1; i <= steps; i++)
            {
                float t    = Mathf.Lerp(startRad, endRad, (float)i / steps);
                Vector3 pt = centre + new Vector3(Mathf.Cos(t) * radius,
                                                  Mathf.Sin(t) * radius, 0f);
                Gizmos.DrawLine(prev, pt);
                prev = pt;
            }
            // Close the wedge to the centre
            Gizmos.DrawLine(centre, centre + new Vector3(
                Mathf.Cos(startRad) * radius, Mathf.Sin(startRad) * radius, 0f));
            Gizmos.DrawLine(centre, prev);
        }
    }
}
