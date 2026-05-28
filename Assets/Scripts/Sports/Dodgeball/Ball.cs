using System.Collections.Generic;
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

        public enum State { Carried, Passing, Thrown, Bouncing, Loose }

        /// <summary>Weights for the catch skill-check. All bonuses/penalties are additive on a 0..1 chance.</summary>
        [System.Serializable]
        public class CatchTuning
        {
            [Range(0f, 1f)] public float minBaseChance = 0.20f;   // at catching = 0
            [Range(0f, 1f)] public float maxBaseChance = 0.85f;   // at catching = 100
            public float comfortableSpeed = 8f;                   // no speed penalty at/below this
            public float maxSpeed = 24f;                          // full speed penalty at/above this
            [Range(0f, 1f)] public float speedPenalty = 0.40f;
            [Range(0f, 1f)] public float facingBonus = 0.15f;     // facing the ball head-on
            [Range(0f, 1f)] public float facingPenalty = 0.60f;   // facing fully away
            [Range(0f, 1f)] public float stancePenalty = 0.35f;   // not set in a defensive stance
            [Range(0f, 1f)] public float timingBonus = 0.20f;     // press right as the ball arrives
            [Range(0f, 1f)] public float timingPenalty = 0.30f;   // press at the edge of the window
            [Range(0f, 1f)] public float luckBonus = 0.15f;       // random upside scaled by luck
        }

        /// <summary>Per-term breakdown of a catch chance, for resolution + HUD/debug display.</summary>
        public struct CatchFactors
        {
            public bool valid;
            public float catching01;       // catcher catching rating (0..1)
            public float luck01;           // catcher luck rating (0..1)
            public float ballSpeed;        // u/s
            public float baseChance;       // from catching
            public float speedPenalty;     // subtracted (the only "throw speed" influence)
            public float facingAlignment;  // -1..1 (+1 = head-on into the ball)
            public float facingFactor;     // signed
            public bool  inStance;         // catcher set in a defensive stance
            public float stanceFactor;     // signed (penalty when not in stance)
            public bool  armed;            // catch press active
            public float timingScore;      // 0..1
            public float timingFactor;     // signed
            public float luckContribution; // added (0 in preview)
            public float rawChance;        // unclamped sum
            public float finalChance;      // clamp01(rawChance)
        }

        /// <summary>Per-throw telemetry: state at release vs at the first live contact.</summary>
        public struct ThrowTelemetry
        {
            public bool releaseValid;
            public Vector2 origin;
            public float releaseSpeed;   // lateral u/s
            public float releaseHeight;  // u above floor
            public bool destValid;
            public Vector2 dest;
            public float arrivalSpeed;   // lateral u/s
            public float arrivalHeight;  // u above floor
            public float Distance => (releaseValid && destValid) ? Vector2.Distance(origin, dest) : 0f;
        }

        [Header("Pickup")]
        [SerializeField] private float pickupRadius = 0.6f;
        [Tooltip("Reach of an active (button) catch — usually a touch more forgiving than the passive pickup radius.")]
        [SerializeField] private float catchRadius = 0.9f;
        [SerializeField] private float throwerPickupCooldown = 0.4f;
        [Tooltip("A ball above this Height is overhead and not catchable.")]
        [SerializeField] private float pickupMaxHeight = 1.5f;

        [Header("Catch (skill check)")]
        [Tooltip("A human-controlled player must press Catch within this window (seconds) before the ball arrives.")]
        [SerializeField] private float catchArmWindow = 0.35f;
        [Tooltip("Loose-ball speed above which securing it needs a skill catch rather than a free pickup.")]
        [SerializeField] private float skillSpeedThreshold = 3f;
        [SerializeField] private CatchTuning catchTuning = new CatchTuning();

        [Header("Physics")]
        [SerializeField] private float linearDamping = 1.4f;
        [Tooltip("Speed kept when bouncing off a boundary wall (0 = dead stop, 1 = perfectly elastic).")]
        [SerializeField, Range(0f, 1f)] private float wallRestitution = 0.6f;

        [Header("Height (measured above the visible floor)")]
        [Tooltip("Local Y offset from ball.transform.position down to the visible floor. " +
                 "Match the player's FootOffset so a Height = 0 ball renders at foot level.")]
        [SerializeField] private float floorOffsetY = -0.79f;
        [Tooltip("Ball Height while carried (chest level, above feet).")]
        [SerializeField] private float carryHeight = 1.29f;
        [SerializeField] private float lobApex = 0.15f;   // near-flat base; distance/clearance add the height
        [Tooltip("Extra lateral pass speed (u/s) per unit of distance — long passes are thrown harder.")]
        [SerializeField] private float passSpeedPerUnit = 0.35f;
        [Tooltip("Cap on lateral pass speed (u/s) after distance scaling.")]
        [SerializeField] private float maxPassSpeed = 24f;
        [Tooltip("Extra lob apex (height) per unit of pass distance.")]
        [SerializeField] private float lobApexPerUnit = 0.06f;
        [Tooltip("Lob apex floor when an opponent stands in the pass lane (clears a standing reach).")]
        [SerializeField] private float lobClearanceApex = 1f;
        [Tooltip("Maximum lob apex after scaling (keeps it 'a little height', not a moonball).")]
        [SerializeField] private float maxLobApex = 1.5f;
        [Tooltip("Half-width (u) of the lob lane used to detect opponents to clear.")]
        [SerializeField] private float lobLaneRadius = 0.9f;
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
        private float passApex;

        // Bouncing state.
        private float bounceArcTimer;
        private float bounceArcDuration;
        private float bounceArcApex;
        private float bounceStartHeight;

        // Hits collected "in the air": each distinct player struck since the
        // last release is recorded here and only fires OnHit (counts) once the
        // ball touches the floor. If the victim's team catches it before then,
        // every pending hit is wiped — the "caught before it hit the ground"
        // rule. Repeated touches of the same player still count once.
        private struct PendingHit { public PlayerZoneTracker victim; public HitZone zone; public float speed; public bool victimOutOfZone; }
        private readonly List<PendingHit> pendingHits = new List<PendingHit>();

        // True once the ball has touched the floor since the last release. A
        // pickup after this is possession only, never a (scoring) catch.
        private bool groundedSinceRelease;

        // Thrown state vertical kinematics. Positive = rising; gravity pulls
        // it negative; ground impacts flip it via bounceRestitution.
        private float heightVelocity;

        public PlayerZoneTracker Carrier => carrier;

        /// <summary>Player credited with the most recent release (thrower/passer/cannon source), or null.</summary>
        public PlayerZoneTracker RecentThrower => recentThrower;

        /// <summary>True if the most recent release was a throw (offensive) rather than a pass to a teammate. Lets a catch put the thrower out only for actual throws.</summary>
        public bool LastReleaseWasThrow { get; private set; }

        /// <summary>The thrower's intended target at release (set by the controller), for the play-by-play log. May be null (e.g. an untargeted throw).</summary>
        public PlayerZoneTracker IntendedTarget { get; set; }

        /// <summary>True if the ball has caromed off a player since the last release — lets the log say "deflects and is caught".</summary>
        public bool DeflectedSinceRelease { get; private set; }

        /// <summary>Current trajectory state.</summary>
        public State CurrentState => state;

        /// <summary>True when the ball has left the play area (beyond the outer strip bounds).</summary>
        public bool IsOutOfPlay
        {
            get
            {
                Vector2 p = transform.position;
                return Mathf.Abs(p.x) > CourtSetup.HalfWidth + ZoneFactory.StripDepth
                    || Mathf.Abs(p.y) > CourtSetup.HalfHeight + ZoneFactory.StripDepth;
            }
        }

        /// <summary>Lateral (court-plane) velocity of the ball.</summary>
        public Vector2 Velocity => rb != null ? rb.linearVelocity : Vector2.zero;

        /// <summary>Current visual height above the ball's ground (XY) position.</summary>
        public float Height { get; private set; }

        /// <summary>Name of the current trajectory state (Carried / Passing / Thrown / Bouncing / Loose).</summary>
        public string StateLabel => state.ToString();

        // Most recent resolved catch attempt — surfaced for the diagnostics HUD.
        public CatchFactors LastCatchFactors { get; private set; }
        public float LastCatchRoll { get; private set; }
        public bool LastCatchSucceeded { get; private set; }
        public float LastCatchTime { get; private set; } = -999f;

        // Most recent throw's release vs arrival metrics — for the HUD.
        public ThrowTelemetry LastThrow { get; private set; }

        /// <summary>Fires whenever the ball attaches to a player (pickup, pass catch, or ForcePickup).</summary>
        public event System.Action<PlayerZoneTracker> OnAttached;

        /// <summary>Fires when a thrown ball caroms off an opponent. Args: hit player, zone, impact speed (u/s).</summary>
        public event System.Action<PlayerZoneTracker, HitZone, float> OnHit;

        /// <summary>Fires when a player skill-catches an opponent's throw. Args: catcher.</summary>
        public event System.Action<PlayerZoneTracker> OnCaught;

        /// <summary>Fires on release. Args: thrower, intended target (may be null), isThrow (vs pass). For the play-by-play log.</summary>
        public event System.Action<PlayerZoneTracker, PlayerZoneTracker, bool> OnReleased;

        /// <summary>Fires when the ball settles to a loose ball (a play ended without a catch/possession). For the play-by-play log.</summary>
        public event System.Action OnBecameLoose;

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
            // Count down the self-recatch lockout only. Attribution
            // (recentThrower) persists for the whole flight — cleared on the
            // next release or a scripted pickup — so a hit or catch always
            // knows who threw it, no matter how long the ball is in the air.
            if (throwerCooldownRemaining > 0f) throwerCooldownRemaining -= Time.deltaTime;

            switch (state)
            {
                case State.Carried:  UpdateCarried(); break;
                case State.Passing:  UpdatePassing(); break;
                case State.Thrown:   UpdateThrown(); break;
                case State.Bouncing: UpdateBouncing(); break;
                case State.Loose:    UpdateLoose(); break;
            }

            ReflectOffBoundaries();
            ApplyVisualHeight();
        }

        // Keep the ball inside the outfield's outer edge: clamp the lateral
        // position and reflect the velocity so it bounces off the boundary wall.
        private void ReflectOffBoundaries()
        {
            if (state != State.Thrown && state != State.Bouncing && state != State.Loose) return;

            float maxX = CourtSetup.HalfWidth + ZoneFactory.StripDepth - ballRadius;
            float maxY = CourtSetup.HalfHeight + ZoneFactory.StripDepth - ballRadius;
            Vector3 p = transform.position;
            Vector2 v = rb.linearVelocity;
            bool changed = false;

            if (p.x < -maxX) { p.x = -maxX; if (v.x < 0f) v.x = -v.x * wallRestitution; changed = true; }
            else if (p.x > maxX) { p.x = maxX; if (v.x > 0f) v.x = -v.x * wallRestitution; changed = true; }
            if (p.y < -maxY) { p.y = -maxY; if (v.y < 0f) v.y = -v.y * wallRestitution; changed = true; }
            else if (p.y > maxY) { p.y = maxY; if (v.y > 0f) v.y = -v.y * wallRestitution; changed = true; }

            if (changed) { transform.position = p; rb.linearVelocity = v; }
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
                ? baseline + passApex * Mathf.Sin(t * Mathf.PI)
                : baseline;
            // A pass is a live ball: a teammate on the path receives it, but an
            // opponent who steps in front either intercepts it (catch → turnover
            // / score) or is struck by it (carom → hit), exactly like a throw.
            TryLiveInteraction();
            if (state != State.Passing) return;  // a catch / hit may have transitioned us
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
                groundedSinceRelease = true;   // touched the floor: no scoring catch past here
                ConfirmPendingHits();          // and any pending hits now stand
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

            TryLiveInteraction();
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

            // A deflected ball stays live: it can be caught (incl. a diving catch)
            // or picked up while bouncing — no re-carom (caromOnMiss: false).
            TryPickup();
            if (state != State.Bouncing) return;

            if (t < 1f) return;

            // First ground contact: the ball has touched the floor, so a pickup
            // from here is possession, not a scoring catch — and any pending hits
            // now stand (a catch can no longer nullify them).
            groundedSinceRelease = true;
            ConfirmPendingHits();

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
            ConfirmPendingHits();   // settled without a catch — any pending hits stand
            state = State.Loose;
            rb.simulated = true;
            OnBecameLoose?.Invoke();
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

        // Loose / Passing states: anyone in range may take the ball. A failed
        // take here just leaves the ball in play (no carom).
        private void TryPickup()
        {
            if (Height > pickupMaxHeight) return;

            var trackers = PlayerZoneTracker.All;
            Vector2 ballPos = transform.position;
            for (int i = 0; i < trackers.Count; i++)
            {
                var t = trackers[i];
                if (t == null || (t == recentThrower && throwerCooldownRemaining > 0f)) continue;

                float radius = CatchRadiusFor(t);
                if (Vector2.SqrMagnitude((Vector2)t.transform.position - ballPos) > radius * radius)
                    continue;

                if (TryTakeBall(t, caromOnMiss: false)) return;
            }
        }

        // Live ball (Thrown or Passing): an opponent of the thrower/passer who
        // doesn't catch it is struck (carom → hit); a teammate on the path may
        // catch it (a pass's intended receiver), and a non-catch passes by.
        private void TryLiveInteraction()
        {
            var trackers = PlayerZoneTracker.All;
            Vector2 ballPos = transform.position;
            Team? throwerTeam = recentThrower != null ? recentThrower.Spawn.team : (Team?)null;

            for (int i = 0; i < trackers.Count; i++)
            {
                var t = trackers[i];
                if (t == null || (t == recentThrower && throwerCooldownRemaining > 0f)) continue;

                float radius = CatchRadiusFor(t);
                if (Vector2.SqrMagnitude((Vector2)t.transform.position - ballPos) > radius * radius)
                    continue;

                bool isTeammate = throwerTeam.HasValue && t.Spawn.team == throwerTeam.Value;
                if (TryTakeBall(t, caromOnMiss: !isTeammate)) return;
            }
        }

        /// <summary>
        /// Resolves a ball/player contact. Slow loose balls are free pickups.
        /// For a live ball, a human always — and an AI when defending an
        /// opponent throw — must have an armed catch to attempt one (resolved
        /// by the skill check); otherwise an opponent throw hits (carom) and a
        /// teammate pass / loose ball is auto-caught by AI. caromOnMiss marks
        /// the opponent-throw case. Returns true if the contact was consumed.
        /// </summary>
        private bool TryTakeBall(PlayerZoneTracker t, bool caromOnMiss)
        {
            if (t.HasBall) return false;

            bool live = state == State.Thrown || state == State.Passing
                        || rb.linearVelocity.sqrMagnitude > skillSpeedThreshold * skillSpeedThreshold;

            if (!live)
            {
                // A loose / dead ball can be retrieved by anyone, anywhere — incl.
                // crossing into the opponent's area to grab it and carry it back.
                // (Catching a LIVE throw out of zone is still illegal — gated by
                // CanCatchBall in the live paths below — except a diving catch.)
                if (Height <= pickupMaxHeight) { Attach(t); return true; }
                return false;
            }

            bool human = IsHumanControlled(t);
            // AI only skill-catches when defending an opponent's throw; teammate
            // passes / loose balls auto-catch (keeps passing + control transfer).
            bool deliberate = human || (caromOnMiss && HasAI(t));

            // A catch needs the ball within reach height.
            if (deliberate && t.CanCatchBall() && t.IsCatchArmed(catchArmWindow) && Height <= pickupMaxHeight)
            {
                RecordArrival();
                var f = BuildCatchFactors(t, applyLuck: true);
                float roll = Random.value;
                RecordCatchAttempt(f, roll);
                if (roll < f.finalChance) { Attach(t); return true; }
                ResolveMiss(t);
                return true;
            }

            // An opponent throw hits only if the ball is within the player's body
            // band — ducking drops the top, jumping raises the bottom, so the ball
            // can pass over/under and miss.
            if (caromOnMiss)
            {
                if (WithinBody(t)) { RecordArrival(); Carom(t, ClassifyHit(Height)); return true; }
                return false;   // passed over a ducker / under a jumper
            }

            // Pass / loose ball: humans must arm (pass-by), AI/uncontrolled auto-catch.
            if (!human && t.CanCatchBall() && Height <= pickupMaxHeight) { RecordArrival(); Attach(t); return true; }
            return false;
        }

        // True if the ball's Height is within the player's current vertical body
        // band (which jump/duck shift). Falls back to the catch ceiling.
        private bool WithinBody(PlayerZoneTracker t)
        {
            var m = t.GetComponent<PlayerMovement>();
            if (m == null) return Height <= pickupMaxHeight;
            return Height >= m.BodyBottom && Height <= m.BodyTop;
        }

        // Catch/pickup reach for a player — the larger catch radius for a
        // controlled or AI player, extended further while diving (arms out).
        private float CatchRadiusFor(PlayerZoneTracker t)
        {
            float r = (IsHumanControlled(t) || HasAI(t)) ? catchRadius : pickupRadius;
            var m = t.GetComponent<PlayerMovement>();
            if (m != null && m.IsDiving) r += m.DiveReach;
            return r;
        }

        private static bool IsHumanControlled(PlayerZoneTracker t)
            => t.GetComponent<DodgeballPlayerInput>() != null;

        private static bool HasAI(PlayerZoneTracker t)
            => t.GetComponent<DodgeballAI>() != null;

        /// <summary>
        /// Builds the full per-term breakdown of a catch chance for a player
        /// against the ball right now. With applyLuck=false the luck term is
        /// 0 (deterministic preview for the HUD); with true it rolls the luck
        /// contribution (used at actual resolution).
        /// </summary>
        public CatchFactors BuildCatchFactors(PlayerZoneTracker catcher, bool applyLuck)
        {
            var f = new CatchFactors { valid = catcher != null };
            if (catcher == null) return f;

            var catchAttr = catcher.GetComponent<DodgeballAttributes>();
            var genAttr   = catcher.GetComponent<GeneralAttributes>();
            f.catching01 = catchAttr != null ? catchAttr.Catching01 : 0.6f;
            f.luck01     = genAttr != null ? genAttr.Luck01 : 0.5f;
            var move = catcher.GetComponent<PlayerMovement>();

            // Base from catching ability.
            f.baseChance = Mathf.Lerp(catchTuning.minBaseChance, catchTuning.maxBaseChance, f.catching01);

            // Faster ball = harder. This is the only path throw speed feeds in:
            // a harder thrower simply produces a higher ballSpeed.
            f.ballSpeed = rb.linearVelocity.magnitude;
            float speedT = Mathf.Clamp01(
                (f.ballSpeed - catchTuning.comfortableSpeed) /
                Mathf.Max(0.01f, catchTuning.maxSpeed - catchTuning.comfortableSpeed));
            f.speedPenalty = speedT * catchTuning.speedPenalty;

            // Facing: catcher's Facing vs the ball's movement vector.
            Vector2 vel = rb.linearVelocity;
            if (vel.sqrMagnitude < 0.0001f && state == State.Passing) vel = passEnd - passStart;
            if (vel.sqrMagnitude < 0.0001f)
            {
                f.facingAlignment = 0f;
                f.facingFactor = 0f;
            }
            else
            {
                Vector2 facing = move != null ? move.Facing : Vector2.right;
                f.facingAlignment = -Vector2.Dot(facing.normalized, vel.normalized); // +1 = head-on
                f.facingFactor = Mathf.Lerp(-catchTuning.facingPenalty, catchTuning.facingBonus,
                                            (f.facingAlignment + 1f) * 0.5f);
            }

            // Timing: press-window reaction. Neutral (0) until a catch is armed.
            f.armed = catcher.IsCatchArmed(catchArmWindow);
            if (f.armed)
            {
                float sincePress = Time.time - catcher.CatchArmedAt;
                f.timingScore = Mathf.Clamp01(1f - sincePress / Mathf.Max(0.01f, catchArmWindow));
                f.timingFactor = Mathf.Lerp(-catchTuning.timingPenalty, catchTuning.timingBonus, f.timingScore);
            }
            else
            {
                f.timingScore = 0f;
                f.timingFactor = 0f;
            }

            // Luck: random upside that grows with the luck rating.
            f.luckContribution = applyLuck ? Random.value * f.luck01 * catchTuning.luckBonus : 0f;

            // Defensive stance: a flat-footed catcher (not set) takes a penalty.
            f.inStance = move != null && move.InDefensiveStance;
            f.stanceFactor = f.inStance ? 0f : -catchTuning.stancePenalty;

            f.rawChance = f.baseChance - f.speedPenalty
                        + f.facingFactor + f.stanceFactor + f.timingFactor + f.luckContribution;
            f.finalChance = Mathf.Clamp01(f.rawChance);
            return f;
        }

        /// <summary>Deterministic catch preview (no luck roll) for HUD / debug.</summary>
        public CatchFactors PreviewCatch(PlayerZoneTracker catcher) => BuildCatchFactors(catcher, false);

        private void RecordCatchAttempt(CatchFactors f, float roll)
        {
            LastCatchFactors = f;
            LastCatchRoll = roll;
            LastCatchSucceeded = roll < f.finalChance;
            LastCatchTime = Time.time;
        }

        // Telemetry: snapshot at release; later filled in at the first live contact.
        private void RecordRelease(float lateralSpeed)
        {
            LastThrow = new ThrowTelemetry
            {
                releaseValid = true,
                origin = transform.position,
                releaseSpeed = lateralSpeed,
                releaseHeight = Height,
                destValid = false,
            };
        }

        private void RecordArrival()
        {
            var t = LastThrow;
            if (!t.releaseValid || t.destValid) return;
            t.destValid = true;
            t.dest = transform.position;
            t.arrivalSpeed = rb.linearVelocity.magnitude;
            t.arrivalHeight = Height;
            LastThrow = t;
        }

        // A flubbed catch resolves into one of three outcomes at random.
        private void ResolveMiss(PlayerZoneTracker catcher)
        {
            rb.simulated = true;
            Vector2 v = rb.linearVelocity;
            if (v.sqrMagnitude < 0.01f)
            {
                // From a parametric pass — synthesize a direction to react with.
                Vector2 dir = (passEnd - passStart);
                v = dir.sqrMagnitude > 0.0001f ? dir.normalized * 6f : Vector2.right * 6f;
            }

            switch (Random.Range(0, 3))
            {
                case 0: // carom off the catcher (a "hit")
                    rb.linearVelocity = v;
                    Carom(catcher, ClassifyHit(Height));
                    break;
                case 1: // fumble — drops loose at the catcher's feet
                    rb.linearVelocity = v * 0.1f;
                    heightVelocity = 0f;
                    EnterLoose();
                    break;
                default: // deflect backward, back toward where it came from
                    rb.linearVelocity = -v * 0.5f;
                    heightVelocity = Mathf.Max(heightVelocity, 2.5f);
                    state = State.Thrown;
                    break;
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
            DeflectedSinceRelease = true;   // a deflection — for the play-by-play log
            Vector2 incoming = rb.linearVelocity;

            // A struck pass is parametric (rb asleep, zero velocity): rebuild the
            // lateral velocity from the pass leg so the carom direction and the
            // impact speed (damage / telemetry) are real, and wake the body so
            // the resulting bounce runs on physics.
            if (incoming.sqrMagnitude < 0.0001f && state == State.Passing)
            {
                float dur = Mathf.Max(0.0001f, passDurationCurrent);
                incoming = (passEnd - passStart) / dur;
                rb.simulated = true;
            }

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

            // Defer the hit: record it (one entry per distinct player) instead of
            // firing OnHit now. It only counts once the ball touches the floor; if
            // the victim's team catches it first, every hit collected this airborne
            // segment is wiped. Repeated touches of a player count once.
            AddPendingHit(hit, zone, incoming.magnitude);
        }

        // Record a strike as a pending (airborne) hit — one entry per distinct
        // player, so repeated touches of the same player still count once.
        private void AddPendingHit(PlayerZoneTracker victim, HitZone zone, float speed)
        {
            if (victim == null) return;
            for (int i = 0; i < pendingHits.Count; i++)
                if (pendingHits[i].victim == victim) return;   // already struck this segment
            pendingHits.Add(new PendingHit
            {
                victim = victim, zone = zone, speed = speed,
                victimOutOfZone = !victim.IsInZone,   // hit while out of their area can't be saved by a catch
            });
        }

        // Flight ended on the floor (or the thrower's own team recovered the
        // ricochet) without the victim's team catching it: every pending hit
        // stands — fire OnHit once per distinct player.
        private void ConfirmPendingHits()
        {
            for (int i = 0; i < pendingHits.Count; i++)
            {
                var h = pendingHits[i];
                if (h.victim != null) OnHit?.Invoke(h.victim, h.zone, h.speed);
            }
            pendingHits.Clear();
        }

        // The victim's team caught the ball out of the air. A victim struck while
        // IN their area is saved (hit nullified); a victim struck while OUT of
        // their area is NOT saved — that hit stands and earns the catch no credit.
        // Returns true if the catch should still score: a clean interception, or
        // it saved at least one in-zone teammate.
        private bool ResolveAirborneCatch()
        {
            bool hadPending = pendingHits.Count > 0;
            bool savedInZone = false;
            for (int i = 0; i < pendingHits.Count; i++)
            {
                var h = pendingHits[i];
                if (h.victimOutOfZone)
                {
                    if (h.victim != null) OnHit?.Invoke(h.victim, h.zone, h.speed);   // stands — no save
                }
                else
                {
                    savedInZone = true;   // nullified: not fired
                }
            }
            pendingHits.Clear();
            return !hadPending || savedInZone;
        }

        // ── Release / attach API ──

        // Secures the ball to t and fires OnCaught only for a genuine catch: an
        // opponent of the thrower/passer taking a live ball out of the air — a
        // throw, a pass, or a deflection caught before it touches the floor. A
        // pickup off the hop (after the ball has grounded), a dead loose ball, an
        // out-of-bounds retrieval, or a teammate receiving a pass is possession
        // only, not a catch.
        private void Attach(PlayerZoneTracker t)
        {
            bool fromOpponent = recentThrower != null && recentThrower.Spawn.team != t.Spawn.team;
            // A scoring catch must take the ball out of the air: a live state AND
            // the ball hasn't touched the floor since release (a pickup off the
            // hop is possession only, not a catch).
            bool airborneCatch = fromOpponent && !groundedSinceRelease
                           && (state == State.Thrown || state == State.Passing || state == State.Bouncing);

            // Resolve any still-airborne hits. An airborne catch by the victim's
            // team saves in-zone victims (nullifies their hit) but NOT victims who
            // were out of their area when hit — those hits stand and the catch
            // scores no credit for them. Any other secure (the thrower's own team
            // recovers it, or a post-bounce pickup) lets all pending hits stand.
            bool countsAsCatch = airborneCatch && ResolveAirborneCatch();
            if (!airborneCatch) ConfirmPendingHits();

            AttachTo(t);
            if (countsAsCatch) OnCaught?.Invoke(t);
        }

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
        /// The carrier loses the ball where they stand — it falls straight to a
        /// loose ball (no throw). Used when a player is caught grounded outside
        /// their area still holding it. recentThrower is cleared (a dropped ball
        /// is no one's throw), so anyone may retrieve it — though the dropper
        /// can't until they're back in their area (CanCatchBall).
        /// </summary>
        public void Drop()
        {
            if (carrier == null) return;
            carrier.HeldBall = null;
            carrier = null;
            carrierMovement = null;
            recentThrower = null;
            throwerCooldownRemaining = 0f;
            rb.linearVelocity = Vector2.zero;
            heightVelocity = 0f;
            groundedSinceRelease = true;   // dead ball — a pickup off it isn't a catch
            EnterLoose();                  // wakes physics; falls to the floor and rolls
        }

        /// <summary>
        /// Reset to a loose ball at a ground position (Height 0) — used by the
        /// match restarter when switching test modes. Detaches any carrier and
        /// clears thrower / pending-hit state so it's a clean neutral ball.
        /// </summary>
        public void ResetLoose(Vector2 groundPos)
        {
            if (carrier != null) { carrier.HeldBall = null; carrier = null; carrierMovement = null; }
            recentThrower = null;
            throwerCooldownRemaining = 0f;
            pendingHits.Clear();
            transform.position = new Vector3(groundPos.x, groundPos.y, transform.position.z);
            Height = 0f;
            heightVelocity = 0f;
            groundedSinceRelease = true;
            LastReleaseWasThrow = false;
            EnterLoose();
            rb.linearVelocity = Vector2.zero;
        }

        // A held ball may only be released (thrown or passed) from your own area,
        // or while airborne — the jump exception that lets you leap over the line
        // to throw/pass. A grounded player in the opponent's area can't release it
        // (no offensive play from their side); they must carry it home first.
        private bool CarrierMayRelease()
        {
            if (carrier == null || carrier.IsInZone) return true;
            return carrierMovement != null && !carrierMovement.IsGrounded;
        }

        /// <summary>
        /// Velocity-driven release of the held ball — used for the Circle throw.
        /// Direction is normalized internally; power is units/sec. Vertical
        /// release velocity defaults to 0 (a flat horizontal throw); pass
        /// something positive to add a little arc.
        /// </summary>
        public void Throw(Vector2 direction, float power, float verticalVelocity = 0f)
        {
            if (carrier == null || !CarrierMayRelease()) return;
            if (direction.sqrMagnitude < 0.0001f) direction = Vector2.right;

            recentThrower = carrier;
            throwerCooldownRemaining = throwerPickupCooldown;
            carrier.HeldBall = null;
            carrier = null;
            carrierMovement = null;

            rb.simulated = true;
            rb.linearVelocity = direction.normalized * power;
            heightVelocity = verticalVelocity;
            groundedSinceRelease = false;
            DeflectedSinceRelease = false;
            LastReleaseWasThrow = true;
            state = State.Thrown;
            RecordRelease(power);
            OnReleased?.Invoke(recentThrower, IntendedTarget, true);
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
            float t = FlightTime(dist, power);
            float vy = (carryHeight - Height + 0.5f * gravity * t * t) / t;
            Throw(dir, power, vy);
        }

        /// <summary>
        /// Estimated lateral time-of-flight to cover <paramref name="dist"/> at
        /// release speed <paramref name="power"/>, accounting for linearDamping
        /// (the ball decelerates, so this is longer than dist/power). Drag-free
        /// and beyond-max-range cases fall back to dist/power.
        ///
        /// Under exponential decay v(t) = v0·e^(-k·t), distance covered is
        /// (v0/k)(1 - e^(-k·t)); solving for t gives the closed form below.
        /// </summary>
        public float FlightTime(float dist, float power)
        {
            float v0 = Mathf.Max(0.01f, power);
            float k = linearDamping;
            if (k <= 0.0001f) return dist / v0;          // no drag: constant speed

            float maxRange = v0 / k;                     // asymptotic reach under drag
            if (dist >= maxRange * 0.999f) return dist / v0;  // can't actually reach; avoid log blow-up
            return -Mathf.Log(1f - dist * k / v0) / k;
        }

        /// <summary>
        /// Predicts the ball's Height after it travels <paramref name="lateralDistance"/>
        /// at its current speed, integrating heightVelocity + gravity over the
        /// (drag-aware) flight time. Ignores bounces — meant for a direct shot
        /// reaching a defender. Used by AI to pick duck / jump / catch.
        /// </summary>
        public float PredictHeightAfter(float lateralDistance)
        {
            float speed = rb != null ? rb.linearVelocity.magnitude : 0f;
            if (speed < 0.01f) return Height;
            float t = FlightTime(lateralDistance, speed);
            return Mathf.Max(0f, Height + heightVelocity * t - 0.5f * gravity * t * t);
        }

        /// <summary>
        /// Predicts the lateral world position where the ball first reaches the
        /// ground on its current trajectory (projectile fall time × drag-aware
        /// lateral distance). Ignores wall/player bounces — meant for AI to
        /// position under an incoming throw; re-evaluate each frame so a wall
        /// bounce self-corrects. Returns the current position if not moving.
        /// </summary>
        public Vector2 PredictGroundPoint()
        {
            Vector2 pos = transform.position;
            Vector2 v = rb != null ? rb.linearVelocity : Vector2.zero;
            float v0 = v.magnitude;
            if (v0 < 0.01f) return pos;

            // Time for Height to fall to 0:  0.5·g·t² − hv·t − H = 0.
            float disc = heightVelocity * heightVelocity + 2f * gravity * Mathf.Max(0f, Height);
            float t = gravity > 0.0001f ? (heightVelocity + Mathf.Sqrt(disc)) / gravity : 0f;
            if (t <= 0f) return pos;

            // Lateral distance covered in t under exponential drag.
            float k = linearDamping;
            float dist = k > 0.0001f ? (v0 / k) * (1f - Mathf.Exp(-k * t)) : v0 * t;
            return pos + v.normalized * dist;
        }

        /// <summary>
        /// Lead-the-target aim point. Predicts where the target will be when
        /// the ball arrives (drag-aware flight time) and blends from the
        /// target's current position (anticipation 0) to the full predicted
        /// intercept (anticipation 1).
        /// </summary>
        public Vector2 LeadAim(Vector2 throwerPos, Vector2 targetPos,
                               Vector2 targetVelocity, float power, float anticipation01)
        {
            float dist = Vector2.Distance(throwerPos, targetPos);
            float flightTime = FlightTime(dist, power);
            return targetPos + targetVelocity * (flightTime * Mathf.Clamp01(anticipation01));
        }

        /// <summary>
        /// Carrier-free launch (e.g. a debug cannon). Teleports the ball to
        /// fromPos at chest height, then fires a ballistic arc at targetPos so
        /// it arrives near chest level. virtualThrower is attributed as the
        /// thrower for the catch math and team logic (use an opponent of the
        /// intended catcher so the catcher is a valid target).
        /// </summary>
        public void LaunchFrom(Vector2 fromPos, Vector2 targetPos, float power, PlayerZoneTracker virtualThrower)
        {
            if (carrier != null)
            {
                carrier.HeldBall = null;
                carrier = null;
                carrierMovement = null;
            }

            transform.position = fromPos;
            Height = carryHeight;

            Vector2 toTarget = targetPos - fromPos;
            float dist = toTarget.magnitude;
            Vector2 dir = dist > 0.01f ? toTarget / dist : Vector2.right;
            float t = dist / Mathf.Max(0.01f, power);
            // Launch height == carryHeight, so this lands back at chest level.
            float vy = (carryHeight - Height + 0.5f * gravity * t * t) / t;

            recentThrower = virtualThrower;
            throwerCooldownRemaining = throwerPickupCooldown;
            rb.simulated = true;
            rb.linearVelocity = dir * power;
            heightVelocity = vy;
            groundedSinceRelease = false;
            DeflectedSinceRelease = false;
            LastReleaseWasThrow = true;
            state = State.Thrown;
            RecordRelease(power);
            OnReleased?.Invoke(recentThrower, IntendedTarget, true);
        }

        /// <summary>
        /// Parametric pass to a fixed world position. Lateral motion is a
        /// straight lerp from current position to target; lob adds a sin-arc
        /// on top of carryHeight, chest stays flat. Catches resolve via the
        /// normal proximity pickup so anyone on the path can intercept.
        /// </summary>
        public void Pass(Vector2 target, float lateralSpeed, bool isLob)
        {
            if (carrier == null || !CarrierMayRelease()) return;

            Vector2 start = transform.position;
            Vector2 toTarget = target - start;
            float dist = toTarget.magnitude;
            if (dist < 0.01f) return;

            Team passerTeam = carrier.Spawn.team;

            recentThrower = carrier;
            throwerCooldownRemaining = throwerPickupCooldown;
            carrier.HeldBall = null;
            carrier = null;
            carrierMovement = null;

            // Long passes are thrown harder so they don't float across the court.
            float effectiveSpeed = Mathf.Min(lateralSpeed + passSpeedPerUnit * dist, maxPassSpeed);

            // Lobs arc higher the further they travel — and higher still when an
            // opponent stands in the lane, so the pass clears them.
            float apex = 0f;
            if (isLob)
            {
                apex = lobApex + lobApexPerUnit * dist;
                if (OpponentInLane(start, target, passerTeam)) apex = Mathf.Max(apex, lobClearanceApex);
                apex = Mathf.Min(apex, maxLobApex);
            }

            passStart = start;
            passEnd = target;
            passDurationCurrent = dist / Mathf.Max(0.01f, effectiveSpeed);
            passTimer = 0f;
            passIsLob = isLob;
            passApex = apex;
            // Capture the visual height at the moment of release so the
            // trajectory's baseline starts there, not at carryHeight.
            passLaunchHeight = Height;
            groundedSinceRelease = false;
            DeflectedSinceRelease = false;
            LastReleaseWasThrow = false;
            state = State.Passing;

            rb.simulated = false;
            RecordRelease(effectiveSpeed);
            OnReleased?.Invoke(recentThrower, IntendedTarget, false);
        }

        // True if an opponent (other team) stands within lobLaneRadius of the
        // pass segment, between passer and target — someone a lob should clear.
        private bool OpponentInLane(Vector2 start, Vector2 end, Team passerTeam)
        {
            Vector2 seg = end - start;
            float segLen = seg.magnitude;
            if (segLen < 0.01f) return false;
            Vector2 dir = seg / segLen;

            var trackers = PlayerZoneTracker.All;
            for (int i = 0; i < trackers.Count; i++)
            {
                var t = trackers[i];
                if (t == null || t.Spawn.team == passerTeam) continue;
                Vector2 toP = (Vector2)t.transform.position - start;
                float along = Vector2.Dot(toP, dir);
                if (along <= 0.5f || along >= segLen - 0.5f) continue;   // behind passer or past target
                float perp = Vector2.Distance(start + dir * along, t.transform.position);
                if (perp <= lobLaneRadius) return true;
            }
            return false;
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
