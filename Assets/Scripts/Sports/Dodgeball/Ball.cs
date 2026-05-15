using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Dodgeball with a real (top-down) Height dimension and a small state
    /// machine over its trajectory.
    ///
    /// States:
    ///   - Carried:  Ground tracks the carrier; Height = carryHeight.
    ///   - Passing:  Parametric lerp to a fixed target; lob adds a sin-arc to
    ///               carryHeight, chest stays flat. Catches resolve via the
    ///               normal proximity pickup (anyone on the path can intercept).
    ///   - Thrown:   Velocity-driven via Rigidbody2D. Teammates of the thrower
    ///               can catch (if catchable); opponents are struck and the
    ///               ball caroms off based on hit zone (head / torso / limb).
    ///   - Bouncing: Post-carom arc — Height follows a sin curve scaled by
    ///               zone-specific apex/duration; pickups disabled.
    ///   - Loose:    Velocity-driven, no special handling; pickups enabled.
    ///
    /// Pickups use 2D distance against PlayerZoneTracker.All and require
    /// CanCatchBall() to encode the "no catches in restricted areas" rule.
    /// They also require Height ≤ pickupMaxHeight so an overhead lob can't
    /// be intercepted along its straight-line path.
    /// </summary>
    public class Ball : MonoBehaviour
    {
        public enum HitZone { Head, Torso, Limb }

        private enum State { Carried, Passing, Thrown, Bouncing, Loose }

        [Header("Pickup")]
        [SerializeField] private float pickupRadius = 0.6f;
        [SerializeField] private float throwerPickupCooldown = 0.4f;
        [Tooltip("A ball above this Height is overhead and not catchable.")]
        [SerializeField] private float pickupMaxHeight = 1.5f;

        [Header("Physics")]
        [SerializeField] private float linearDamping = 1.4f;

        [Header("Height (measured above the visible floor)")]
        [Tooltip("Local Y offset from ball.transform.position down to the visible floor. " +
                 "Match the player's FootOffset so a Height = 0 ball renders at foot level.")]
        [SerializeField] private float floorOffsetY = -0.79f;
        [Tooltip("Ball Height while carried (chest level, above feet).")]
        [SerializeField] private float carryHeight = 1.29f;
        [SerializeField] private float lobApex = 1.2f;
        [Tooltip("Constant downward acceleration applied to Height in the Thrown state (units/sec^2).")]
        [SerializeField] private float gravity = 12f;

        [Header("Throw bounce zones (Height above the floor)")]
        [Tooltip("Ball Height at/above this lands in the head zone.")]
        [SerializeField] private float headZoneMinHeight = 1.4f;
        [Tooltip("Ball Height at/below this lands in the limb zone.")]
        [SerializeField] private float limbZoneMaxHeight = 0.6f;
        [Tooltip("When a thrown ball's speed drops below this, it transitions to Loose without hitting.")]
        [SerializeField] private float thrownToLooseSpeed = 4f;

        [Header("Bounce — head")]
        [SerializeField, Range(0f, 1f)] private float headBounceFactor = 0.25f;
        [SerializeField] private float headBounceArcApex = 1.2f;
        [SerializeField] private float headBounceArcDuration = 0.6f;

        [Header("Bounce — torso")]
        [SerializeField, Range(0f, 1f)] private float torsoBounceFactor = 0.6f;
        [SerializeField] private float torsoBounceArcApex = 0.3f;
        [SerializeField] private float torsoBounceArcDuration = 0.3f;

        [Header("Bounce — limb")]
        [SerializeField, Range(0f, 1f)] private float limbBounceFactor = 0.35f;
        [SerializeField] private float limbBounceArcApex = 0.15f;
        [SerializeField] private float limbBounceArcDuration = 0.25f;

        [Header("Bounce chain")]
        [Tooltip("Apex multiplier per ground bounce (coefficient of restitution).")]
        [SerializeField, Range(0f, 1f)] private float bounceRestitution = 0.5f;
        [Tooltip("Lateral velocity multiplier applied at each ground bounce.")]
        [SerializeField, Range(0f, 1f)] private float bounceLateralFriction = 0.65f;
        [Tooltip("When the next bounce apex would be below this, the ball stops bouncing and rolls.")]
        [SerializeField] private float minBounceApex = 0.08f;
        [Tooltip("After flight (throw/pass/bounce chain ends), Height drops to 0 at this rate.")]
        [SerializeField] private float looseFallRate = 3f;

        [Header("Shadow")]
        [Tooltip("Ball Height at which the shadow has fully shrunk to shadowMinScale.")]
        [SerializeField] private float shadowFalloffHeight = 2.5f;
        [SerializeField, Range(0f, 1f)] private float shadowMinScale = 0.4f;

        [Header("Procedural fallback")]
        [SerializeField] private float ballRadius = 0.25f;
        [SerializeField] private Color ballColor = new Color(0.98f, 0.85f, 0.25f, 1f);
        [SerializeField] private float shadowWidth = 0.18f;
        [SerializeField] private float shadowHeight = 0.06f;
        [SerializeField] private Vector2 shadowOffset = new Vector2(0f, -0.08f);
        [SerializeField] private Color shadowColor = new Color(0f, 0f, 0f, 0.65f);

        private Rigidbody2D rb;
        private PlayerZoneTracker carrier;
        private PlayerMovement carrierMovement;
        private PlayerZoneTracker recentThrower;
        private float throwerCooldownRemaining;

        private Transform visualTransform;
        private Transform shadowTransform;
        private Vector3 shadowBaseScale = Vector3.one;

        private State state = State.Loose;

        // Passing state.
        private bool passIsLob;
        private float passTimer;
        private float passDurationCurrent;
        private Vector2 passStart;
        private Vector2 passEnd;
        private float passLaunchHeight;

        // Bouncing state.
        private float bounceArcTimer;
        private float bounceArcDuration;
        private float bounceArcApex;
        private float bounceStartHeight;

        // Thrown state vertical kinematics. Positive = rising; gravity pulls
        // it negative; ground impacts flip it via bounceRestitution.
        private float heightVelocity;

        public PlayerZoneTracker Carrier => carrier;

        /// <summary>Current visual height above the ball's ground (XY) position.</summary>
        public float Height { get; private set; }

        /// <summary>Fires whenever the ball attaches to a player (pickup, pass catch, or ForcePickup).</summary>
        public event System.Action<PlayerZoneTracker> OnAttached;

        /// <summary>Fires when a thrown ball caroms off an opponent. Args: hit player, zone.</summary>
        public event System.Action<PlayerZoneTracker, HitZone> OnHit;

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

            switch (state)
            {
                case State.Carried:  UpdateCarried(); break;
                case State.Passing:  UpdatePassing(); break;
                case State.Thrown:   UpdateThrown(); break;
                case State.Bouncing: UpdateBouncing(); break;
                case State.Loose:    UpdateLoose(); break;
            }

            ApplyVisualHeight();
        }

        private void UpdateCarried()
        {
            transform.position = carrier.transform.position;
            float jumpBob = carrierMovement != null ? carrierMovement.CurrentJumpHeight : 0f;
            Height = carryHeight + jumpBob;
        }

        private void UpdatePassing()
        {
            passTimer += Time.deltaTime;
            float t = Mathf.Clamp01(passTimer / passDurationCurrent);
            transform.position = Vector2.Lerp(passStart, passEnd, t);
            float baseline = Mathf.Lerp(passLaunchHeight, carryHeight, t);
            Height = passIsLob
                ? baseline + lobApex * Mathf.Sin(t * Mathf.PI)
                : baseline;
            TryPickup();
            if (state != State.Passing) return;  // pickup may have transitioned us
            if (t >= 1f) EnterLoose();
        }

        private void UpdateThrown()
        {
            // Gravity pulls the ball down continuously. Initial vertical
            // velocity is 0 (set in Throw), so the trajectory is a parabola
            // from the launch height to the floor.
            heightVelocity -= gravity * Time.deltaTime;
            Height += heightVelocity * Time.deltaTime;

            if (Height <= 0f && heightVelocity < 0f)
            {
                Height = 0f;
                rb.linearVelocity *= bounceLateralFriction;
                heightVelocity = -heightVelocity * bounceRestitution;

                // If the next vertical arc would peak below minBounceApex,
                // the ball is essentially settled — let it roll to a stop.
                float predictedApex = (heightVelocity * heightVelocity) / (2f * gravity);
                if (predictedApex < minBounceApex)
                {
                    heightVelocity = 0f;
                    EnterLoose();
                    return;
                }
            }

            TryThrownInteraction();
            if (state != State.Thrown) return;

            // Only fall back to Loose once the ball has settled on the ground
            // and slowed laterally.
            if (Height <= 0f
                && rb.linearVelocity.sqrMagnitude < thrownToLooseSpeed * thrownToLooseSpeed)
            {
                EnterLoose();
            }
        }

        private void UpdateBouncing()
        {
            bounceArcTimer += Time.deltaTime;
            float t = Mathf.Clamp01(bounceArcTimer / bounceArcDuration);
            Height = Mathf.Lerp(bounceStartHeight, 0f, t)
                   + bounceArcApex * Mathf.Sin(t * Mathf.PI);
            if (t < 1f) return;

            // Hit the ground. Bleed lateral velocity and start the next, smaller
            // bounce — or roll if it would be too short to read.
            rb.linearVelocity *= bounceLateralFriction;
            float nextApex = bounceArcApex * bounceRestitution;
            if (nextApex < minBounceApex)
            {
                Height = 0f;
                EnterLoose();
                return;
            }
            bounceStartHeight = 0f;
            bounceArcApex = nextApex;
            // Period of a parabolic arc scales ~sqrt(apex), so duration shrinks
            // proportionally — bounces speed up as they get shorter.
            bounceArcDuration = Mathf.Max(0.05f, bounceArcDuration * Mathf.Sqrt(bounceRestitution));
            bounceArcTimer = 0f;
        }

        private void UpdateLoose()
        {
            // Ball settles to the floor and rolls; linearDamping does the friction work.
            if (Height > 0f)
                Height = Mathf.MoveTowards(Height, 0f, looseFallRate * Time.deltaTime);
            TryPickup();
        }

        private void EnterLoose()
        {
            state = State.Loose;
            rb.simulated = true;
        }

        private void ApplyVisualHeight()
        {
            // Heights are measured above the floor; ball.transform.position
            // sits sprite-center above the floor by |floorOffsetY|. So the
            // Visual gets Height shifted down by that offset, and the Shadow
            // lands at (floorOffsetY + shadowOffset.y) — at floor regardless
            // of state (carried, in flight, at rest, etc).
            if (visualTransform != null)
            {
                var p = visualTransform.localPosition;
                visualTransform.localPosition = new Vector3(p.x, Height + floorOffsetY, p.z);
            }
            if (shadowTransform != null)
            {
                var sp = shadowTransform.localPosition;
                shadowTransform.localPosition = new Vector3(
                    shadowOffset.x, shadowOffset.y + floorOffsetY, sp.z);

                if (shadowFalloffHeight > 0f)
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
        }

        // ── Pickup / hit checks ──

        private void TryPickup()
        {
            if (Height > pickupMaxHeight) return;

            var trackers = PlayerZoneTracker.All;
            Vector2 ballPos = transform.position;
            float r2 = pickupRadius * pickupRadius;
            for (int i = 0; i < trackers.Count; i++)
            {
                var t = trackers[i];
                if (t == null) continue;
                if (t.HasBall) continue;
                if (t == recentThrower) continue;
                if (!t.CanCatchBall()) continue;

                Vector2 trackerPos = t.transform.position;
                if (Vector2.SqrMagnitude(trackerPos - ballPos) <= r2)
                {
                    AttachTo(t);
                    return;
                }
            }
        }

        // While Thrown: opponents trigger a carom, teammates may catch.
        private void TryThrownInteraction()
        {
            var trackers = PlayerZoneTracker.All;
            Vector2 ballPos = transform.position;
            float r2 = pickupRadius * pickupRadius;
            Team? throwerTeam = recentThrower != null ? recentThrower.Spawn.team : (Team?)null;

            for (int i = 0; i < trackers.Count; i++)
            {
                var t = trackers[i];
                if (t == null) continue;
                if (t == recentThrower) continue;

                Vector2 trackerPos = t.transform.position;
                if (Vector2.SqrMagnitude(trackerPos - ballPos) > r2) continue;

                bool isTeammate = throwerTeam.HasValue && t.Spawn.team == throwerTeam.Value;
                if (isTeammate)
                {
                    if (!t.HasBall && t.CanCatchBall() && Height <= pickupMaxHeight)
                    {
                        AttachTo(t);
                        return;
                    }
                    // Teammate present but can't catch — ball passes through.
                }
                else
                {
                    Carom(t, ClassifyHit(Height));
                    return;
                }
            }
        }

        private HitZone ClassifyHit(float height)
        {
            if (height >= headZoneMinHeight) return HitZone.Head;
            if (height >  limbZoneMaxHeight) return HitZone.Torso;
            return HitZone.Limb;
        }

        private void Carom(PlayerZoneTracker hit, HitZone zone)
        {
            Vector2 incoming = rb.linearVelocity;
            Vector2 normal = (Vector2)transform.position - (Vector2)hit.transform.position;
            if (normal.sqrMagnitude < 0.0001f) normal = -incoming;
            normal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector2.up;

            Vector2 reflected = Vector2.Reflect(incoming, normal);

            float factor, apex, duration;
            switch (zone)
            {
                case HitZone.Head:
                    factor = headBounceFactor;
                    apex = headBounceArcApex;
                    duration = headBounceArcDuration;
                    break;
                case HitZone.Limb:
                    factor = limbBounceFactor;
                    apex = limbBounceArcApex;
                    duration = limbBounceArcDuration;
                    break;
                default:
                    factor = torsoBounceFactor;
                    apex = torsoBounceArcApex;
                    duration = torsoBounceArcDuration;
                    break;
            }

            rb.linearVelocity = reflected.normalized * (incoming.magnitude * factor);

            bounceArcTimer = 0f;
            bounceArcDuration = Mathf.Max(0.05f, duration);
            bounceStartHeight = Height;
            bounceArcApex = apex;
            state = State.Bouncing;

            OnHit?.Invoke(hit, zone);
        }

        // ── Release / attach API ──

        private void AttachTo(PlayerZoneTracker t)
        {
            carrier = t;
            carrierMovement = t != null ? t.GetComponent<PlayerMovement>() : null;
            t.HeldBall = this;
            rb.linearVelocity = Vector2.zero;
            rb.simulated = false;
            state = State.Carried;
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
                carrierMovement = null;
            }
            recentThrower = null;
            throwerCooldownRemaining = 0f;
            transform.position = t.transform.position;
            AttachTo(t);
        }

        /// <summary>
        /// Velocity-driven release of the held ball — used for the Circle throw.
        /// Direction is normalized internally; power is units/sec. Vertical
        /// release velocity defaults to 0 (a flat horizontal throw); pass
        /// something positive to add a little arc.
        /// </summary>
        public void Throw(Vector2 direction, float power, float verticalVelocity = 0f)
        {
            if (carrier == null) return;
            if (direction.sqrMagnitude < 0.0001f) direction = Vector2.right;

            recentThrower = carrier;
            throwerCooldownRemaining = throwerPickupCooldown;
            carrier.HeldBall = null;
            carrier = null;
            carrierMovement = null;

            rb.simulated = true;
            rb.linearVelocity = direction.normalized * power;
            heightVelocity = verticalVelocity;
            state = State.Thrown;
        }

        /// <summary>
        /// Throw at a target position with enough arc to land at carryHeight
        /// when the ball reaches the target. Drag is ignored in the math
        /// (the ball will fall slightly short of far targets); the resulting
        /// arc is what a "thrower aiming up" would do to extend the reach.
        /// </summary>
        public void ThrowAt(Vector2 targetPos, float power)
        {
            if (carrier == null) return;

            Vector2 toTarget = targetPos - (Vector2)transform.position;
            float dist = toTarget.magnitude;
            if (dist < 0.01f)
            {
                Throw(Vector2.right, power);
                return;
            }

            Vector2 dir = toTarget / dist;
            float t = dist / power;
            float vy = (carryHeight - Height + 0.5f * gravity * t * t) / t;
            Throw(dir, power, vy);
        }

        /// <summary>
        /// Parametric pass to a fixed world position. Lateral motion is a
        /// straight lerp from current position to target; lob adds a sin-arc
        /// on top of carryHeight, chest stays flat. Catches resolve via the
        /// normal proximity pickup so anyone on the path can intercept.
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
            carrierMovement = null;

            passStart = start;
            passEnd = target;
            passDurationCurrent = dist / Mathf.Max(0.01f, lateralSpeed);
            passTimer = 0f;
            passIsLob = isLob;
            // Capture the visual height at the moment of release so the
            // trajectory's baseline starts there, not at carryHeight.
            passLaunchHeight = Height;
            state = State.Passing;

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
