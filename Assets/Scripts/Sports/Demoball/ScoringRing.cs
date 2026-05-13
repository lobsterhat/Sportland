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

        [Header("=== INNER EXCLUSION ===")]
        [Tooltip("Radius of the central no-go zone for defenders during dead-ball windows " +
                 "(kickoff and post-score countdowns). Defenders may roam outside this circle " +
                 "but cannot enter it until the ball launches.")]
        [SerializeField] private float innerExclusionRadius = 4f;

        [Tooltip("Width of the inner-exclusion indicator ring (line thickness, world units).")]
        [SerializeField] private float innerExclusionLineWidth = 0.12f;

        [Tooltip("Colour of the inner-exclusion indicator ring.")]
        [SerializeField] private Color innerExclusionColour = new Color(0.95f, 0.55f, 0.25f, 0.85f);

        [Header("=== QUADRANTS ===")]
        [Tooltip("Quadrant the offense starts in. This quadrant is locked (un-scorable) " +
                 "until a score is registered elsewhere; then the just-scored quadrant locks " +
                 "and this one re-opens.")]
        [SerializeField] private ScoringQuadrant initialLockedQuadrant = ScoringQuadrant.Q1;

        [Tooltip("Width of the quadrant indicator arc (line thickness, world units).")]
        [SerializeField] private float quadrantArcWidth = 0.55f;

        [Tooltip("Colour of an open (scorable) quadrant arc.")]
        [SerializeField] private Color openQuadrantColour = new Color(0.45f, 0.95f, 0.55f, 0.85f);

        [Tooltip("Colour of the locked (un-scorable) quadrant arc.")]
        [SerializeField] private Color lockedQuadrantColour = new Color(0.4f, 0.4f, 0.4f, 0.85f);

        [Header("=== VISUALS ===")]
        [SerializeField] private Color ringGizmoColor  = new Color(0.2f, 0.9f, 0.2f, 0.4f);
        [SerializeField] private Color bonusGizmoColor = new Color(1f,   0.85f, 0f,   0.45f);

        // ──────────────────────────────────────────────
        //  STATE
        // ──────────────────────────────────────────────

        /// <summary>Centre angle of the active bonus zone (degrees, 0 = right / east, CCW positive).</summary>
        public float BonusZoneCentreAngle { get; private set; }

        /// <summary>The quadrant currently locked from scoring. Defaults to initialLockedQuadrant on reset.</summary>
        public ScoringQuadrant LockedQuadrant { get; private set; } = ScoringQuadrant.Q1;

        /// <summary>Outer radius of the scoring ring (= the field perimeter).</summary>
        public float RingOuterRadius => ringRadius;

        /// <summary>Inner edge of the scoring ring annulus.</summary>
        public float RingInnerRadius => ringRadius * innerEdgeFraction;

        /// <summary>Midpoint between the inner and outer radii — anchor for placing players in the endzone.</summary>
        public float RingMidRadius   => ringRadius * (innerEdgeFraction + 1f) * 0.5f;

        /// <summary>Radius of the central defenders-only-after-launch exclusion zone.</summary>
        public float InnerExclusionRadius => innerExclusionRadius;

        private readonly HashSet<DemoballMovementController> playersInRing =
            new HashSet<DemoballMovementController>();

        private readonly LineRenderer[] quadrantArcs = new LineRenderer[4];
        private LineRenderer innerExclusionRing;

        // ──────────────────────────────────────────────
        //  EVENTS
        // ──────────────────────────────────────────────

        public event Action<DemoballMovementController> OnPlayerEnterRing;
        public event Action<DemoballMovementController> OnPlayerExitRing;
        public event Action<float> OnBonusZoneRotated; // passes new centre angle
        public event Action<ScoringQuadrant> OnLockedQuadrantChanged;

        // ──────────────────────────────────────────────
        //  UNITY LIFECYCLE
        // ──────────────────────────────────────────────

        private void Awake()
        {
            var col = GetComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius    = ringRadius;

            LockedQuadrant = initialLockedQuadrant;
            BuildQuadrantArcs();
            RefreshQuadrantArcColours();
            BuildInnerExclusionRing();
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
        //  QUADRANTS
        // ──────────────────────────────────────────────

        /// <summary>
        /// Returns which 90° quadrant of the ring the given world position falls in.
        /// Q1 = NE (0-90°), Q2 = NW (90-180°), Q3 = SW (180-270°), Q4 = SE (270-360°),
        /// measured CCW from +X (standard math convention).
        /// </summary>
        public ScoringQuadrant GetQuadrantAt(Vector2 worldPosition)
        {
            Vector2 dir = worldPosition - (Vector2)transform.position;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            if (angle < 0f) angle += 360f;

            if (angle < 90f)  return ScoringQuadrant.Q1;
            if (angle < 180f) return ScoringQuadrant.Q2;
            if (angle < 270f) return ScoringQuadrant.Q3;
            return ScoringQuadrant.Q4;
        }

        /// <summary>True if the position falls inside the currently-locked quadrant.</summary>
        public bool IsInLockedQuadrant(Vector2 worldPosition)
            => GetQuadrantAt(worldPosition) == LockedQuadrant;

        /// <summary>The centre angle of a quadrant, in degrees CCW from +X.</summary>
        public static float GetQuadrantCentreAngle(ScoringQuadrant q)
        {
            switch (q)
            {
                case ScoringQuadrant.Q1: return  45f;
                case ScoringQuadrant.Q2: return 135f;
                case ScoringQuadrant.Q3: return 225f;
                default:                 return 315f; // Q4
            }
        }

        /// <summary>The quadrant 180° opposite the given one.</summary>
        public static ScoringQuadrant GetOppositeQuadrant(ScoringQuadrant q)
        {
            switch (q)
            {
                case ScoringQuadrant.Q1: return ScoringQuadrant.Q3;
                case ScoringQuadrant.Q2: return ScoringQuadrant.Q4;
                case ScoringQuadrant.Q3: return ScoringQuadrant.Q1;
                default:                 return ScoringQuadrant.Q2; // Q4 → Q2
            }
        }

        /// <summary>
        /// Called after a successful score. Locks the quadrant the carrier scored in,
        /// re-opening whichever quadrant was previously locked.
        /// </summary>
        public void RegisterScore(Vector2 carrierPosition)
        {
            ScoringQuadrant scored = GetQuadrantAt(carrierPosition);
            if (scored == LockedQuadrant) return; // shouldn't happen — TryTouchDown gates
            LockedQuadrant = scored;
            RefreshQuadrantArcColours();
            OnLockedQuadrantChanged?.Invoke(LockedQuadrant);
            BlockingAiLog.Log($"<b>Quadrant locked</b>: {LockedQuadrant} — others now open");
        }

        /// <summary>Resets the locked quadrant to the configured initial value (period reset).</summary>
        public void ResetLockedQuadrant()
        {
            if (LockedQuadrant == initialLockedQuadrant) return;
            LockedQuadrant = initialLockedQuadrant;
            RefreshQuadrantArcColours();
            OnLockedQuadrantChanged?.Invoke(LockedQuadrant);
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
        //  QUADRANT VISUAL ARCS
        // ──────────────────────────────────────────────

        private void BuildQuadrantArcs()
        {
            float arcRadius = ringRadius - quadrantArcWidth * 0.5f;
            const int segs = 24;
            for (int q = 0; q < 4; q++)
            {
                var go = new GameObject($"QuadrantArc_{(ScoringQuadrant)q}");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0f, 0f, -0.05f);

                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace  = false;
                lr.loop           = false;
                lr.positionCount  = segs + 1;
                lr.startWidth     = quadrantArcWidth;
                lr.endWidth       = quadrantArcWidth;
                lr.material       = new Material(Shader.Find("Sprites/Default"));
                lr.sortingOrder   = 5;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;

                float startDeg = q * 90f + 2f;        // small gap between quadrants for clarity
                float endDeg   = q * 90f + 90f - 2f;
                for (int i = 0; i <= segs; i++)
                {
                    float t   = (float)i / segs;
                    float deg = Mathf.Lerp(startDeg, endDeg, t);
                    float rad = deg * Mathf.Deg2Rad;
                    lr.SetPosition(i, new Vector3(Mathf.Cos(rad) * arcRadius,
                                                  Mathf.Sin(rad) * arcRadius, 0f));
                }
                quadrantArcs[q] = lr;
            }
        }

        private void RefreshQuadrantArcColours()
        {
            for (int q = 0; q < 4; q++)
            {
                var lr = quadrantArcs[q];
                if (lr == null) continue;
                Color c = (ScoringQuadrant)q == LockedQuadrant ? lockedQuadrantColour : openQuadrantColour;
                lr.startColor = c;
                lr.endColor   = c;
                if (lr.material != null) lr.material.color = c;
            }
        }

        private void BuildInnerExclusionRing()
        {
            const int segs = 48;
            var go = new GameObject("InnerExclusionRing");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, -0.05f);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace  = false;
            lr.loop           = true;
            lr.positionCount  = segs;
            lr.startWidth     = innerExclusionLineWidth;
            lr.endWidth       = innerExclusionLineWidth;
            lr.material       = new Material(Shader.Find("Sprites/Default")) { color = innerExclusionColour };
            lr.startColor     = innerExclusionColour;
            lr.endColor       = innerExclusionColour;
            lr.sortingOrder   = 4;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;

            for (int i = 0; i < segs; i++)
            {
                float a = i * Mathf.PI * 2f / segs;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a) * innerExclusionRadius,
                                              Mathf.Sin(a) * innerExclusionRadius, 0f));
            }
            innerExclusionRing = lr;
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
