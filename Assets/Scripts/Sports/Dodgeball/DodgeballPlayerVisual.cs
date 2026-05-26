using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Visual for a Dodgeball player. Lives on the "Visual" child of a player
    /// root (the same child PlayerMovement bobs for the jump arc).
    ///
    /// If a SpriteRenderer is present (e.g. when CourtSetup instantiated a
    /// sprite prefab as the visual), its color is tinted per team. Otherwise
    /// a procedural circle + role mark is built as a fallback.
    ///
    /// When the player is outside their assigned zone, the visual is tinted
    /// with outOfZoneTint to make the violation legible.
    /// </summary>
    public class DodgeballPlayerVisual : MonoBehaviour
    {
        [SerializeField] private float bodyRadius = 0.45f;
        [SerializeField] private Color teamAColor = new Color(0.45f, 0.70f, 1.00f, 1f);
        [SerializeField] private Color teamBColor = new Color(1.00f, 0.55f, 0.55f, 1f);
        [SerializeField] private Color outOfZoneTint = new Color(1f, 0.85f, 0.2f, 1f);

        [Header("Facing arrow")]
        [SerializeField] private Color arrowColor = new Color(1f, 0.95f, 0.3f, 0.95f);
        [Tooltip("How far from the player center the pointer orbits.")]
        [SerializeField] private float arrowDistance = 0.9f;
        [SerializeField] private float arrowSize = 0.35f;
        [SerializeField] private int arrowSortingOrder = 26;

        private Material bodyMaterial;
        private SpriteRenderer spriteRenderer;
        private PlayerZoneTracker tracker;
        private Color baseColor;
        private PlayerMovement movement;
        private Transform arrowTransform;

        public void Configure(Team team, PlayerRole role, PlayerZoneTracker zoneTracker)
        {
            tracker = zoneTracker;
            baseColor = team == Team.A ? teamAColor : teamBColor;

            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                spriteRenderer.color = baseColor;
            }
            else
            {
                BuildBody(baseColor);
                BuildRoleMark(role, baseColor);
            }

            movement = GetComponentInParent<PlayerMovement>();
            BuildFacingArrow();
        }

        private void Update()
        {
            UpdateFacingArrow();

            if (tracker == null) return;
            Color target = tracker.IsInZone ? baseColor : outOfZoneTint;

            if (spriteRenderer != null)
            {
                if (spriteRenderer.color != target) spriteRenderer.color = target;
            }
            else if (bodyMaterial != null)
            {
                if (bodyMaterial.color != target) bodyMaterial.color = target;
            }
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

        private void BuildFacingArrow()
        {
            if (arrowTransform != null) return;

            // Parent to the player root (not this Visual child) so the jump bob
            // and duck squash don't distort the pointer.
            Transform parent = transform.parent != null ? transform.parent : transform;
            var go = new GameObject("FacingArrow");
            go.transform.SetParent(parent, false);

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = CreateMat(arrowColor);
            mr.sortingOrder = arrowSortingOrder;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            float s = arrowSize;
            var verts = new Vector3[]
            {
                new Vector3( s * 0.6f,  0f,       0f),   // tip (+X); rotated to Facing each frame
                new Vector3(-s * 0.4f,  s * 0.5f, 0f),
                new Vector3(-s * 0.4f, -s * 0.5f, 0f),
            };
            var tris = new int[] { 0, 1, 2 };
            var mesh = new Mesh { name = "FacingArrow", vertices = verts, triangles = tris };
            mesh.RecalculateNormals();
            mf.mesh = mesh;

            arrowTransform = go.transform;
        }

        // Orbit the pointer around the player in the current Facing direction.
        private void UpdateFacingArrow()
        {
            if (arrowTransform == null) return;
            if (movement == null) movement = GetComponentInParent<PlayerMovement>();
            if (movement == null) return;

            Vector2 f = movement.Facing;
            if (f.sqrMagnitude < 0.0001f) return;
            float ang = Mathf.Atan2(f.y, f.x) * Mathf.Rad2Deg;
            arrowTransform.localPosition = new Vector3(f.x * arrowDistance, f.y * arrowDistance, 0f);
            arrowTransform.localRotation = Quaternion.Euler(0f, 0f, ang);
        }

        private static Material CreateMat(Color c)
        {
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = c;
            return mat;
        }
    }
}
