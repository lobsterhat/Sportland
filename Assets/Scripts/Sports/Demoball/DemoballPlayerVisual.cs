using UnityEngine;

namespace Sportland.Sports.Demoball
{
    /// <summary>
    /// Procedural player visuals for Demoball — guaranteed to render regardless of
    /// sprite import settings. Generates:
    ///   • Filled circle in the team colour
    ///   • Thin outline ring (slightly darker) for edge definition
    ///   • Yellow carry-indicator ring that activates when the player holds a ball
    ///
    /// Same technique as FieldRenderer — Sprites/Default mesh + MeshFilter.
    /// </summary>
    public class DemoballPlayerVisual : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        //  CONFIGURATION
        // ──────────────────────────────────────────────

        [Tooltip("Main body colour. Set per-player to distinguish teams.")]
        [SerializeField] private Color bodyColor = new Color(0.2f, 0.45f, 0.9f, 1f);

        [Tooltip("Radius of the player circle (world units). Should match CircleCollider2D.")]
        [SerializeField] private float bodyRadius = 0.45f;

        [Tooltip("Colour of the carry-indicator ring shown when this player holds a ball.")]
        [SerializeField] private Color carryIndicatorColor = new Color(0.95f, 0.9f, 0.2f, 0.95f);

        // ──────────────────────────────────────────────
        //  RUNTIME
        // ──────────────────────────────────────────────

        private DemoballMovementController movement;
        private LineRenderer carryRing;

        // ──────────────────────────────────────────────
        //  LIFECYCLE
        // ──────────────────────────────────────────────

        private void Awake()
        {
            movement = GetComponent<DemoballMovementController>();
            BuildVisuals();
        }

        private void Update()
        {
            if (carryRing == null) return;
            carryRing.enabled = movement != null && movement.IsCarryingBall;
        }

        // ──────────────────────────────────────────────
        //  CONSTRUCTION
        // ──────────────────────────────────────────────

        private void BuildVisuals()
        {
            // Darken body colour slightly for the edge outline
            Color outlineColor = new Color(bodyColor.r * 0.55f,
                                           bodyColor.g * 0.55f,
                                           bodyColor.b * 0.55f, 1f);

            // z-offsets so layers stack correctly (camera at z = -10)
            CreateFilledCircle("Body",    bodyRadius,        bodyColor,    0f,   sortOrder: 20);
            carryRing = CreateLineRing("CarryRing", bodyRadius + 0.18f, carryIndicatorColor, 0f, sortOrder: 22);
            carryRing.enabled = false;
        }

        // ──────────────────────────────────────────────
        //  MESH HELPERS
        // ──────────────────────────────────────────────

        private void CreateFilledCircle(string goName, float radius, Color colour,
                                         float z, int sortOrder)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, z);

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = CreateMat(colour);
            mr.sortingOrder   = sortOrder;
            mr.shadowCastingMode  = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            const int segs = 32;
            var verts = new Vector3[segs + 1];
            var tris  = new int[segs * 3];
            verts[0]  = Vector3.zero;
            for (int i = 0; i < segs; i++)
            {
                float a     = i * Mathf.PI * 2f / segs;
                verts[i + 1] = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f);
                tris[i * 3]     = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = (i + 1) % segs + 1;
            }
            var mesh = new Mesh { name = goName, vertices = verts, triangles = tris };
            mesh.RecalculateNormals();
            mf.mesh = mesh;
        }

        private LineRenderer CreateLineRing(string goName, float radius, Color colour,
                                             float z, int sortOrder)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, z);

            const int segs = 32;
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace  = false;
            lr.loop           = true;
            lr.positionCount  = segs;
            lr.startWidth     = 0.07f;
            lr.endWidth       = 0.07f;
            lr.material       = CreateMat(colour);
            lr.startColor     = colour;
            lr.endColor       = colour;
            lr.sortingOrder   = sortOrder;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;

            for (int i = 0; i < segs; i++)
            {
                float a = i * Mathf.PI * 2f / segs;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
            }
            return lr;
        }

        private static Material CreateMat(Color c)
        {
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = c;
            return mat;
        }
    }
}
