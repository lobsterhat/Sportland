using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Minimal dodgeball. Two visible states: carried (follows a player and
    /// physics is paused) or loose (drifts under linear damping). Pickup is a
    /// proximity check against PlayerZoneTracker.All — only trackers whose
    /// CanCatchBall() returns true are eligible, which encodes the "no catches
    /// in restricted areas" rule.
    ///
    /// The thrower gets a short pickup cooldown so a thrown ball doesn't snap
    /// back to them on the next frame.
    /// </summary>
    public class Ball : MonoBehaviour
    {
        [SerializeField] private float pickupRadius = 0.6f;
        [SerializeField] private float linearDamping = 0.5f;
        [SerializeField] private Vector2 carryOffset = new Vector2(0f, 0.5f);
        [SerializeField] private float throwerPickupCooldown = 0.4f;
        [SerializeField] private float ballRadius = 0.25f;
        [SerializeField] private Color ballColor = new Color(0.98f, 0.85f, 0.25f, 1f);

        private Rigidbody2D rb;
        private PlayerZoneTracker carrier;
        private PlayerZoneTracker recentThrower;
        private float throwerCooldownRemaining;

        public PlayerZoneTracker Carrier => carrier;

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

            // Only build the procedural visual when nothing's rendering yet
            // (i.e. we weren't instantiated from a sprite-bearing prefab).
            if (GetComponentInChildren<Renderer>() == null)
            {
                BuildVisual();
            }
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
                transform.position = (Vector2)carrier.transform.position + carryOffset;
                return;
            }

            TryPickup();
        }

        private void TryPickup()
        {
            var trackers = PlayerZoneTracker.All;
            for (int i = 0; i < trackers.Count; i++)
            {
                var t = trackers[i];
                if (t == null) continue;
                if (t.HasBall) continue;
                if (t == recentThrower) continue;
                if (!t.CanCatchBall()) continue;

                Vector2 ballPos = transform.position;
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
            transform.position = (Vector2)t.transform.position + carryOffset;
            AttachTo(t);
        }

        /// <summary>
        /// Called by the carrier's input layer on throw press. Direction is
        /// unit-normalized internally; power is units/sec.
        /// </summary>
        public void Throw(Vector2 direction, float power)
        {
            if (carrier == null) return;
            if (direction.sqrMagnitude < 0.0001f) direction = Vector2.right;

            recentThrower = carrier;
            throwerCooldownRemaining = throwerPickupCooldown;

            carrier.HeldBall = null;
            carrier = null;

            rb.simulated = true;
            rb.linearVelocity = direction.normalized * power;
        }

        private void BuildVisual()
        {
            var visualGO = new GameObject("BallVisual");
            visualGO.transform.SetParent(transform, false);

            var mf = visualGO.AddComponent<MeshFilter>();
            var mr = visualGO.AddComponent<MeshRenderer>();
            mr.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = ballColor };
            mr.sortingOrder = 25;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            const int segs = 24;
            var verts = new Vector3[segs + 1];
            var tris = new int[segs * 3];
            verts[0] = Vector3.zero;
            for (int i = 0; i < segs; i++)
            {
                float a = i * Mathf.PI * 2f / segs;
                verts[i + 1] = new Vector3(Mathf.Cos(a) * ballRadius, Mathf.Sin(a) * ballRadius, 0f);
                tris[i * 3]     = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = (i + 1) % segs + 1;
            }
            var mesh = new Mesh { name = "Ball", vertices = verts, triangles = tris };
            mesh.RecalculateNormals();
            mf.mesh = mesh;
        }
    }
}
