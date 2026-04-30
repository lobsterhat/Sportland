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

        [Tooltip("Main body colour. Set per-player to distinguish teams. " +
                 "For offensive (non-defender) players this is overridden each frame by " +
                 "playerControlledColor / aiTeammateColor based on who currently has control.")]
        [SerializeField] private Color bodyColor = new Color(0.2f, 0.45f, 0.9f, 1f);

        [Tooltip("Body colour applied to the offensive player who currently has control.")]
        [SerializeField] private Color playerControlledColor = new Color(0.55f, 0.85f, 1.0f, 1f);

        [Tooltip("Body colour applied to offensive AI teammates of the player-controlled character.")]
        [SerializeField] private Color aiTeammateColor = new Color(0.10f, 0.25f, 0.55f, 1f);

        [Tooltip("Radius of the player circle (world units). Should match CircleCollider2D.")]
        [SerializeField] private float bodyRadius = 0.45f;

        [Tooltip("Colour of the carry-indicator ring shown when this player holds a ball.")]
        [SerializeField] private Color carryIndicatorColor = new Color(0.95f, 0.9f, 0.2f, 0.95f);

        [Tooltip("Colour of the pass-target ring shown when the carrier is aiming at this player.")]
        [SerializeField] private Color passTargetIndicatorColor = new Color(0.25f, 0.95f, 0.35f, 0.95f);

        // ──────────────────────────────────────────────
        //  RUNTIME
        // ──────────────────────────────────────────────

        private DemoballMovementController movement;
        private DemoballInputBroker broker;
        private LineRenderer carryRing;
        private LineRenderer passTargetRing;
        private Material bodyMaterial;

        // ──────────────────────────────────────────────
        //  LIFECYCLE
        // ──────────────────────────────────────────────

        private void Awake()
        {
            movement = GetComponent<DemoballMovementController>();
            broker   = GetComponent<DemoballInputBroker>();
            BuildVisuals();
        }

        private void Update()
        {
            if (movement == null) return;
            if (carryRing != null)      carryRing.enabled      = movement.IsCarryingBall;
            if (passTargetRing != null) passTargetRing.enabled = movement.IsPassTarget;
            UpdateTeamTint();
        }

        // Offensive players are re-tinted each frame: the player-controlled
        // character is light blue and same-team AI players are dark blue.
        // Defenders keep their configured bodyColor untouched.
        private void UpdateTeamTint()
        {
            if (bodyMaterial == null) return;
            if (movement.Role == DemoballRole.Defender)
            {
                if (bodyMaterial.color != bodyColor) bodyMaterial.color = bodyColor;
                return;
            }

            Color target;
            if (broker != null && broker.IsPlayerControlled)
            {
                target = playerControlledColor;
            }
            else if (AnyOffensiveTeammatePlayerControlled())
            {
                target = aiTeammateColor;
            }
            else
            {
                target = bodyColor;
            }

            if (bodyMaterial.color != target) bodyMaterial.color = target;
        }

        private bool AnyOffensiveTeammatePlayerControlled()
        {
            var brokers = DemoballInputBroker.All;
            for (int i = 0; i < brokers.Count; i++)
            {
                var b = brokers[i];
                if (b == null || !b.IsPlayerControlled) continue;
                var m = b.GetComponent<DemoballMovementController>();
                if (m == null || m == movement) continue;
                if (m.Role != DemoballRole.Defender) return true;
            }
            return false;
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
            var bodyRenderer = CreateFilledCircle("Body", bodyRadius, bodyColor, 0f, sortOrder: 20);
            bodyMaterial = bodyRenderer.sharedMaterial;
            carryRing = CreateLineRing("CarryRing", bodyRadius + 0.18f, carryIndicatorColor, 0f, sortOrder: 22);
            carryRing.enabled = false;

            // Pass-target ring sits a bit further out so it stays visible if the
            // same player is somehow both carrying and being aimed at.
            passTargetRing = CreateLineRing("PassTargetRing", bodyRadius + 0.34f,
                                            passTargetIndicatorColor, 0f, sortOrder: 21);
            passTargetRing.enabled = false;
        }

        // ──────────────────────────────────────────────
        //  MESH HELPERS
        // ──────────────────────────────────────────────

        private MeshRenderer CreateFilledCircle(string goName, float radius, Color colour,
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
            return mr;
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
