using UnityEngine;
using Sportland.Movement;

namespace Sportland.World
{
    /// <summary>
    /// Marks a region of the world as having a specific surface type.
    /// Any BaseMovementController that enters the trigger receives the
    /// surface's movement modifiers. Multiple overlapping zones are handled
    /// by priority (highest wins).
    ///
    /// Hard obstacles (full block) need only a non-trigger Collider2D.
    /// Soft obstacles (bushes, mud) use this component with a heavy-penalty
    /// surface type (Undergrowth, Mud, etc.).
    ///
    /// Setup:
    ///   1. Add to a GameObject with a BoxCollider2D (or any Collider2D).
    ///   2. Choose a surfaceType — defaults are applied automatically.
    ///   3. Optionally assign a custom SurfaceDefinition ScriptableObject for
    ///      fine-tuned values.
    ///   4. For Slope zones, set the slopeDirection arrow to point downhill.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class SurfaceZone : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        //  CONFIGURATION
        // ──────────────────────────────────────────────

        [Header("Surface")]
        [Tooltip("Built-in surface type. Auto-populates default modifiers.")]
        [SerializeField] private SurfaceType surfaceType = SurfaceType.Grass;

        [Tooltip("Optional custom ScriptableObject. Leave null to use built-in defaults.")]
        [SerializeField] private SurfaceDefinition customDefinition;

        [Header("Slope Direction (Slope type only)")]
        [Tooltip("World-space downhill direction. Moving with this is faster; against it is slower.")]
        [SerializeField] private Vector2 slopeDirection = Vector2.down;

        [Header("Debug")]
        [SerializeField] private bool showGizmo = true;

        // ──────────────────────────────────────────────
        //  RUNTIME
        // ──────────────────────────────────────────────

        private SurfaceDefinition runtimeDefinition;

        /// <summary>The resolved SurfaceDefinition used at runtime.</summary>
        public SurfaceDefinition Definition => runtimeDefinition;

        // ──────────────────────────────────────────────
        //  LIFECYCLE
        // ──────────────────────────────────────────────

        private void Awake()
        {
            if (customDefinition != null)
            {
                runtimeDefinition = customDefinition;
            }
            else
            {
                runtimeDefinition = SurfaceDefinition.CreateDefault(surfaceType);
                if (surfaceType == SurfaceType.Slope)
                    runtimeDefinition.slopeDirection = slopeDirection;
            }

            // Always a trigger — hard-block colliders live separately
            var col = GetComponent<Collider2D>();
            if (!col.isTrigger)
                col.isTrigger = true;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            var movement = other.GetComponent<BaseMovementController>();
            if (movement != null)
                movement.EnterSurface(this, runtimeDefinition);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            var movement = other.GetComponent<BaseMovementController>();
            if (movement != null)
                movement.ExitSurface(this);
        }

        // ──────────────────────────────────────────────
        //  GIZMOS
        // ──────────────────────────────────────────────

        private void OnDrawGizmos()
        {
            if (!showGizmo) return;

            SurfaceType effectiveType = customDefinition != null
                ? customDefinition.surfaceType
                : surfaceType;

            Color fill = SurfaceDefinition.GetDefaultGizmoColor(effectiveType);
            Color edge = new Color(fill.r, fill.g, fill.b, 1f);

            var col = GetComponent<Collider2D>();
            if (col is BoxCollider2D box)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.color = fill;
                Gizmos.DrawCube(box.offset, box.size);
                Gizmos.color = edge;
                Gizmos.DrawWireCube(box.offset, box.size);
            }

            // Slope arrow
            bool isSlope = effectiveType == SurfaceType.Slope;
            if (isSlope)
            {
                Gizmos.matrix = Matrix4x4.identity;
                Gizmos.color  = Color.yellow;
                Vector3 center = transform.position;
                Vector3 dir    = new Vector3(slopeDirection.x, slopeDirection.y, 0f).normalized;
                Gizmos.DrawRay(center, dir * 1.5f);
                // Arrowhead
                Vector3 right = Vector3.Cross(dir, Vector3.back) * 0.3f;
                Gizmos.DrawLine(center + dir * 1.5f, center + dir * 1.1f + right);
                Gizmos.DrawLine(center + dir * 1.5f, center + dir * 1.1f - right);
            }

            // Surface type label (editor only, scene view)
#if UNITY_EDITOR
            UnityEditor.Handles.color = edge;
            string label = customDefinition != null
                ? customDefinition.surfaceName
                : effectiveType.ToString();
            UnityEditor.Handles.Label(transform.position, label);
#endif
        }
    }
}
