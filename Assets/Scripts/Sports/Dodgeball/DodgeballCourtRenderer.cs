using System.Collections.Generic;
using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Procedural court for Dodgeball, built through <see cref="CourtProjection"/>
    /// so the floor agrees with where <see cref="DodgeballCourtView"/> puts the
    /// players and the ball.
    ///
    /// Shaped after the Technos Super Dodge Ball courts: a floor that recedes
    /// and narrows slightly toward the back, banded across its depth like mown
    /// turf, with the stands rising behind the far sideline. The backdrop is
    /// doing more work than it looks like it should — a floor alone reads as a
    /// squashed rectangle, and it is the crowd above the horizon that makes the
    /// eye accept it as a floor seen at an angle.
    ///
    /// Everything here is a stand-in for real art. Assign <see cref="floorSprite"/>
    /// or <see cref="backdropSprite"/> and the matching procedural block steps
    /// aside; the floor sprite is UV-mapped across the trapezoid, so a flat
    /// rectangular court texture warps onto the angled floor on its own.
    ///
    /// Geometry is subdivided across depth rather than drawn as single quads,
    /// because the projection's depth curve is not linear — a quad spanning the
    /// whole court would be right at its corners and wrong in the middle.
    /// </summary>
    public class DodgeballCourtRenderer : MonoBehaviour
    {
        [Header("Floor")]
        [SerializeField] private Color teamAHalfColor = new Color(0.22f, 0.55f, 0.28f, 1f);
        [SerializeField] private Color teamBHalfColor = new Color(0.19f, 0.50f, 0.25f, 1f);
        [Tooltip("The surround outside the court lines, where the outfielders stand.")]
        [SerializeField] private Color apronColor = new Color(0.14f, 0.36f, 0.19f, 1f);
        [Tooltip("Depth of one mown band, in metres. Also the subdivision step for every floor mesh.")]
        [SerializeField] private float bandDepth = 0.75f;
        [Tooltip("How strongly alternate bands darken. 0 = a flat surface with no banding.")]
        [Range(0f, 0.5f)] [SerializeField] private float bandShade = 0.11f;

        [Header("Lines")]
        [Tooltip("The court's own boundary. Kept the brightest line on the floor so the court reads as the primary shape.")]
        [SerializeField] private Color boundaryColor = new Color(0.94f, 0.94f, 0.94f, 0.95f);
        [Tooltip("The wall the ball bounces off, out past the outfielders. Dimmer than the court line so the two don't compete.")]
        [SerializeField] private Color outerBoundaryColor = new Color(0.62f, 0.68f, 0.62f, 0.5f);
        [SerializeField] private Color centerLineColor = new Color(0.98f, 0.98f, 0.98f, 1f);
        [SerializeField] private float lineWidth = 0.08f;
        [Tooltip("Outline the outfielder strips. Not part of the arcade look, but the zones are a live rule.")]
        [SerializeField] private bool showZoneStrips = true;
        [SerializeField] private Color teamAStripColor = new Color(0.45f, 0.60f, 0.90f, 0.9f);
        [SerializeField] private Color teamBStripColor = new Color(0.90f, 0.50f, 0.50f, 0.9f);

        [Header("Backdrop (stands behind the far sideline)")]
        [SerializeField] private bool showBackdrop = true;
        [Tooltip("Half-width of the backdrop. Deliberately wider than the court so it overfills the frame.")]
        [SerializeField] private float backdropHalfWidth = 20f;
        [SerializeField] private float kerbHeight = 0.35f;
        [SerializeField] private float hoardingHeight = 0.9f;
        [SerializeField] private float crowdHeight = 1.9f;
        [SerializeField] private float roofHeight = 0.7f;
        [SerializeField] private float skyHeight = 4f;
        [SerializeField] private Color kerbColor = new Color(0.13f, 0.14f, 0.17f, 1f);
        [SerializeField] private Color hoardingColor = new Color(0.85f, 0.68f, 0.16f, 1f);
        [SerializeField] private Color hoardingDividerColor = new Color(0.35f, 0.26f, 0.06f, 1f);
        [SerializeField] private Color standColor = new Color(0.24f, 0.22f, 0.28f, 1f);
        [SerializeField] private Color crowdColorA = new Color(0.72f, 0.34f, 0.28f, 1f);
        [SerializeField] private Color crowdColorB = new Color(0.36f, 0.44f, 0.66f, 1f);
        [SerializeField] private Color roofColor = new Color(0.17f, 0.16f, 0.20f, 1f);
        [SerializeField] private Color skyColor = new Color(0.28f, 0.62f, 0.92f, 1f);

        [Header("Art slots (leave empty for the procedural stand-ins)")]
        [Tooltip("Court surface art. UV-mapped across the projected floor, so author it as a plain rectangle.")]
        [SerializeField] private Sprite floorSprite;
        [Tooltip("Stadium art sitting above the far sideline. Replaces the procedural bands wholesale.")]
        [SerializeField] private Sprite backdropSprite;

        // Sorting, back to front. All well below the 80 that the depth-sorted
        // players and ball bottom out at, so nothing on the court can cover them.
        private const int SkyOrder = -60;
        private const int RoofOrder = -59;
        private const int CrowdBaseOrder = -58;
        private const int CrowdSpeckleOrder = -57;
        private const int HoardingOrder = -56;
        private const int HoardingDividerOrder = -55;
        private const int KerbOrder = -54;
        private const int ApronOrder = -30;
        private const int HalfFillOrder = -28;
        private const int BandOrder = -26;
        private const int StripOutlineOrder = -24;
        private const int BoundaryOrder = -22;
        private const int CenterLineOrder = -20;

        private Transform root;
        private ProjectionState builtWith;
        // Runtime materials are not collected when their renderer goes, and a
        // slider drag rebuilds the court every frame. Share one per colour and
        // keep them across rebuilds.
        private readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
        private readonly Dictionary<Sprite, Material> spriteMaterials = new Dictionary<Sprite, Material>();
        // Destroying a MeshFilter does not destroy the mesh you handed it, so
        // these have to be released by hand or every rebuild leaks a court's
        // worth of geometry.
        private readonly List<Mesh> meshes = new List<Mesh>();

        // The knob values the current geometry was built from. The projection is
        // live-tunable, so the floor has to notice when it goes out of date.
        private struct ProjectionState
        {
            public bool enabled;
            public float farScale, depthSquash, depthBunch;

            public static ProjectionState Current => new ProjectionState
            {
                enabled = CourtProjection.Enabled,
                farScale = CourtProjection.FarScale,
                depthSquash = CourtProjection.DepthSquash,
                depthBunch = CourtProjection.DepthBunch,
            };

            public bool Matches(ProjectionState o) =>
                enabled == o.enabled
                && Mathf.Approximately(farScale, o.farScale)
                && Mathf.Approximately(depthSquash, o.depthSquash)
                && Mathf.Approximately(depthBunch, o.depthBunch);
        }

        // Start, not Awake: CourtSetup configures the projection in its Awake,
        // and Awake order between two components on the same object is not
        // defined. Every Awake has run by the time any Start does.
        private void Start() => Rebuild();

        private void Update()
        {
            var now = ProjectionState.Current;
            if (!builtWith.Matches(now)) Rebuild();
        }

        /// <summary>Throw away the court and rebuild it against the current projection.</summary>
        public void Rebuild()
        {
            builtWith = ProjectionState.Current;

            if (root != null)
            {
                // Destroy is deferred to the end of the frame, so hide the old
                // court now or it draws over the new one for a frame.
                root.gameObject.SetActive(false);
                Destroy(root.gameObject);
                ReleaseMeshes();
            }
            root = new GameObject("Court").transform;
            root.SetParent(transform, false);

            if (showBackdrop) BuildBackdrop();
            BuildFloor();
            BuildLines();
        }

        // ---- floor ----

        private void BuildFloor()
        {
            float hw = CourtSetup.HalfWidth, hh = CourtSetup.HalfHeight;
            float pw = CourtSetup.PlayAreaHalfWidth, ph = CourtSetup.PlayAreaHalfHeight;

            if (floorSprite != null)
            {
                // One textured sheet over the whole play area; the sprite is
                // responsible for its own lines, halves and banding.
                AddMesh("FloorArt", FloorStrip(-pw, pw, -ph, ph), SpriteMat(floorSprite), ApronOrder);
                return;
            }

            AddMesh("Apron", FloorStrip(-pw, pw, -ph, ph), Mat(apronColor), ApronOrder);
            AddMesh("HalfA", FloorStrip(-hw, 0f, -hh, hh), Mat(teamAHalfColor), HalfFillOrder);
            AddMesh("HalfB", FloorStrip(0f, hw, -hh, hh), Mat(teamBHalfColor), HalfFillOrder);
            if (bandShade > 0f) BuildBands();
        }

        // Mown turf: every other depth band across the whole play area, darkened
        // by a translucent overlay so it works over the apron and both halves
        // without needing to know which colour is underneath.
        private void BuildBands()
        {
            float pw = CourtSetup.PlayAreaHalfWidth, ph = CourtSetup.PlayAreaHalfHeight;
            int bands = Mathf.Max(2, Mathf.RoundToInt(ph * 2f / Mathf.Max(0.05f, bandDepth)));

            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i < bands; i += 2)
            {
                float y0 = Mathf.Lerp(-ph, ph, i / (float)bands);
                float y1 = Mathf.Lerp(-ph, ph, (i + 1) / (float)bands);
                AppendQuad(verts, tris, P(-pw, y0), P(pw, y0), P(pw, y1), P(-pw, y1));
            }

            var mesh = new Mesh { name = "FloorBands" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            AddMesh("FloorBands", mesh, Mat(new Color(0f, 0f, 0f, bandShade)), BandOrder);
        }

        /// <summary>
        /// A floor rectangle in sim metres, subdivided across depth and pushed
        /// through the projection. UVs run 0..1 across width and near-to-far in
        /// depth so a court texture maps onto the trapezoid.
        /// </summary>
        private Mesh FloorStrip(float x0, float x1, float y0, float y1)
        {
            int rows = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(y1 - y0) / Mathf.Max(0.05f, bandDepth)));
            var verts = new Vector3[(rows + 1) * 2];
            var uvs = new Vector2[(rows + 1) * 2];
            var tris = new int[rows * 6];

            for (int r = 0; r <= rows; r++)
            {
                float t = r / (float)rows;
                float y = Mathf.Lerp(y0, y1, t);
                verts[r * 2] = P(x0, y);
                verts[r * 2 + 1] = P(x1, y);
                uvs[r * 2] = new Vector2(0f, t);
                uvs[r * 2 + 1] = new Vector2(1f, t);
            }
            for (int r = 0; r < rows; r++)
            {
                int b = r * 2, i = r * 6;
                tris[i] = b; tris[i + 1] = b + 2; tris[i + 2] = b + 1;
                tris[i + 3] = b + 1; tris[i + 4] = b + 2; tris[i + 5] = b + 3;
            }

            var mesh = new Mesh { name = "FloorStrip", vertices = verts, uv = uvs, triangles = tris };
            mesh.RecalculateNormals();
            return mesh;
        }

        // ---- lines ----

        private void BuildLines()
        {
            float hw = CourtSetup.HalfWidth, hh = CourtSetup.HalfHeight;
            float pw = CourtSetup.PlayAreaHalfWidth, ph = CourtSetup.PlayAreaHalfHeight;

            if (showZoneStrips)
            {
                Outline("A_Back", hw, pw, -ph, ph, teamAStripColor, StripOutlineOrder);
                Outline("A_Top", 0f, hw, hh, ph, teamAStripColor, StripOutlineOrder);
                Outline("A_Bottom", 0f, hw, -ph, -hh, teamAStripColor, StripOutlineOrder);
                Outline("B_Back", -pw, -hw, -ph, ph, teamBStripColor, StripOutlineOrder);
                Outline("B_Top", -hw, 0f, hh, ph, teamBStripColor, StripOutlineOrder);
                Outline("B_Bottom", -hw, 0f, -ph, -hh, teamBStripColor, StripOutlineOrder);
            }

            // The wall the ball bounces off — see Ball.ReflectOffBoundaries.
            Outline("OuterBoundary", -pw, pw, -ph, ph, outerBoundaryColor, StripOutlineOrder);
            Outline("CourtBoundary", -hw, hw, -hh, hh, boundaryColor, BoundaryOrder);
            Line("CenterLine", DepthRun(0f, -hh, hh), centerLineColor, CenterLineOrder, false);
        }

        private void Outline(string name, float x0, float x1, float y0, float y1, Color color, int order)
        {
            // Sides run along depth, so they bend with the projection and need
            // sampling; the near and far edges sit at a single depth and stay
            // straight, so their endpoints are enough.
            var pts = new List<Vector3>();
            pts.AddRange(DepthRun(x0, y0, y1));
            pts.AddRange(DepthRun(x1, y1, y0));
            Line(name, pts, color, order, true);
        }

        // Sample a line of constant X across a depth span. Convergence is linear
        // in depth but the depth axis itself is not, so a straight sim line is a
        // gentle curve on screen.
        private List<Vector3> DepthRun(float x, float y0, float y1)
        {
            int steps = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(y1 - y0) / 1.5f));
            var pts = new List<Vector3>(steps + 1);
            for (int i = 0; i <= steps; i++)
                pts.Add(P(x, Mathf.Lerp(y0, y1, i / (float)steps)));
            return pts;
        }

        // ---- backdrop ----

        private void BuildBackdrop()
        {
            float y = CourtProjection.FarScreenY;
            float hwd = backdropHalfWidth;

            if (backdropSprite != null)
            {
                float h = kerbHeight + hoardingHeight + crowdHeight + roofHeight + skyHeight;
                AddMesh("BackdropArt", ScreenQuad(-hwd, hwd, y, y + h), SpriteMat(backdropSprite), SkyOrder);
                return;
            }

            // Stacked upward from the floor's far edge: the fence and hoarding
            // the players stand in front of, then the crowd, the roof, the sky.
            float kerbTop = y + kerbHeight;
            float hoardingTop = kerbTop + hoardingHeight;
            float crowdTop = hoardingTop + crowdHeight;
            float roofTop = crowdTop + roofHeight;

            Band("Sky", -hwd, hwd, roofTop, roofTop + skyHeight, skyColor, SkyOrder);
            Band("StandRoof", -hwd, hwd, crowdTop, roofTop, roofColor, RoofOrder);
            Band("CrowdBase", -hwd, hwd, hoardingTop, crowdTop, standColor, CrowdBaseOrder);
            BuildCrowd(-hwd, hwd, hoardingTop, crowdTop);
            Band("Hoarding", -hwd, hwd, kerbTop, hoardingTop, hoardingColor, HoardingOrder);
            BuildHoardingDividers(-hwd, hwd, kerbTop, hoardingTop);
            Band("Kerb", -hwd, hwd, y, kerbTop, kerbColor, KerbOrder);
        }

        // A speckled block of spectators. Two alternating colours, each gathered
        // into a single mesh so the whole crowd costs two draws.
        private void BuildCrowd(float x0, float x1, float y0, float y1)
        {
            const int rows = 4;
            float cell = 0.34f;
            int cols = Mathf.Max(1, Mathf.RoundToInt((x1 - x0) / cell));
            float w = (x1 - x0) / cols;
            float h = (y1 - y0) / rows;
            float padX = w * 0.18f, padY = h * 0.22f;

            var vertsA = new List<Vector3>(); var trisA = new List<int>();
            var vertsB = new List<Vector3>(); var trisB = new List<int>();

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    // Offset alternate rows so the crowd doesn't read as a grid.
                    float ox = (r % 2 == 0) ? 0f : w * 0.5f;
                    float left = x0 + c * w + ox + padX;
                    float right = left + w - padX * 2f;
                    if (right > x1) continue;
                    float bottom = y0 + r * h + padY;
                    float top = bottom + h - padY * 2f;

                    bool a = ((r + c) & 1) == 0;
                    var verts = a ? vertsA : vertsB;
                    var tris = a ? trisA : trisB;
                    AppendQuad(verts, tris,
                        new Vector3(left, bottom, 0f), new Vector3(right, bottom, 0f),
                        new Vector3(right, top, 0f), new Vector3(left, top, 0f));
                }
            }

            AddMesh("CrowdA", MeshOf("CrowdA", vertsA, trisA), Mat(crowdColorA), CrowdSpeckleOrder);
            AddMesh("CrowdB", MeshOf("CrowdB", vertsB, trisB), Mat(crowdColorB), CrowdSpeckleOrder);
        }

        // Panel seams along the advertising hoarding — cheap, but they give the
        // band a sense of running away to either side.
        private void BuildHoardingDividers(float x0, float x1, float y0, float y1)
        {
            const float spacing = 3.2f;
            const float thickness = 0.09f;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (float x = x0 + spacing; x < x1; x += spacing)
            {
                AppendQuad(verts, tris,
                    new Vector3(x, y0, 0f), new Vector3(x + thickness, y0, 0f),
                    new Vector3(x + thickness, y1, 0f), new Vector3(x, y1, 0f));
            }
            AddMesh("HoardingDividers", MeshOf("HoardingDividers", verts, tris),
                    Mat(hoardingDividerColor), HoardingDividerOrder);
        }

        // ---- primitives ----

        // Backdrop pieces are already in screen space — they sit above the
        // horizon where there is no floor to project onto.
        private void Band(string name, float x0, float x1, float y0, float y1, Color color, int order)
            => AddMesh(name, ScreenQuad(x0, x1, y0, y1), Mat(color), order);

        private static Mesh ScreenQuad(float x0, float x1, float y0, float y1)
        {
            var mesh = new Mesh
            {
                name = "Quad",
                vertices = new[]
                {
                    new Vector3(x0, y0, 0f), new Vector3(x1, y0, 0f),
                    new Vector3(x1, y1, 0f), new Vector3(x0, y1, 0f),
                },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
                triangles = new[] { 0, 2, 1, 0, 3, 2 },
            };
            mesh.RecalculateNormals();
            return mesh;
        }

        private static void AppendQuad(List<Vector3> verts, List<int> tris,
                                       Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
            tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
        }

        private static Mesh MeshOf(string name, List<Vector3> verts, List<int> tris)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            return mesh;
        }

        private GameObject AddMesh(string name, Mesh mesh, Material material, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().mesh = mesh;
            meshes.Add(mesh);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.sortingOrder = order;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        private void Line(string name, List<Vector3> points, Color color, int order, bool loop)
        {
            if (points.Count < 2) return;
            var go = new GameObject(name);
            go.transform.SetParent(root, false);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = loop;
            lr.positionCount = points.Count;
            lr.SetPositions(points.ToArray());
            lr.startWidth = lr.endWidth = lineWidth;
            lr.material = Mat(color);
            lr.startColor = lr.endColor = color;
            lr.sortingOrder = order;
            lr.numCornerVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
        }

        private static Vector3 P(float worldX, float worldY)
        {
            Vector2 g = CourtProjection.Ground(worldX, worldY);
            return new Vector3(g.x, g.y, 0f);
        }

        private Material Mat(Color c)
        {
            if (materials.TryGetValue(c, out Material cached) && cached != null) return cached;
            var mat = new Material(Shader.Find("Sprites/Default")) { color = c };
            materials[c] = mat;
            return mat;
        }

        private Material SpriteMat(Sprite sprite)
        {
            if (spriteMaterials.TryGetValue(sprite, out Material cached) && cached != null) return cached;
            var mat = new Material(Shader.Find("Sprites/Default")) { color = Color.white };
            mat.mainTexture = sprite.texture;
            spriteMaterials[sprite] = mat;
            return mat;
        }

        private void ReleaseMeshes()
        {
            for (int i = 0; i < meshes.Count; i++)
                if (meshes[i] != null) Destroy(meshes[i]);
            meshes.Clear();
        }

        private void OnDestroy()
        {
            foreach (var mat in materials.Values)
                if (mat != null) Destroy(mat);
            foreach (var mat in spriteMaterials.Values)
                if (mat != null) Destroy(mat);
            materials.Clear();
            spriteMaterials.Clear();
            ReleaseMeshes();
        }
    }
}
