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

        [Header("Movement arrow (yellow — controlled player, hides when stationary)")]
        [SerializeField] private Color movementArrowColor = new Color(1f, 0.95f, 0.3f, 0.95f);
        [Tooltip("How far from the player center the yellow movement pointer orbits.")]
        [SerializeField] private float movementArrowDistance = 0.9f;
        [SerializeField] private float movementArrowSize = 0.35f;
        [SerializeField] private int movementArrowSortingOrder = 26;
        [Tooltip("Hide the yellow movement arrow when below this speed (u/s).")]
        [SerializeField] private float movementArrowMinSpeed = 0.4f;

        [Header("Facing arrow (red — every player, always visible)")]
        [SerializeField] private Color facingArrowColor = new Color(1f, 0.35f, 0.35f, 0.95f);
        [Tooltip("How far from the player center the red facing pointer orbits.")]
        [SerializeField] private float facingArrowDistance = 0.6f;
        [SerializeField] private float facingArrowSize = 0.28f;
        [SerializeField] private int facingArrowSortingOrder = 27;

        [Header("Sprite facing flip")]
        [Tooltip("Mirror the SpriteRenderer horizontally based on Facing.x so the player looks the way they're facing. Only affects sprite visuals (not the procedural fallback).")]
        [SerializeField] private bool flipSpriteByFacing = true;
        [Tooltip("True if the sprite art is drawn facing right by default (+X). False if the source sprite faces left — flips the polarity of the mirror.")]
        [SerializeField] private bool spriteFacesRightAtDefault = true;
        [Tooltip("Deadband on Facing.x. While |Facing.x| is below this, the sprite keeps its current flip so straight up/down looking doesn't jitter.")]
        [SerializeField] private float flipDeadband = 0.1f;

        [Header("Controlled-player ring")]
        [SerializeField] private Color ringColor = new Color(1f, 0.9f, 0.15f, 0.95f);
        [Tooltip("Ground-ring half-extents (x wide, y flat) so it reads as lying on the floor.")]
        [SerializeField] private float ringRadiusX = 0.6f;
        [SerializeField] private float ringRadiusY = 0.24f;
        [SerializeField] private float ringWidth = 0.06f;
        [Tooltip("Below the player sprite (order 0) but above the court (center line -2) so it sits under the player.")]
        [SerializeField] private int ringSortingOrder = -1;

        [Header("Ground shadow")]
        [Tooltip("Flat ellipse at the feet (root-parented, so it stays on the floor while the sprite bobs). Grounds the side-view character and sells the jump height.")]
        [SerializeField] private float shadowRadiusX = 0.42f;
        [SerializeField] private float shadowRadiusY = 0.16f;
        [SerializeField] private Color shadowColor = new Color(0f, 0f, 0f, 0.45f);
        [Tooltip("Bottom layer of the player (under body/role/arrows) within its depth-sort group.")]
        [SerializeField] private int shadowSortingOrder = -10;
        [Tooltip("Jump height (u) at which the shadow shrinks to shadowMinScale, selling the lift.")]
        [SerializeField] private float shadowFalloffHeight = 1.5f;
        [Range(0f, 1f)] [SerializeField] private float shadowMinScale = 0.5f;

        private Material bodyMaterial;
        private SpriteRenderer spriteRenderer;
        private PlayerZoneTracker tracker;
        private Color baseColor;
        private PlayerMovement movement;
        private Transform movementArrowTransform;
        private Transform facingArrowTransform;
        private Transform ringTransform;
        private Transform shadowTransform;
        private float shadowFootY;

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
            BuildMovementArrow();
            BuildFacingArrow();
            BuildControlRing();
            BuildShadow();
        }

        private void Update()
        {
            UpdateControlIndicators();
            UpdateMovementArrow();
            UpdateFacingArrow();
            UpdateSpriteFacing();
            UpdateShadow();

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

        private void BuildMovementArrow()
        {
            if (movementArrowTransform != null) return;

            // Parent to the player root (not this Visual child) so the jump bob
            // and duck squash don't distort the pointer.
            Transform parent = transform.parent != null ? transform.parent : transform;
            movementArrowTransform = BuildArrow(parent, "MovementArrow",
                movementArrowColor, movementArrowSize, movementArrowSortingOrder);
            movementArrowTransform.gameObject.SetActive(false);   // shown only while controlled + moving
        }

        private void BuildFacingArrow()
        {
            if (facingArrowTransform != null) return;
            Transform parent = transform.parent != null ? transform.parent : transform;
            facingArrowTransform = BuildArrow(parent, "FacingArrow",
                facingArrowColor, facingArrowSize, facingArrowSortingOrder);
            // The red facing arrow is always visible (every player).
        }

        private static Transform BuildArrow(Transform parent, string name, Color color, float size, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = CreateMat(color);
            mr.sortingOrder = sortingOrder;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            float s = size;
            var verts = new Vector3[]
            {
                new Vector3( s * 0.6f,  0f,       0f),   // tip (+X); rotated each frame
                new Vector3(-s * 0.4f,  s * 0.5f, 0f),
                new Vector3(-s * 0.4f, -s * 0.5f, 0f),
            };
            var tris = new int[] { 0, 1, 2 };
            var mesh = new Mesh { name = name, vertices = verts, triangles = tris };
            mesh.RecalculateNormals();
            mf.mesh = mesh;

            return go.transform;
        }

        // Orbit the yellow movement arrow in the direction the player is actually
        // moving (rb velocity), only for the controlled player, and hide it when
        // they're essentially stationary.
        private void UpdateMovementArrow()
        {
            if (movementArrowTransform == null) return;
            if (movement == null) movement = GetComponentInParent<PlayerMovement>();
            if (movement == null) return;

            bool controlled = movement.GetComponent<DodgeballPlayerInput>() != null;
            Vector2 v = movement.Velocity;
            bool show = controlled && v.magnitude >= movementArrowMinSpeed;
            if (movementArrowTransform.gameObject.activeSelf != show)
                movementArrowTransform.gameObject.SetActive(show);
            if (!show) return;

            Vector2 n = v.normalized;
            float ang = Mathf.Atan2(n.y, n.x) * Mathf.Rad2Deg;
            movementArrowTransform.localPosition = new Vector3(n.x * movementArrowDistance, n.y * movementArrowDistance, 0f);
            movementArrowTransform.localRotation = Quaternion.Euler(0f, 0f, ang);
        }

        // Orbit the red facing arrow in the player's Facing direction. Always on
        // (every player), so you can see where each player is looking — useful for
        // reading defensive stance / set-and-watch-the-ball behaviour.
        private void UpdateFacingArrow()
        {
            if (facingArrowTransform == null) return;
            if (movement == null) movement = GetComponentInParent<PlayerMovement>();
            if (movement == null) return;

            Vector2 f = movement.Facing;
            if (f.sqrMagnitude < 0.0001f) return;
            float ang = Mathf.Atan2(f.y, f.x) * Mathf.Rad2Deg;
            facingArrowTransform.localPosition = new Vector3(f.x * facingArrowDistance, f.y * facingArrowDistance, 0f);
            facingArrowTransform.localRotation = Quaternion.Euler(0f, 0f, ang);
        }

        // Mirror the sprite horizontally to match the player's Facing direction.
        // Sprite-only (the procedural fallback is symmetric, so flipping it does
        // nothing useful). A small deadband around Facing.x = 0 keeps the sprite
        // from flickering when the player is looking straight up or down.
        private void UpdateSpriteFacing()
        {
            if (!flipSpriteByFacing || spriteRenderer == null) return;
            if (movement == null) movement = GetComponentInParent<PlayerMovement>();
            if (movement == null) return;

            float fx = movement.Facing.x;
            if (Mathf.Abs(fx) < flipDeadband) return;   // straight up/down — keep current flip

            bool wantFacingLeft = fx < 0f;
            bool flip = spriteFacesRightAtDefault ? wantFacingLeft : !wantFacingLeft;
            if (spriteRenderer.flipX != flip) spriteRenderer.flipX = flip;
        }

        // A flat yellow ring at the player's feet marking the human-controlled
        // player. Ground-fixed (parented to the root, not the bobbing Visual).
        private void BuildControlRing()
        {
            if (ringTransform != null) return;

            Transform parent = transform.parent != null ? transform.parent : transform;
            var go = new GameObject("ControlRing");
            go.transform.SetParent(parent, false);
            float footY = movement != null ? movement.FootOffset : -0.79f;
            go.transform.localPosition = new Vector3(0f, footY, 0f);

            const int segs = 28;
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = segs;
            lr.startWidth = ringWidth;
            lr.endWidth = ringWidth;
            lr.material = CreateMat(ringColor);
            lr.startColor = ringColor;
            lr.endColor = ringColor;
            lr.sortingOrder = ringSortingOrder;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            for (int i = 0; i < segs; i++)
            {
                float a = i * Mathf.PI * 2f / segs;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a) * ringRadiusX, Mathf.Sin(a) * ringRadiusY, 0f));
            }

            go.SetActive(false);   // shown only while controlled
            ringTransform = go.transform;
        }

        // The ring marks the human-controlled player; show it only on whoever
        // currently holds a DodgeballPlayerInput. (The yellow movement arrow has
        // its own controlled+moving gating in UpdateMovementArrow; the red facing
        // arrow is always on.)
        private void UpdateControlIndicators()
        {
            if (movement == null) movement = GetComponentInParent<PlayerMovement>();
            bool controlled = movement != null && movement.GetComponent<DodgeballPlayerInput>() != null;
            if (ringTransform != null && ringTransform.gameObject.activeSelf != controlled)
                ringTransform.gameObject.SetActive(controlled);
        }

        // A flat dark ellipse at the feet. Parented to the root (not the bobbing
        // Visual) so it stays on the floor as the player jumps; the depth-sort
        // group's low order keeps it under the character. UpdateShadow shrinks it
        // with jump height to sell the lift.
        private void BuildShadow()
        {
            if (shadowTransform != null) return;

            Transform parent = transform.parent != null ? transform.parent : transform;
            var go = new GameObject("Shadow");
            go.transform.SetParent(parent, false);
            shadowFootY = movement != null ? movement.FootOffset : -0.79f;
            go.transform.localPosition = new Vector3(0f, shadowFootY, 0.01f);

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = CreateMat(shadowColor);
            mr.sortingOrder = shadowSortingOrder;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            const int segs = 24;
            var verts = new Vector3[segs + 1];
            var tris = new int[segs * 3];
            verts[0] = Vector3.zero;
            for (int i = 0; i < segs; i++)
            {
                float a = i * Mathf.PI * 2f / segs;
                verts[i + 1] = new Vector3(Mathf.Cos(a) * shadowRadiusX, Mathf.Sin(a) * shadowRadiusY, 0f);
                tris[i * 3]     = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = (i + 1) % segs + 1;
            }
            var mesh = new Mesh { name = "PlayerShadow", vertices = verts, triangles = tris };
            mesh.RecalculateNormals();
            mf.mesh = mesh;

            shadowTransform = go.transform;
        }

        private void UpdateShadow()
        {
            if (shadowTransform == null) return;
            if (movement == null) movement = GetComponentInParent<PlayerMovement>();

            float h = movement != null ? movement.CurrentJumpHeight : 0f;
            float t = shadowFalloffHeight > 0f ? Mathf.Clamp01(h / shadowFalloffHeight) : 0f;
            float s = Mathf.Lerp(1f, shadowMinScale, t);
            // Re-anchor to the foot every frame (flat view). The oblique view
            // overrides this in LateUpdate while active; resetting here means it
            // snaps back cleanly when the oblique view is toggled off.
            shadowTransform.localPosition = new Vector3(0f, shadowFootY, 0.01f);
            shadowTransform.localScale = new Vector3(s, s, 1f);
        }

        private static Material CreateMat(Color c)
        {
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = c;
            return mat;
        }
    }
}
