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

        /// <summary>How a throw's intended target evaded it (for the play-by-play log).</summary>
        public enum DodgeKind { None, Duck, Jump, Dive }

        public enum State { Carried, Passing, Thrown, Bouncing, Loose }

        /// <summary>Weights for the catch skill-check. All bonuses/penalties are additive on a 0..1 chance.</summary>
        [System.Serializable]
        public class CatchTuning
        {
            // Catch is a deterministic timing check — no RNG for a human. The press's
            // timingScore (1 = right at arrival, → 0 at the edge of the arm window) is
            // compared to a CLEAN bar and a BOBBLE bar. Catch Technique LOWERS the bars
            // (softer hands = more timing slop); ball speed and bad facing/stance RAISE
            // them; facing fully away blocks a clean catch outright.
            public float comfortableSpeed = 8f;   // u/s at/below which speed doesn't tighten the window
            public float maxSpeed = 24f;          // u/s at/above which speed tightening is full

            [Header("Clean-catch timing bar (timingScore needed)")]
            [Range(0f, 1f)] public float cleanBarAtRating0  = 0.85f;  // weak hands: near-perfect timing
            [Range(0f, 1f)] public float cleanBarAtRating20 = 0.35f;  // soft hands: forgiving
            [Range(0f, 1f)] public float bobbleBand         = 0.15f;  // bobble zone width below the clean bar (narrow → a badly mistimed catch falls past it to a hit)
            [Range(0f, 1f)] public float speedTighten       = 0.25f;  // a max-speed ball raises the bar this much
            [Range(0f, 1f)] public float sideFacingTighten  = 0.30f;  // catching off to the side raises the bar
            [Range(0f, 1f)] public float stanceTighten      = 0.15f;  // flat-footed human (no stance) raises the bar
            [Range(0f, 1f)] public float catchSuccessCap    = 0.95f;  // nothing is 100%: even a sure catch flubs to a bobble this often (before ability mods)
            [Range(0f, 1f)] public float bobbleHardThrough  = 0.30f;  // at MAX ball speed, a bobble punches through to a hit this often (scaled down by speed → slow lobs ~never hit); the throw-vs-catch lethality dial
            [Range(0f, 1f)] public float bobbleDamageMul    = 0.33f;  // a mishandle (bobble) deals this fraction of a direct connect's damage/stamina — it still glanced off you

            [Header("AI simulated press (no real button)")]
            [Range(0f, 1f)] public float aiTimingAtRating0  = 0.55f;  // a weak AI's typical timingScore
            [Range(0f, 1f)] public float aiTimingAtRating20 = 1.00f;  // an elite AI's typical timingScore
            [Range(0f, 1f)] public float aiTimingNoise      = 0.12f;  // ± wobble on the AI's timing

            [Header("Bobble")]
            public float bobbleKeepFraction = 0.25f;  // fraction of the incoming ball speed a tipped ball keeps (a hot throw squirts off harder)
            public float bobblePopUp        = 3f;      // base upward pop off the hands (randomized ±50% per bobble)
        }

        public enum CatchZone { Miss, Bobble, Clean }

        /// <summary>Per-term breakdown of a catch resolution, for HUD / debug display.</summary>
        public struct CatchFactors
        {
            public bool valid;
            public float catching01;       // catcher Catch Technique (0..1)
            public float ballSpeed;        // u/s
            public float speedT;           // 0..1 speed tightening
            public float facingAlignment;  // -1..1 (+1 = head-on into the ball)
            public bool  backFacing;       // facing away → no clean catch
            public bool  inStance;         // catcher set in a defensive stance
            public bool  armed;            // catch press active
            public bool  human;            // human (real timing) vs AI (simulated)
            public float timingScore;      // 0..1 press precision used in the check
            public float cleanBar;         // timingScore needed for a clean catch
            public float bobbleBar;        // timingScore needed for at least a bobble
            public CatchZone zone;         // resolved outcome
            public float finalChance;      // 0..1 catch quality (display + recovery difficulty)
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
        [Tooltip("How near a thrown ball must pass the intended target to register a duck/jump/dive in the play-by-play log.")]
        [SerializeField] private float dodgeSampleRadius = 1.5f;

        [Header("Catch (skill check)")]
        [Tooltip("A human-controlled player must press Catch within this window (seconds) before the ball arrives.")]
        [SerializeField] private float catchArmWindow = 0.35f;
        [Tooltip("Loose-ball speed above which securing it needs a skill catch rather than a free pickup.")]
        [SerializeField] private float skillSpeedThreshold = 3f;
        [SerializeField] private CatchTuning catchTuning = new CatchTuning();

        [Header("Physics")]
        [Tooltip("Air drag while the ball is in FLIGHT (Thrown / Passing). Near-zero = " +
                 "throws hold their speed and carry instead of petering out. Also feeds the " +
                 "drag-aware flight-time / lead-aim math.")]
        [SerializeField] private float flightDamping = 0.15f;
        [Tooltip("Rolling friction once the ball is LOOSE on the floor, so it slows and " +
                 "settles (and the delay-of-game alarm can fire). Stays firm even when " +
                 "flight drag is ~0 — decoupled from air drag on purpose.")]
        [SerializeField] private float groundDamping = 2.8f;
        [Tooltip("Speed kept when bouncing off a boundary wall (0 = dead stop, 1 = perfectly elastic).")]
        [SerializeField, Range(0f, 1f)] private float wallRestitution = 0.45f;

        [Header("Height (measured above the visible floor)")]
        [Tooltip("Local Y offset from ball.transform.position down to the visible floor. " +
                 "Match the player's FootOffset so a Height = 0 ball renders at foot level.")]
        [SerializeField] private float floorOffsetY = -0.79f;
        [Tooltip("Ball Height while carried (chest level, above feet).")]
        [SerializeField] private float carryHeight = 1.29f;
        public float lobApex = 0.5f;
        [Tooltip("Extra lateral pass speed (u/s) per unit of distance — long passes are thrown harder.")]
        [SerializeField] private float passSpeedPerUnit = 0.35f;
        [Tooltip("Cap on lateral pass speed (u/s) after distance scaling.")]
        [SerializeField] private float maxPassSpeed = 24f;
        [Tooltip("Extra lob apex (u) per unit of pass distance. 0.15 gives a 13 m outfielder→infielder lob a ~2 u natural apex before any clearance bump — visible arc, steeper launch.")]
        public float lobApexPerUnit = 0.15f;
        [Tooltip("Lob apex floor (u) when an opponent stands in the pass lane. 3.5 is above a jumping defender's reach (~3 u) so they can't intercept at the midpoint of the arc. Capped by maxLobApex — keep maxLobApex ≥ this value or the clearance bump gets clipped right back down.")]
        public float lobClearanceApex = 3.5f;
        [Tooltip("Maximum lob apex (u) after scaling. 3.5 lets long outfielder→infielder lobs clear defender jump-reach (~3 u) at the midpoint while still capping the moonball case.")]
        public float maxLobApex = 3.5f;
        [Tooltip("Half-width (u) of the lob lane used to detect opponents to clear. 2.0 matches DodgeballAI.outfielderPassLaneRadius so the AI's 'lane blocked → lob' decision and the Ball's 'apply clearance' decision agree on what counts as an in-lane defender.")]
        public float lobLaneRadius = 2.0f;
        [Tooltip("Lateral-speed multiplier applied to lobs after distance scaling. <1 makes lobs floatier. 1.0 = same speed as a chest pass arrival.")]
        public float lobLateralSpeedMul = 1.0f;
        [Tooltip("Constant downward acceleration applied to Height in the Thrown state (units/sec^2).")]
        [SerializeField] private float gravity = 12f;
        [Tooltip("Gravity multiplier applied ONLY to a throw's initial flight (before the first ground touch). <1 makes attacks float longer / travel flatter for game-feel. Lobs, chest passes, and post-bounce rolling all use full gravity — this knob only affects the airtime of a launched throw.")]
        [Range(0.1f, 1f)] [SerializeField] private float throwGravityMul = 0.4f;

        [Header("Throw release speed (rating → u/s)")]
        [Tooltip("Release speed at Throwing Speed rating 0 — the floor; even a poor arm throws with this much pace.")]
        [SerializeField] private float minReleaseSpeed = 12f;
        [Tooltip("Release speed at Throwing Speed rating 20 — the ceiling. The rating maps LINEARLY from min to max " +
                 "(each rating point = (max-min)/20 u/s). One shared mapping for AI and human throws.")]
        [SerializeField] private float maxReleaseSpeed = 36f;

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
        [SerializeField, Range(0f, 1f)] private float bounceRestitution = 0.6f;
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

        /// <summary>True if the most recent release was a throw from an outfielder who was inside the opposing infield at the moment of release. The hit handler nullifies all effects (no points, no elimination, no damage, no sideline) of a "throw from inside" — passes from inside are unaffected since they don't score on contact anyway.</summary>
        public bool LastReleaseFromOpposingInfield { get; private set; }

        /// <summary>The thrower's intended target at release (set by the controller), for the play-by-play log. May be null (e.g. an untargeted throw).</summary>
        public PlayerZoneTracker IntendedTarget { get; set; }

        /// <summary>The last player the ball caromed off since release (null if none) — for the play-by-play "deflects off of X".</summary>
        public PlayerZoneTracker LastDeflector { get; private set; }

        /// <summary>How the intended target evaded a throw that passed near them (None until a duck/jump/dive is seen). For the play-by-play log.</summary>
        public DodgeKind LastTargetDodge { get; private set; }

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
        public bool LastCatchSucceeded { get; private set; }
        public float LastCatchTime { get; private set; } = -999f;

        // Most recent throw's release vs arrival metrics — for the HUD.
        public ThrowTelemetry LastThrow { get; private set; }

        /// <summary>Fires whenever the ball attaches to a player (pickup, pass catch, or ForcePickup).</summary>
        public event System.Action<PlayerZoneTracker> OnAttached;

        /// <summary>Fires when a thrown ball caroms off an opponent. Args: hit player, zone, impact speed (u/s).</summary>
        public event System.Action<PlayerZoneTracker, HitZone, float> OnHit;

        /// <summary>
        /// Fires the instant a throw makes contact with a defender — a body connect
        /// (carom/deflect) OR a mishandle (bobble) — BEFORE the rebound is resolved.
        /// Carries the damage + stamina effect, which lands at impact and is never
        /// undone by a later catch (unlike <see cref="OnHit"/>, which is deferred and
        /// governs points/possession + the catch-save). Args: victim, zone, impact
        /// speed (u/s), contactMul (1 = direct connect, &lt;1 = glancing mishandle).
        /// </summary>
        public event System.Action<PlayerZoneTracker, HitZone, float, float> OnImpact;

        /// <summary>Fires when a player skill-catches an opponent's throw. Args: catcher.</summary>
        public event System.Action<PlayerZoneTracker> OnCaught;

        /// <summary>Fires on release. Args: thrower, intended target (may be null), isThrow (vs pass). For the play-by-play log.</summary>
        public event System.Action<PlayerZoneTracker, PlayerZoneTracker, bool> OnReleased;

        /// <summary>Fires when the ball settles to a loose ball (a play ended without a catch/possession). For the play-by-play log.</summary>
        public event System.Action OnBecameLoose;

        /// <summary>Fires when a player touches the ball while in the opposing team's half (out of their area). Args: the toucher. Counts as a hit on them — the opposing team scores +1 and the touch earns no catch credit.</summary>
        public event System.Action<PlayerZoneTracker> OnViolationTouch;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.linearDamping = groundDamping;   // starts Loose; release paths switch to flightDamping
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

        // While a throw is in flight, note if its intended target ducks / jumps /
        // dives as the ball passes near them — surfaced as the "miss" reason in
        // the play-by-play log. Keeps the strongest evade seen; only read on a miss.
        private void SampleTargetDodge()
        {
            if (IntendedTarget == null) return;
            Vector2 d = (Vector2)IntendedTarget.transform.position - (Vector2)transform.position;
            if (d.sqrMagnitude > dodgeSampleRadius * dodgeSampleRadius) return;
            var m = IntendedTarget.GetComponent<PlayerMovement>();
            if (m == null) return;
            if (m.IsDiving) LastTargetDodge = DodgeKind.Dive;
            else if (m.IsAirborne) LastTargetDodge = DodgeKind.Jump;
            else if (m.IsDucking) LastTargetDodge = DodgeKind.Duck;
        }

        private void UpdateThrown()
        {
            // Gravity pulls the ball down continuously. Before the first
            // ground touch, throwGravityMul lightens the pull so attacks
            // float across the court rather than dropping like rocks
            // (game-feel knob). Once the ball has bounced once,
            // groundedSinceRelease is set and we revert to full gravity
            // so bounces and rolls behave normally.
            float gNow = groundedSinceRelease ? gravity : gravity * throwGravityMul;
            heightVelocity -= gNow * Time.deltaTime;
            Height += heightVelocity * Time.deltaTime;
            SampleTargetDodge();

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
            // Ball settles to the floor and rolls; groundDamping does the friction work.
            if (Height > 0f)
                Height = Mathf.MoveTowards(Height, 0f, looseFallRate * Time.deltaTime);
            TryPickup();
        }

        private void EnterLoose()
        {
            ConfirmPendingHits();   // settled without a catch — any pending hits stand
            state = State.Loose;
            rb.simulated = true;
            rb.linearDamping = groundDamping;   // firm friction so the loose ball slows and settles
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
            // Referee is mid-handoff — no proximity pickups during the pause.
            // The recipient receives the ball via ForcePickup when the
            // transfer timer expires.
            if (DodgeballMatch.RefereeTransferActive) return;
            // No top height guard: a jumping defender extends their catch reach,
            // so the per-player check inside TryTakeBall is what decides.
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
                //
                // Exception: a player who was just forced to drop the ball by the
                // opp-infield rules is locked out of pickup until they leave or
                // jump — otherwise they'd re-grab the ball at their feet and
                // loop drop/re-pickup once per frame.
                if (t.PickupLockedInOppInfield) return false;
                if (Height <= PickupHeightFor(t)) { Attach(t); return true; }
                return false;
            }

            bool human = IsHumanControlled(t);
            // AI only skill-catches when defending an opponent's throw; teammate
            // passes / loose balls auto-catch (keeps passing + control transfer).
            bool deliberate = human || (caromOnMiss && HasAI(t));

            // A catch needs the ball within reach height.
            if (deliberate && t.CanCatchBall() && t.IsCatchArmed(catchArmWindow) && Height <= PickupHeightFor(t))
            {
                RecordArrival();
                var f = BuildCatchFactors(t);
                RecordCatchAttempt(f);
                if (f.zone == CatchZone.Clean) { Attach(t); return true; }
                ResolveMiss(t, bobble: f.zone == CatchZone.Bobble);
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
            if (!human && t.CanCatchBall() && Height <= PickupHeightFor(t)) { RecordArrival(); Attach(t); return true; }
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

        // A jumping player extends their vertical catch reach by their current
        // jump height — lets a defender leap to intercept a slightly overhead lob.
        private float PickupHeightFor(PlayerZoneTracker t)
        {
            var m = t.GetComponent<PlayerMovement>();
            return pickupMaxHeight + (m != null ? m.CurrentJumpHeight : 0f);
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
        /// Resolves a catch into Clean / Bobble / Miss from the press timing vs a
        /// Catch-Technique-sized window (no RNG for a human). Ball speed and bad
        /// facing/stance tighten the window; facing away blocks a clean catch. AI
        /// catchers (no real button) get a skill-simulated timingScore. Also used as
        /// a deterministic preview for the HUD.
        /// </summary>
        public CatchFactors BuildCatchFactors(PlayerZoneTracker catcher, bool preview = false)
        {
            var f = new CatchFactors { valid = catcher != null };
            if (catcher == null) return f;

            var catchAttr = catcher.GetComponent<DodgeballAttributes>();
            f.catching01 = catchAttr != null ? catchAttr.EffectiveCatching01 : 0.6f;
            f.human = IsHumanControlled(catcher);
            var move = catcher.GetComponent<PlayerMovement>();

            // Ball speed tightens the window — a fast ball is inherently harder
            // because you get less margin. (Replaces the old flat speed penalty.)
            f.ballSpeed = rb.linearVelocity.magnitude;
            f.speedT = Mathf.Clamp01(
                (f.ballSpeed - catchTuning.comfortableSpeed) /
                Mathf.Max(0.01f, catchTuning.maxSpeed - catchTuning.comfortableSpeed));

            // Facing: catcher's Facing vs the incoming ball direction. facingAlignment
            // = +1 looking straight at it, -1 away. Front 90° cone is fine; the side
            // wedges tighten the bar; the back half blocks a clean catch entirely.
            Vector2 vel = rb.linearVelocity;
            if (vel.sqrMagnitude < 0.0001f && state == State.Passing) vel = passEnd - passStart;
            bool side = false;
            if (vel.sqrMagnitude < 0.0001f) { f.facingAlignment = 0f; }
            else
            {
                Vector2 facing = move != null ? move.Facing : Vector2.right;
                f.facingAlignment = -Vector2.Dot(facing.normalized, vel.normalized);
                if (f.facingAlignment < 0f)               f.backFacing = true;   // back half
                else if (f.facingAlignment < 0.7071068f)  side = true;           // side eighths
            }

            // Flat-footed (not in a defensive stance) tightens the bar.
            f.inStance = move != null && move.InDefensiveStance;

            // Clean bar: Catch Technique lowers it; speed / side-facing / no-stance raise it.
            // The flat-footed penalty is human-only — an AI defender is always "set"
            // (it can't toggle a stance, and it deliberately armed the catch).
            f.cleanBar = Mathf.Lerp(catchTuning.cleanBarAtRating0, catchTuning.cleanBarAtRating20, f.catching01)
                       + f.speedT * catchTuning.speedTighten
                       + (side ? catchTuning.sideFacingTighten : 0f)
                       + ((f.human && !f.inStance) ? catchTuning.stanceTighten : 0f);
            f.bobbleBar = Mathf.Max(0f, f.cleanBar - catchTuning.bobbleBand);

            // Timing precision. Human = real press (1 right at arrival, → 0 at the edge
            // of the arm window). AI = simulated from skill, with a little wobble.
            f.armed = catcher.IsCatchArmed(catchArmWindow);
            if (f.human)
            {
                float sincePress = Time.time - catcher.CatchArmedAt;
                f.timingScore = Mathf.Clamp01(1f - sincePress / Mathf.Max(0.01f, catchArmWindow));
            }
            else
            {
                f.timingScore = Mathf.Clamp01(
                    Mathf.Lerp(catchTuning.aiTimingAtRating0, catchTuning.aiTimingAtRating20, f.catching01)
                    + (preview ? 0f : Random.Range(-catchTuning.aiTimingNoise, catchTuning.aiTimingNoise)));
            }

            // Resolve the zone. Facing away can never be a clean catch.
            if (!f.backFacing && f.timingScore >= f.cleanBar) f.zone = CatchZone.Clean;
            else if (f.timingScore >= f.bobbleBar)            f.zone = CatchZone.Bobble;
            else                                              f.zone = CatchZone.Miss;

            // A hard throw that beats the hands punches a bobble through into a hit:
            // the bobble edge thins as the ball speeds up, so soft lobs stay bobble-
            // safe (a weak thrower can't draw blood) while heat does — and it scales
            // with the throw-vs-catch speed gap. Resolution only; preview is un-punched.
            if (f.zone == CatchZone.Bobble && !preview
                && Random.value < catchTuning.bobbleHardThrough * f.speedT)
                f.zone = CatchZone.Miss;

            // Nothing is a 100% deal: even a sure catch flubs to a bobble now and then.
            // Caps catch success at catchSuccessCap (before ability mods). Runs after
            // the punch-through so a flubbed-clean stays a soft bobble, never a hit.
            // Resolution only — the preview shows the un-flubbed expected zone.
            if (f.zone == CatchZone.Clean && !preview && Random.value > catchTuning.catchSuccessCap)
                f.zone = CatchZone.Bobble;

            // Display / recovery-difficulty proxy: how far past the bobble bar toward
            // perfect (1 = clean with margin, 0 = on the edge of a bobble).
            f.finalChance = Mathf.Clamp01((f.timingScore - f.bobbleBar) / Mathf.Max(0.01f, 1f - f.bobbleBar));
            return f;
        }

        /// <summary>Deterministic catch preview for HUD / debug (no AI timing noise).</summary>
        public CatchFactors PreviewCatch(PlayerZoneTracker catcher) => BuildCatchFactors(catcher, preview: true);

        private void RecordCatchAttempt(CatchFactors f)
        {
            LastCatchFactors = f;
            LastCatchSucceeded = f.zone == CatchZone.Clean;
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

        // A non-clean catch. A bobble (near miss) is tipped loose at the catcher's
        // feet — recoverable. A harder miss caroms or deflects off them (can hit).
        private void ResolveMiss(PlayerZoneTracker catcher, bool bobble)
        {
            rb.simulated = true;
            Vector2 v = rb.linearVelocity;
            if (v.sqrMagnitude < 0.01f)
            {
                // From a parametric pass — synthesize a direction to react with.
                Vector2 dir = (passEnd - passStart);
                v = dir.sqrMagnitude > 0.0001f ? dir.normalized * 6f : Vector2.right * 6f;
            }

            // Bobble → fumble at the feet (case 1). Miss → carom (case 0, 60%) or
            // deflect backward (case 2). Keeps clean catches deterministic while the
            // failure flavor stays chaotic.
            int outcome = bobble ? 1 : (Random.value < 0.6f ? 0 : 2);
            switch (outcome)
            {
                case 0: // carom off the catcher (a "hit")
                    rb.linearVelocity = v;
                    Carom(catcher, ClassifyHit(Height));
                    break;
                case 1: // bobble — a flubbed catch off the hands/arms. Deflects in a varied
                        // direction, keeps a FRACTION of the incoming pace (a hot throw squirts
                        // off harder than a soft one), and pops up catchable. A mishandle still
                        // deals CHIP damage (it glanced off you); re-grabbable by the catcher or
                        // a teammate after the brief cooldown.
                    OnImpact?.Invoke(catcher, ClassifyHit(Height), v.magnitude, catchTuning.bobbleDamageMul);
                    rb.linearVelocity = (-v.normalized + Random.insideUnitCircle * 0.8f).normalized
                                        * v.magnitude * catchTuning.bobbleKeepFraction * Random.Range(0.7f, 1.3f);
                    rb.linearDamping = groundDamping;            // high drag → settles loose, not flung far
                    heightVelocity = catchTuning.bobblePopUp * Random.Range(0.5f, 1.5f);   // varied pop off the hands
                    groundedSinceRelease = true;                 // full (snappy) gravity + no scoring catch
                    recentThrower = catcher;                     // no instant re-grab / re-catch
                    throwerCooldownRemaining = throwerPickupCooldown;
                    state = State.Thrown;
                    break;
                default: // deflect backward — the ball still CONNECTED (beat the hands), so it
                         // deals full connect damage; the backward glance is only direction.
                    OnImpact?.Invoke(catcher, ClassifyHit(Height), v.magnitude, 1f);
                    rb.linearVelocity = -v * 0.5f;
                    heightVelocity = Mathf.Max(heightVelocity, 2.5f);
                    groundedSinceRelease = true;                 // full (snappy) gravity, like a bobble
                    recentThrower = catcher;                     // no instant re-grab of one's own deflection
                    throwerCooldownRemaining = throwerPickupCooldown;
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
            LastDeflector = hit;   // a deflection — for the play-by-play log
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

            // Damage + stamina land NOW, at the connect — a direct body hit (contactMul
            // 1) — and are never undone by a later catch (that's the whole point of the
            // damage game). Distinct from the deferred OnHit below, which governs only
            // points/possession + the catch-save.
            OnImpact?.Invoke(hit, zone, incoming.magnitude, 1f);

            // Defer the SCORING hit: record it (one entry per distinct player) instead
            // of firing OnHit now. It only counts once the ball touches the floor; if
            // the victim's team catches it first, every hit collected this airborne
            // segment is wiped (the points-game save). Repeated touches count once.
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
            // Touching the ball while grounded in the opposing team's half (out
            // of your assigned area) is a hit on YOU — even a "catch" — so you
            // don't get the catch credit and the opposing team scores. You still
            // hold the ball briefly (the carrier-out-of-zone timer then forces a
            // drop). Airborne touches (jumps / dives over the line) are exempt.
            bool inOpposingHalf = t.Spawn.team == Team.A
                ? t.transform.position.x > 0f
                : t.transform.position.x < 0f;
            var tm = t.GetComponent<PlayerMovement>();
            bool grounded = tm == null || tm.IsGrounded;
            if (!t.IsInZone && inOpposingHalf && grounded)
            {
                ConfirmPendingHits();   // prior in-air hits commit (no save)
                AttachTo(t);
                OnViolationTouch?.Invoke(t);   // +1 opposing team, no OnCaught
                return;
            }

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
            if (countsAsCatch)
            {
                // 1 - finalChance = how hard this catch was (already factors the
                // catcher's skill — a great catcher's tough grab still has a
                // higher chance). Harder grab → longer fumble/settle.
                float difficulty = 1f - Mathf.Clamp01(LastCatchFactors.finalChance);
                t.GetComponent<PlayerMovement>()?.BeginCatchRecovery(difficulty);
                OnCaught?.Invoke(t);
            }
        }

        private void AttachTo(PlayerZoneTracker t)
        {
            carrier = t;
            carrierMovement = t != null ? t.GetComponent<PlayerMovement>() : null;
            t.HeldBall = this;
            rb.linearVelocity = Vector2.zero;
            rb.simulated = false;
            // Snap to hand height NOW (not next UpdateCarried) so a throw fired the
            // same frame as the pickup releases from chest, not from the feet.
            Height = carryHeight + (carrierMovement != null ? carrierMovement.CurrentJumpHeight : 0f);
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
        /// The carrier loses the ball. recentThrower is cleared so a pickup
        /// off it isn't scored as a catch (groundedSinceRelease=true). The
        /// optional initialVelocity transfers the carrier's momentum to the
        /// ball — used by the opp-infield rule so the release looks like a
        /// release (ball slides away with the player's motion) rather than
        /// freezing in place. Default (zero) preserves the old "drops where
        /// they stand" behavior for voluntary ineligible-release drops.
        /// </summary>
        public void Drop(Vector2 initialVelocity = default)
        {
            if (carrier == null) return;
            carrier.HeldBall = null;
            carrier = null;
            carrierMovement = null;
            recentThrower = null;
            throwerCooldownRemaining = 0f;
            rb.linearVelocity = initialVelocity;
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
            LastReleaseFromOpposingInfield = false;
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
        /// <summary>
        /// Release speed (u/s) for a thrower whose effective Throwing Speed is
        /// <paramref name="throwSpeed01"/> (0..1) — a straight line from
        /// minReleaseSpeed (rating 0) to maxReleaseSpeed (rating 20). One shared
        /// mapping so AI and human throws can't drift apart.
        /// </summary>
        public float ReleaseSpeed(float throwSpeed01)
        {
            return Mathf.Lerp(minReleaseSpeed, maxReleaseSpeed, Mathf.Clamp01(throwSpeed01));
        }

        public void Throw(Vector2 direction, float power, float verticalVelocity = 0f)
        {
            if (carrier == null) return;
            if (!CarrierMayRelease()) { Drop(); return; }   // pressing throw while ineligible drops the ball
            if (direction.sqrMagnitude < 0.0001f) direction = Vector2.right;

            // Capture carrier momentum BEFORE nulling carrierMovement — running
            // throws hit harder because the carrier's lateral velocity is added
            // to the release. A stationary throw adds zero, so this is a
            // strict upgrade for anyone moving forward through the shot.
            Vector2 carrierVel = carrierMovement != null ? carrierMovement.Velocity : Vector2.zero;

            recentThrower = carrier;
            throwerCooldownRemaining = throwerPickupCooldown;
            // Phase D: neutered if an outfielder released from inside the opposing infield.
            LastReleaseFromOpposingInfield = recentThrower.Spawn.role == PlayerRole.Outfielder
                                          && recentThrower.IsInOpposingInfield;
            carrier.HeldBall = null;
            carrier = null;
            carrierMovement = null;

            rb.simulated = true;
            rb.linearDamping = flightDamping;   // in flight: near-zero air drag so the throw carries
            rb.linearVelocity = direction.normalized * power + carrierVel;
            heightVelocity = verticalVelocity;
            groundedSinceRelease = false;
            LastDeflector = null;
            LastTargetDodge = DodgeKind.None;
            LastReleaseWasThrow = true;
            state = State.Thrown;
            // Record the ACTUAL release speed (carrier momentum included), so the lab's
            // "spd" differs from the commanded "pow" on running / crow-hop throws.
            RecordRelease(rb.linearVelocity.magnitude);
            OnReleased?.Invoke(recentThrower, IntendedTarget, true);
        }

        /// <summary>
        /// Throw at a target position. Computes the vertical velocity that
        /// would land the ball at <paramref name="targetHeight"/> after the
        /// lateral flight time, then clamps it to be non-positive so the
        /// ball never arcs UP off the thrower's hand.
        ///
        /// <paramref name="targetHeight"/>: pass a negative number (or
        /// omit) to default to carryHeight (chest). The AI passes a lower,
        /// scattered value for jump attacks so spikes land at waist height
        /// and may go negative on a wild miss — driving the ball into the
        /// ground short of the target instead of always arriving at chest.
        ///
        /// Net result:
        ///   - Standing / running throw → vy = 0 → flat with gravity droop.
        ///   - Jump / dive throw → vy negative → spike descending from apex.
        /// Long throws may hit the ground before reaching the target and
        /// bounce / roll the rest of the way.
        /// </summary>
        public void ThrowAt(Vector2 targetPos, float power, float targetHeight = -1f)
        {
            if (carrier == null) return;
            if (targetHeight < 0f) targetHeight = carryHeight;

            Vector2 toTarget = targetPos - (Vector2)transform.position;
            float dist = toTarget.magnitude;
            if (dist < 0.01f)
            {
                Throw(Vector2.right, power);
                return;
            }

            Vector2 dir = toTarget / dist;
            float t = FlightTime(dist, power);
            // Use the SAME reduced gravity that the integrator will apply
            // pre-bounce so the math agrees with what the ball actually does.
            float gNow = gravity * throwGravityMul;
            float vy = (targetHeight - Height + 0.5f * gNow * t * t) / t;
            vy = Mathf.Min(vy, 0f);   // never throw UP off the thrower's hand — flat or descending only
            Throw(dir, power, vy);
        }

        /// <summary>
        /// Estimated lateral time-of-flight to cover <paramref name="dist"/> at
        /// release speed <paramref name="power"/>, accounting for flightDamping
        /// (the ball decelerates, so this is longer than dist/power). Drag-free
        /// and beyond-max-range cases fall back to dist/power.
        ///
        /// Under exponential decay v(t) = v0·e^(-k·t), distance covered is
        /// (v0/k)(1 - e^(-k·t)); solving for t gives the closed form below.
        /// </summary>
        public float FlightTime(float dist, float power)
        {
            float v0 = Mathf.Max(0.01f, power);
            float k = flightDamping;
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
            // Pre-bounce flight uses the reduced throw gravity; once
            // groundedSinceRelease is true the ball is bouncing and the
            // defender prediction shouldn't fire anyway (defenders react to
            // direct shots, not rollers).
            float gNow = groundedSinceRelease ? gravity : gravity * throwGravityMul;
            return Mathf.Max(0f, Height + heightVelocity * t - 0.5f * gNow * t * t);
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
            float k = flightDamping;
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
            // Phase D: neutered if an outfielder released from inside the opposing infield.
            LastReleaseFromOpposingInfield = recentThrower != null
                                          && recentThrower.Spawn.role == PlayerRole.Outfielder
                                          && recentThrower.IsInOpposingInfield;
            rb.simulated = true;
            rb.linearDamping = flightDamping;   // in flight: near-zero air drag so the launch carries
            rb.linearVelocity = dir * power;
            heightVelocity = vy;
            groundedSinceRelease = false;
            LastDeflector = null;
            LastTargetDodge = DodgeKind.None;
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
            if (carrier == null) return;
            if (!CarrierMayRelease()) { Drop(); return; }   // pressing pass while ineligible drops the ball

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

            // Lobs travel laterally slower so the arc is floaty rather than a
            // quick pop-up. Vertical sin-arc duration is coupled to lateral,
            // so this slows the up-and-back-down motion equally.
            if (isLob) effectiveSpeed *= lobLateralSpeedMul;

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
            LastDeflector = null;
            LastTargetDodge = DodgeKind.None;
            LastReleaseWasThrow = false;
            LastReleaseFromOpposingInfield = false;   // passes never carry the neuter flag
            state = State.Passing;

            // Pass lateral motion is parametric (rb asleep), but pre-set the flight
            // drag so a pass later knocked airborne by a carom uses air drag, not
            // ground friction.
            rb.linearDamping = flightDamping;
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
