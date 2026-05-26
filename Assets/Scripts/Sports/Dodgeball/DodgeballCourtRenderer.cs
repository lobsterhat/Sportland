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

        // Sorting layout (low to high):
        //   -10  half-court fills (deepest background)
        //    -5  outfielder strip outlines
        //    -3  court boundary
        //    -2  center line
        //   (everything else renders above)
        private const int HalfFillOrder     = -10;
        private const int StripOutlineOrder = -5;
        private const int BoundaryOrder     = -3;
        private const int CenterLineOrder   = -2;

        private void BuildCourt()
        {
            float hw = CourtSetup.HalfWidth;
            float hh = CourtSetup.HalfHeight;
            float strip = ZoneFactory.StripDepth;

            CreateRectFill("HalfA", new Vector2(-hw, -hh), new Vector2(0f, hh), teamAHalfColor, HalfFillOrder);
            CreateRectFill("HalfB", new Vector2(0f, -hh), new Vector2(hw, hh), teamBHalfColor, HalfFillOrder);

            CreateRectOutline("A_Back",   new Vector2(hw, -(hh + strip)),  new Vector2(hw + strip, hh + strip),  teamAStripColor, StripOutlineOrder);
            CreateRectOutline("A_Top",    new Vector2(0f, hh),   new Vector2(hw, hh + strip), teamAStripColor, StripOutlineOrder);
            CreateRectOutline("A_Bottom", new Vector2(0f, -hh - strip), new Vector2(hw, -hh), teamAStripColor, StripOutlineOrder);

            CreateRectOutline("B_Back",   new Vector2(-hw - strip, -(hh + strip)),  new Vector2(-hw, hh + strip), teamBStripColor, StripOutlineOrder);
            CreateRectOutline("B_Top",    new Vector2(-hw, hh),   new Vector2(0f, hh + strip), teamBStripColor, StripOutlineOrder);
            CreateRectOutline("B_Bottom", new Vector2(-hw, -hh - strip), new Vector2(0f, -hh), teamBStripColor, StripOutlineOrder);

            CreateRectOutline("CourtBoundary", new Vector2(-hw, -hh), new Vector2(hw, hh), boundaryColor, BoundaryOrder);
            CreateLine("CenterLine", new Vector2(0f, -hh), new Vector2(0f, hh), centerLineColor, CenterLineOrder);

            // Outer boundary wall — the ball bounces off this (see Ball.ReflectOffBoundaries).
            CreateRectOutline("OuterBoundary", new Vector2(-(hw + strip), -(hh + strip)), new Vector2(hw + strip, hh + strip), boundaryColor, BoundaryOrder);
        }

        private GameObject CreateRectFill(string goName, Vector2 min, Vector2 max, Color color, int sortingOrder)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = CreateMat(color);
            mr.sortingOrder = sortingOrder;
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

        private GameObject CreateRectOutline(string goName, Vector2 min, Vector2 max, Color color, int sortingOrder)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = 4;
            lr.startWidth = lineWidth;
            lr.endWidth = lineWidth;
            lr.material = CreateMat(color);
            lr.startColor = color;
            lr.endColor = color;
            lr.sortingOrder = sortingOrder;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.SetPosition(0, new Vector3(min.x, min.y, 0f));
            lr.SetPosition(1, new Vector3(max.x, min.y, 0f));
            lr.SetPosition(2, new Vector3(max.x, max.y, 0f));
            lr.SetPosition(3, new Vector3(min.x, max.y, 0f));
            return go;
        }

        private GameObject CreateLine(string goName, Vector2 a, Vector2 b, Color color, int sortingOrder)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = false;
            lr.positionCount = 2;
            lr.startWidth = lineWidth;
            lr.endWidth = lineWidth;
            lr.material = CreateMat(color);
            lr.startColor = color;
            lr.endColor = color;
            lr.sortingOrder = sortingOrder;
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
