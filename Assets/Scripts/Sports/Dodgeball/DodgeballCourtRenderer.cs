using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Procedural court visualization for Dodgeball — same pattern as
    /// Demoball.FieldRenderer (Sprites/Default mesh + LineRenderer).
    ///
    /// Draws:
    ///   - Two infielder halves (Team A on the left, Team B on the right)
    ///   - Outfielder strips around the opposing half (Back/Top/Bottom × 2 teams)
    ///   - Court boundary outline and center divider
    ///
    /// All dimensions come from CourtSetup constants, so the renderer stays in
    /// sync with the spawn/zone logic.
    /// </summary>
    public class DodgeballCourtRenderer : MonoBehaviour
    {
        [Header("Half-court fill colors")]
        [SerializeField] private Color teamAHalfColor = new Color(0.18f, 0.30f, 0.55f, 1f);
        [SerializeField] private Color teamBHalfColor = new Color(0.55f, 0.20f, 0.22f, 1f);

        [Header("Strip outline colors")]
        [SerializeField] private Color teamAStripColor = new Color(0.45f, 0.60f, 0.90f, 0.9f);
        [SerializeField] private Color teamBStripColor = new Color(0.90f, 0.50f, 0.50f, 0.9f);

        [Header("Lines")]
        [SerializeField] private Color boundaryColor = new Color(0.92f, 0.92f, 0.92f, 0.9f);
        [SerializeField] private Color centerLineColor = new Color(0.98f, 0.98f, 0.98f, 1f);
        [SerializeField] private float lineWidth = 0.08f;

        private void Awake()
        {
            BuildCourt();
        }

        private void BuildCourt()
        {
            float hw = CourtSetup.HalfWidth;
            float hh = CourtSetup.HalfHeight;
            float strip = ZoneFactory.StripDepth;

            // Half-court fills (z behind everything else).
            CreateRectFill("HalfA", new Vector2(-hw, -hh), new Vector2(0f, hh), teamAHalfColor, 0.5f);
            CreateRectFill("HalfB", new Vector2(0f, -hh), new Vector2(hw, hh), teamBHalfColor, 0.5f);

            // Outfielder strip outlines: A surrounds B's half (right), B surrounds A's half (left).
            // Team A strips
            CreateRectOutline("A_Back",   new Vector2(hw, -hh),  new Vector2(hw + strip, hh),  teamAStripColor, 0.3f);
            CreateRectOutline("A_Top",    new Vector2(0f, hh),   new Vector2(hw, hh + strip), teamAStripColor, 0.3f);
            CreateRectOutline("A_Bottom", new Vector2(0f, -hh - strip), new Vector2(hw, -hh), teamAStripColor, 0.3f);

            // Team B strips
            CreateRectOutline("B_Back",   new Vector2(-hw - strip, -hh),  new Vector2(-hw, hh), teamBStripColor, 0.3f);
            CreateRectOutline("B_Top",    new Vector2(-hw, hh),   new Vector2(0f, hh + strip), teamBStripColor, 0.3f);
            CreateRectOutline("B_Bottom", new Vector2(-hw, -hh - strip), new Vector2(0f, -hh), teamBStripColor, 0.3f);

            // Court boundary and center line on top.
            CreateRectOutline("CourtBoundary", new Vector2(-hw, -hh), new Vector2(hw, hh), boundaryColor, 0.1f);
            CreateLine("CenterLine", new Vector2(0f, -hh), new Vector2(0f, hh), centerLineColor, 0.05f);
        }

        private GameObject CreateRectFill(string goName, Vector2 min, Vector2 max, Color color, float z)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, z);

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = CreateMat(color);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var verts = new Vector3[]
            {
                new Vector3(min.x, min.y, 0f),
                new Vector3(max.x, min.y, 0f),
                new Vector3(max.x, max.y, 0f),
                new Vector3(min.x, max.y, 0f),
            };
            var tris = new int[] { 0, 2, 1, 0, 3, 2 };
            var mesh = new Mesh { name = goName, vertices = verts, triangles = tris };
            mesh.RecalculateNormals();
            mf.mesh = mesh;
            return go;
        }

        private GameObject CreateRectOutline(string goName, Vector2 min, Vector2 max, Color color, float z)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, z);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = 4;
            lr.startWidth = lineWidth;
            lr.endWidth = lineWidth;
            lr.material = CreateMat(color);
            lr.startColor = color;
            lr.endColor = color;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.SetPosition(0, new Vector3(min.x, min.y, 0f));
            lr.SetPosition(1, new Vector3(max.x, min.y, 0f));
            lr.SetPosition(2, new Vector3(max.x, max.y, 0f));
            lr.SetPosition(3, new Vector3(min.x, max.y, 0f));
            return go;
        }

        private GameObject CreateLine(string goName, Vector2 a, Vector2 b, Color color, float z)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, z);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = false;
            lr.positionCount = 2;
            lr.startWidth = lineWidth;
            lr.endWidth = lineWidth;
            lr.material = CreateMat(color);
            lr.startColor = color;
            lr.endColor = color;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.SetPosition(0, new Vector3(a.x, a.y, 0f));
            lr.SetPosition(1, new Vector3(b.x, b.y, 0f));
            return go;
        }

        private static Material CreateMat(Color c)
        {
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = c;
            return mat;
        }
    }
}
