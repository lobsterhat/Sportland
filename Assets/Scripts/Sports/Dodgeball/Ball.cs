using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Dodgeball with a real (top-down) Height dimension.
    ///
    /// The root transform's XY is the "ground" position used for pickups and
    /// collision. A "Visual" child is offset along Y by Height each frame, and
    /// a "Shadow" child stays grounded but shrinks as Height grows. Both
    /// children are pre-built by Dodgeball.prefab; procedural fallbacks build
    /// them at runtime if the ball is spawned without the prefab.
    ///
    /// Trajectory state:
    ///   - Carried (carrier != null): ground tracks carrier; Height = carryHeight.
    ///   - Passing (passActive):       parametric lerp to a fixed target; lob
    ///                                 adds a sin-arc to carryHeight, chest
    ///                                 stays flat at carryHeight.
    ///   - Loose:                      velocity-driven; Height stays at
    ///                                 carryHeight while in motion, drops to
    ///                                 0 once nearly stopped.
    ///
    /// Pickups use 2D distance against PlayerZoneTracker.All and require
    /// CanCatchBall() to encode the "no catches in restricted areas" rule.
    /// The thrower / passer is locked out for throwerPickupCooldown seconds
    /// so the ball doesn't snap back on release.
    /// </summary>
    public class Ball : MonoBehaviour
    {
        [Header("Pickup")]
        [SerializeField] private float pickupRadius = 0.6f;
        [SerializeField] private float throwerPickupCooldown = 0.4f;

        [Header("Physics")]
        [SerializeField] private float linearDamping = 0.5f;

        [Header("Height")]
        [SerializeField] private float carryHeight = 0.5f;
        [SerializeField] private float lobApex = 1.2f;

        [Header("Shadow")]
        [Tooltip("Ball Height at which the shadow has fully shrunk to shadowMinScale.")]
        [SerializeField] private float shadowFalloffHeight = 1.5f;
        [SerializeField, Range(0f, 1f)] private float shadowMinScale = 0.4f;

        [Header("Procedural fallback")]
        [SerializeField] private float ballRadius = 0.25f;
        [SerializeField] private Color ballColor = new Color(0.98f, 0.85f, 0.25f, 1f);
        [SerializeField] private float shadowWidth = 0.18f;
        [SerializeField] private float shadowHeight = 0.06f;
        [SerializeField] private Vector2 shadowOffset = new Vector2(0f, -0.08f);
        [SerializeField] private Color shadowColor = new Color(0f, 0f, 0f, 0.4f);

        private Rigidbody2D rb;
        private PlayerZoneTracker carrier;
        private PlayerZoneTracker recentThrower;
        private float throwerCooldownRemaining;

        private Transform visualTransform;
        private Transform shadowTransform;
        private Vector3 shadowBaseScale = Vector3.one;

        // Pass / lob state — parametric drive when active.
        private bool passActive;
        private bool passIsLob;
        private float passTimer;
        private float passDurationCurrent;
        private Vector2 passStart;
        private Vector2 passEnd;

        public PlayerZoneTracker Carrier => carrier;

        /// <summary>Current visual height above the ball's ground (XY) position.</summary>
        public float Height { get; private set; }

        /// <summary>Fires whenever the ball attaches to a player (pickup, pass catch, or ForcePickup).</summary>
        public event System.Action<PlayerZoneTracker> OnAttached;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.linearDamping = linearDamping;
            rb.freezeRotation = true;

            var col = GetComponent<CircleCollider2D>();
            if (col == null)
            {
                col = gameObject.AddComponent<CircleCollider2D>();
                col.radius = ballRadius;
                col.isTrigger = true;
            }

            visualTransform = transform.Find("Visual");
            if (visualTransform == null && GetComponentInChildren<Renderer>() == null)
            {
                BuildVisual();
                visualTransform = transform.Find("Visual");
            }

            shadowTransform = transform.Find("Shadow");
            if (shadowTransform == null)
            {
                BuildShadow();
                shadowTransform = transform.Find("Shadow");
            }
            if (shadowTransform != null) shadowBaseScale = shadowTransform.localScale;
        }

        private void Update()
        {
            if (throwerCooldownRemaining > 0f)
            {
                throwerCooldownRemaining -= Time.deltaTime;
                if (throwerCooldownRemaining <= 0f) recentThrower = null;
            }

            if (carrier != null)
            {
                transform.position = carrier.transform.position;
                Height = carryHeight;
            }
            else if (passActive)
            {
                passTimer += Time.deltaTime;
                float t = Mathf.Clamp01(passTimer / passDurationCurrent);
                transform.position = Vector2.Lerp(passStart, passEnd, t);
                Height = passIsLob
                    ? carryHeight + lobApex * Mathf.Sin(t * Mathf.PI)
                    : carryHeight;
                if (t >= 1f)
                {
                    passActive = false;
                    rb.simulated = true;
                    rb.linearVelocity = Vector2.zero;
                }
                TryPickup();
            }
            else
            {
                bool moving = rb.linearVelocity.sqrMagnitude > 0.01f;
                Height = moving ? carryHeight : 0f;
                TryPickup();
            }

            ApplyVisualHeight();
        }

        private void ApplyVisualHeight()
        {
            if (visualTransform != null)
            {
                var p = visualTransform.localPosition;
                visualTransform.localPosition = new Vector3(p.x, Height, p.z);
            }
            if (shadowTransform != null && shadowFalloffHeight > 0f)
            {
                float t = Mathf.Clamp01(Height / shadowFalloffHeight);
                float scale = Mathf.Lerp(1f, shadowMinScale, t);
                shadowTransform.localScale = new Vector3(
                    shadowBaseScale.x * scale,
                    shadowBaseScale.y * scale,
                    shadowBaseScale.z
                );
            }
        }

        private void TryPickup()
        {
            var trackers = PlayerZoneTracker.All;
            Vector2 ballPos = transform.position;
            for (int i = 0; i < trackers.Count; i++)
            {
                var t = trackers[i];
                if (t == null) continue;
                if (t.HasBall) continue;
                if (t == recentThrower) continue;
                if (!t.CanCatchBall()) continue;

                Vector2 trackerPos = t.transform.position;
                if (Vector2.SqrMagnitude(trackerPos - ballPos) <= pickupRadius * pickupRadius)
                {
                    AttachTo(t);
                    return;
                }
            }
        }

        private void AttachTo(PlayerZoneTracker t)
        {
            passActive = false;
            carrier = t;
            t.HeldBall = this;
            rb.linearVelocity = Vector2.zero;
            rb.simulated = false;
            OnAttached?.Invoke(t);
        }

        /// <summary>
        /// Debug / scripted attach — detaches from the current carrier (if any)
        /// and snaps the ball onto the target, bypassing cooldown and range.
        /// </summary>
        public void ForcePickup(PlayerZoneTracker t)
        {
            if (t == null) return;
            if (carrier != null && carrier != t)
            {
                carrier.HeldBall = null;
                carrier = null;
            }
            recentThrower = null;
            throwerCooldownRemaining = 0f;
            transform.position = t.transform.position;
            AttachTo(t);
        }

        /// <summary>
        /// Velocity-driven release. Used for the Circle throw — direction is a
        /// unit-normalized world vector; power is units/sec.
        /// </summary>
        public void Throw(Vector2 direction, float power)
        {
            if (carrier == null) return;
            if (direction.sqrMagnitude < 0.0001f) direction = Vector2.right;

            recentThrower = carrier;
            throwerCooldownRemaining = throwerPickupCooldown;
            carrier.HeldBall = null;
            carrier = null;

            passActive = false;
            rb.simulated = true;
            rb.linearVelocity = direction.normalized * power;
        }

        /// <summary>
        /// Parametric pass to a fixed world position. Lateral motion is a
        /// straight lerp from current position to target; lob adds a sin-arc
        /// on top of carryHeight, chest stays flat. Catches still resolve via
        /// the normal proximity pickup so anyone on the path can intercept.
        /// </summary>
        public void Pass(Vector2 target, float lateralSpeed, bool isLob)
        {
            if (carrier == null) return;

            Vector2 start = transform.position;
            Vector2 toTarget = target - start;
            float dist = toTarget.magnitude;
            if (dist < 0.01f) return;

            recentThrower = carrier;
            throwerCooldownRemaining = throwerPickupCooldown;
            carrier.HeldBall = null;
            carrier = null;

            passStart = start;
            passEnd = target;
            passDurationCurrent = dist / Mathf.Max(0.01f, lateralSpeed);
            passTimer = 0f;
            passIsLob = isLob;
            passActive = true;

            rb.simulated = false;
        }

        // ── Procedural fallbacks (used only when no prefab provides them) ──

        private void BuildVisual()
        {
            var go = new GameObject("Visual");
            go.transform.SetParent(transform, false);

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = ballColor };
            mr.sortingOrder = 25;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            const int segs = 24;
            var verts = new Vector3[segs + 1];
            var tris  = new int[segs * 3];
            verts[0] = Vector3.zero;
            for (int i = 0; i < segs; i++)
            {
                float a = i * Mathf.PI * 2f / segs;
                verts[i + 1] = new Vector3(Mathf.Cos(a) * ballRadius, Mathf.Sin(a) * ballRadius, 0f);
                tris[i * 3]     = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = (i + 1) % segs + 1;
            }
            var mesh = new Mesh { name = "BallBody", vertices = verts, triangles = tris };
            mesh.RecalculateNormals();
            mf.mesh = mesh;
        }

        private void BuildShadow()
        {
            var go = new GameObject("Shadow");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(shadowOffset.x, shadowOffset.y, 0f);

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = shadowColor };
            mr.sortingOrder = 24;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            const int segs = 20;
            var verts = new Vector3[segs + 1];
            var tris  = new int[segs * 3];
            verts[0] = Vector3.zero;
            for (int i = 0; i < segs; i++)
            {
                float a = i * Mathf.PI * 2f / segs;
                verts[i + 1] = new Vector3(Mathf.Cos(a) * shadowWidth, Mathf.Sin(a) * shadowHeight, 0f);
                tris[i * 3]     = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = (i + 1) % segs + 1;
            }
            var mesh = new Mesh { name = "BallShadow", vertices = verts, triangles = tris };
            mesh.RecalculateNormals();
            mf.mesh = mesh;
        }
    }
}
