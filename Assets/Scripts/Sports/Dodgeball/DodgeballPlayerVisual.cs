using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Procedural sprite for a Dodgeball player. Attached to the "Visual" child
    /// of a player root (the same child PlayerMovement bobs for the jump arc).
    ///
    /// Body color = team. A small inner dot marks role (filled = infielder,
    /// outline only = outfielder). Optional out-of-zone tint when the tracker
    /// reports the player is outside their assigned zone.
    /// </summary>
    public class DodgeballPlayerVisual : MonoBehaviour
    {
        [SerializeField] private float bodyRadius = 0.45f;
        [SerializeField] private Color teamAColor = new Color(0.45f, 0.70f, 1.00f, 1f);
        [SerializeField] private Color teamBColor = new Color(1.00f, 0.55f, 0.55f, 1f);
        [SerializeField] private Color outOfZoneTint = new Color(1f, 0.85f, 0.2f, 1f);

        private Material bodyMaterial;
        private PlayerZoneTracker tracker;
        private Color baseColor;

        public void Configure(Team team, PlayerRole role, PlayerZoneTracker zoneTracker)
        {
            tracker = zoneTracker;
            baseColor = team == Team.A ? teamAColor : teamBColor;

            BuildBody(baseColor);
            BuildRoleMark(role, baseColor);
        }

        private void Update()
        {
            if (tracker == null || bodyMaterial == null) return;
            Color target = tracker.IsInZone ? baseColor : outOfZoneTint;
            if (bodyMaterial.color != target) bodyMaterial.color = target;
        }

        private void BuildBody(Color color)
        {
            var go = new GameObject("Body");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, 0f);

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = CreateMat(color);
            mr.sortingOrder = 20;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            bodyMaterial = mr.sharedMaterial;

            const int segs = 32;
            var verts = new Vector3[segs + 1];
            var tris = new int[segs * 3];
            verts[0] = Vector3.zero;
            for (int i = 0; i < segs; i++)
            {
                float a = i * Mathf.PI * 2f / segs;
                verts[i + 1] = new Vector3(Mathf.Cos(a) * bodyRadius, Mathf.Sin(a) * bodyRadius, 0f);
                tris[i * 3]     = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = (i + 1) % segs + 1;
            }
            var mesh = new Mesh { name = "Body", vertices = verts, triangles = tris };
            mesh.RecalculateNormals();
            mf.mesh = mesh;
        }

        private void BuildRoleMark(PlayerRole role, Color teamColor)
        {
            var go = new GameObject("RoleMark");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, -0.05f);

            const int segs = 20;
            float r = bodyRadius * 0.45f;
            Color markColor = role == PlayerRole.Infielder
                ? new Color(1f, 1f, 1f, 1f)
                : new Color(teamColor.r * 0.35f, teamColor.g * 0.35f, teamColor.b * 0.35f, 1f);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = segs;
            lr.startWidth = 0.05f;
            lr.endWidth = 0.05f;
            lr.material = CreateMat(markColor);
            lr.startColor = markColor;
            lr.endColor = markColor;
            lr.sortingOrder = 22;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;

            for (int i = 0; i < segs; i++)
            {
                float a = i * Mathf.PI * 2f / segs;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
            }
        }

        private static Material CreateMat(Color c)
        {
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = c;
            return mat;
        }
    }
}
