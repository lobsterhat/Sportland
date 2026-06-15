using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// SPIKE — real(ish) 3/4 perspective view, toggled live (Q, via CourtSetup).
    /// Draws the court as a RECEDING TRAPEZOID (near/bottom edge wide, far/top
    /// edge narrow) and projects players + ball onto it: deeper = higher on
    /// screen, narrower, and smaller. The sim is untouched — only the visual
    /// transforms move, every frame, and everything reverts when disabled.
    ///
    /// On enable it hides the flat court visual + the per-player debug arrows/
    /// ring (which are top-down aids) and shows the procedural perspective floor.
    ///
    /// NOTE: this reintroduces the top-vs-bottom spacing non-uniformity that a
    /// flat field avoids — that's inherent to perspective. It's here so the 3/4
    /// look can be seen and the tradeoff judged by eye.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class DodgeballObliqueView : MonoBehaviour
    {
        [Header("Perspective")]
        [Tooltip("Scale of the FAR (top) edge vs the near (bottom) edge. <1 narrows + shrinks with depth. 0.5 = far edge half size.")]
        [Range(0.2f, 1f)] public float farScale = 0.5f;
        [Tooltip("Vertical bunching toward the far edge. 1 = even (flat oblique); >1 = rows bunch toward the top (stronger perspective).")]
        [Range(1f, 3f)] public float depthBunch = 1.6f;
        [Tooltip("Multiplier on jump / ball-arc height so hops pop.")]
        [Range(1f, 3f)] public float heightExaggeration = 1.4f;
        [Tooltip("Procedural floor fill color.")]
        public Color floorColor = new Color(0.18f, 0.34f, 0.22f, 1f);
        [Tooltip("Floor line color (boundary, center, depth lines).")]
        public Color floorLineColor = new Color(0.85f, 0.9f, 0.85f, 0.9f);

        // Depth range projected (world Y), covering the play area incl. outfielders.
        private static readonly float YNear = -CourtSetup.PlayAreaHalfHeight; // bottom (near)
        private static readonly float YFar  =  CourtSetup.PlayAreaHalfHeight;  // top (far)

        private Ball ball;
        private Transform flatCourt, flatLine;
        private GameObject floor;

        private void OnEnable()
        {
            CacheFlatCourt();
            if (flatCourt != null) flatCourt.gameObject.SetActive(false);
            if (flatLine != null) flatLine.gameObject.SetActive(false);
            SetDebugOverlays(false);
            RebuildFloor();   // rebuild with current params (they may have changed since last enable)
        }

        private void RebuildFloor()
        {
            if (floor != null) Destroy(floor);
            BuildFloor();
            floor.SetActive(true);
        }

        private void OnDisable()
        {
            if (flatCourt != null) flatCourt.gameObject.SetActive(true);
            if (flatLine != null) flatLine.gameObject.SetActive(true);
            SetDebugOverlays(true);
            if (floor != null) floor.SetActive(false);
        }

        private void CacheFlatCourt()
        {
            if (flatCourt == null) flatCourt = transform.Find("Court");
            if (flatLine == null) flatLine = transform.Find("CenterLine");
        }

        // Hide/show the top-down debug aids (arrows + control ring) on every
        // player while in perspective — they're pinned to the flat ground and
        // would float wrong otherwise.
        private void SetDebugOverlays(bool on)
        {
            var all = PlayerZoneTracker.All;
            for (int i = 0; i < all.Count; i++)
            {
                var tr = all[i];
                if (tr == null) continue;
                ToggleChild(tr.transform, "MovementArrow", on);
                ToggleChild(tr.transform, "FacingArrow", on);
                ToggleChild(tr.transform, "ControlRing", on);
            }
        }

        private static void ToggleChild(Transform root, string name, bool on)
        {
            var c = root.Find(name);
            if (c != null && c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
        }

        // World ground point → screen position + depth scale.
        private void Project(float wx, float wy, out float sx, out float sy, out float scale)
        {
            float t = Mathf.Clamp01((wy - YNear) / (YFar - YNear));   // 0 near .. 1 far
            scale = Mathf.Lerp(1f, farScale, t);
            sx = wx * scale;
            float tB = 1f - Mathf.Pow(1f - t, depthBunch);            // bunch far rows
            sy = Mathf.Lerp(YNear, YFar, tB);
        }

        private void LateUpdate()
        {
            var all = PlayerZoneTracker.All;
            for (int i = 0; i < all.Count; i++)
            {
                var tr = all[i];
                if (tr == null || tr.transform.childCount == 0) continue;
                ProjectPlayer(tr.transform);
            }

            if (ball == null) ball = FindAnyObjectByType<Ball>();
            if (ball != null) ProjectBall(ball);
        }

        private void ProjectPlayer(Transform root)
        {
            Vector3 wp = root.position;
            Project(wp.x, wp.y, out float sx, out float sy, out float scale);

            var mv = root.GetComponent<PlayerMovement>();
            float foot = mv != null ? -mv.FootOffset : 0.79f;          // sprite center sits this far above feet
            float jump = mv != null ? mv.CurrentJumpHeight : 0f;
            float centerY = sy + foot * scale + jump * heightExaggeration * scale;

            var visual = root.GetChild(0);                              // child[0] = Visual
            visual.position = new Vector3(sx, centerY, visual.position.z);
            visual.localScale = new Vector3(scale, scale, 1f);

            var shadow = root.Find("Shadow");
            if (shadow != null)
            {
                shadow.position = new Vector3(sx, sy, shadow.position.z);
                shadow.localScale = new Vector3(scale, scale, 1f);
            }
        }

        private void ProjectBall(Ball b)
        {
            Vector3 wp = b.transform.position;
            Project(wp.x, wp.y, out float sx, out float sy, out float scale);
            float lift = b.Height * heightExaggeration * scale;

            var visual = b.transform.Find("Visual");
            if (visual != null)
            {
                visual.position = new Vector3(sx, sy + lift, visual.position.z);
                visual.localScale = new Vector3(scale, scale, 1f);
            }
            var shadow = b.transform.Find("Shadow");
            if (shadow != null)
            {
                shadow.position = new Vector3(sx, sy, shadow.position.z);
                shadow.localScale = new Vector3(scale, scale, 1f);
            }
        }

        // ---- procedural perspective floor (built once) ----
        private void BuildFloor()
        {
            floor = new GameObject("ObliqueFloor");
            floor.transform.SetParent(transform, false);

            // Court corners (±HalfWidth x, ±HalfHeight y), projected.
            float hw = CourtSetup.HalfWidth, hh = CourtSetup.HalfHeight;
            Vector3 nl = P(-hw, -hh), nr = P(hw, -hh);   // near (bottom) edge
            Vector3 fl = P(-hw,  hh), fr = P(hw,  hh);   // far (top) edge

            // Filled trapezoid (2 triangles): nl, nr, fr, fl.
            var go = new GameObject("Fill");
            go.transform.SetParent(floor.transform, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Mat(floorColor);
            mr.sortingOrder = -50;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mf.mesh = new Mesh
            {
                name = "ObliqueFloorFill",
                vertices = new[] { nl, nr, fr, fl },
                triangles = new[] { 0, 1, 2, 0, 2, 3 },
            };
            mf.mesh.RecalculateNormals();

            // Boundary outline + center line + a few depth lines (bunch toward top).
            Line("Boundary", new[] { nl, nr, fr, fl, nl });
            Line("Center", new[] { P(0f, -hh), P(0f, hh) });
            for (int i = 1; i <= 3; i++)
            {
                float y = Mathf.Lerp(-hh, hh, i / 4f);
                Line($"Depth{i}", new[] { P(-hw, y), P(hw, y) });
            }
        }

        private Vector3 P(float wx, float wy)
        {
            Project(wx, wy, out float sx, out float sy, out _);
            return new Vector3(sx, sy, 0f);
        }

        private void Line(string name, Vector3[] pts)
        {
            var go = new GameObject(name);
            go.transform.SetParent(floor.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = false;
            lr.positionCount = pts.Length;
            lr.SetPositions(pts);
            lr.startWidth = lr.endWidth = 0.08f;
            lr.material = Mat(floorLineColor);
            lr.startColor = lr.endColor = floorLineColor;
            lr.sortingOrder = -49;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
        }

        private static Material Mat(Color c) => new Material(Shader.Find("Sprites/Default")) { color = c };
    }
}
