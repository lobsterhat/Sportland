using UnityEngine;

namespace Sportland.Sports.Demoball
{
    /// <summary>
    /// Draws the Demoball circular field at runtime using procedural meshes.
    ///
    /// Layers (back to front by Z):
    ///   Outer ring  — dark green border strip around the scoring ring
    ///   Field fill  — mid green interior circle
    ///   Centre mark — small white dot at the cannon position
    ///   Scoring arc — thin white ring at the scoring radius
    ///
    /// All meshes are generated in Awake and never updated (static field art).
    /// Attach to any GameObject in the Demoball scene; set fieldRadius to match
    /// ScoringRing.ringRadius (default 12).
    /// </summary>
    public class FieldRenderer : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        //  CONFIGURATION
        // ──────────────────────────────────────────────

        [Header("=== FIELD GEOMETRY ===")]
        [Tooltip("Outer radius of the playing field (should match ScoringRing.ringRadius).")]
        [SerializeField] private float fieldRadius = 12f;

        [Tooltip("Width of the scoring ring strip drawn at the perimeter.")]
        [SerializeField] private float ringWidth = 1.5f;

        [Tooltip("Radius of the centre-mark circle.")]
        [SerializeField] private float centreMarkRadius = 0.4f;

        [Tooltip("Number of segments used for all circles. Higher = smoother.")]
        [SerializeField] private int segments = 64;

        [Header("=== COLOURS ===")]
        [SerializeField] private Color fieldColour      = new Color(0.18f, 0.50f, 0.22f, 1f); // mid green
        [SerializeField] private Color ringColour       = new Color(0.25f, 0.65f, 0.28f, 1f); // lighter ring
        [SerializeField] private Color lineColour       = new Color(0.85f, 0.92f, 0.82f, 0.7f); // soft white line
        [SerializeField] private Color centreColour     = new Color(0.85f, 0.92f, 0.82f, 0.8f);

        // ──────────────────────────────────────────────
        //  LIFECYCLE
        // ──────────────────────────────────────────────

        private void Awake()
        {
            BuildField();
        }

        // ──────────────────────────────────────────────
        //  CONSTRUCTION
        // ──────────────────────────────────────────────

        private void BuildField()
        {
            // Layer z-offsets (camera at z = -10, field near z = 0)
            float zField  =  0.3f;
            float zRing   =  0.2f;
            float zLine   =  0.1f;
            float zCentre =  0.1f;

            // Field interior (solid filled circle)
            CreateFilledCircle("Field",   fieldRadius,                   fieldColour, zField);

            // Scoring ring strip (annulus between fieldRadius-ringWidth and fieldRadius)
            CreateAnnulus("ScoringRing", fieldRadius - ringWidth, fieldRadius, ringColour, zRing);

            // Thin outline circle at the scoring boundary
            CreateLineCircle("RingLine", fieldRadius - ringWidth, lineColour, zLine);

            // Outer boundary line
            CreateLineCircle("OuterLine", fieldRadius, lineColour, zLine);

            // Centre mark
            CreateFilledCircle("CentreMark", centreMarkRadius, centreColour, zCentre);
        }

        // ──────────────────────────────────────────────
        //  MESH HELPERS
        // ──────────────────────────────────────────────

        /// <summary>Solid filled circle using a triangle fan.</summary>
        private GameObject CreateFilledCircle(string goName, float radius, Color colour, float z)
        {
            var go  = new GameObject(goName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, z);

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = CreateUnlitColourMaterial(colour);

            var verts  = new Vector3[segments + 1];
            var tris   = new int[segments * 3];
            verts[0]   = Vector3.zero;

            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                verts[i + 1] = new Vector3(Mathf.Cos(angle) * radius,
                                           Mathf.Sin(angle) * radius, 0f);
                tris[i * 3]     = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = (i + 1) % segments + 1;
            }

            var mesh      = new Mesh { name = goName };
            mesh.vertices  = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mf.mesh        = mesh;

            return go;
        }

        /// <summary>Annulus (ring shape) between innerRadius and outerRadius.</summary>
        private GameObject CreateAnnulus(string goName, float innerRadius, float outerRadius,
                                         Color colour, float z)
        {
            var go  = new GameObject(goName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, z);

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = CreateUnlitColourMaterial(colour);

            int   vertCount = segments * 2;
            var   verts     = new Vector3[vertCount];
            var   tris      = new int[segments * 6];

            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                float cos   = Mathf.Cos(angle);
                float sin   = Mathf.Sin(angle);

                verts[i]            = new Vector3(cos * innerRadius, sin * innerRadius, 0f);
                verts[i + segments] = new Vector3(cos * outerRadius, sin * outerRadius, 0f);

                int next = (i + 1) % segments;
                int b    = i * 6;
                tris[b]     = i;
                tris[b + 1] = i + segments;
                tris[b + 2] = next + segments;
                tris[b + 3] = i;
                tris[b + 4] = next + segments;
                tris[b + 5] = next;
            }

            var mesh      = new Mesh { name = goName };
            mesh.vertices  = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mf.mesh        = mesh;

            return go;
        }

        /// <summary>Thin line circle using a LineRenderer.</summary>
        private GameObject CreateLineCircle(string goName, float radius, Color colour, float z)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, z);

            var lr             = go.AddComponent<LineRenderer>();
            lr.useWorldSpace   = false;
            lr.loop            = true;
            lr.positionCount   = segments;
            lr.startWidth      = 0.08f;
            lr.endWidth        = 0.08f;
            lr.material        = CreateUnlitColourMaterial(colour);
            lr.startColor      = colour;
            lr.endColor        = colour;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows  = false;

            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                lr.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius,
                                              Mathf.Sin(angle) * radius, 0f));
            }

            return go;
        }

        // ──────────────────────────────────────────────
        //  MATERIAL HELPER
        // ──────────────────────────────────────────────

        private static Material CreateUnlitColourMaterial(Color colour)
        {
            var mat   = new Material(Shader.Find("Sprites/Default"));
            mat.color = colour;
            return mat;
        }
    }
}
