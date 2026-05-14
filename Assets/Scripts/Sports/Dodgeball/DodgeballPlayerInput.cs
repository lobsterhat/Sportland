using UnityEngine;
using UnityEngine.InputSystem;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Routes PS4 / keyboard input into a single human-controlled Dodgeball
    /// player. Move uses analog magnitude. L2 holds sprint. Triangle jumps.
    /// Circle throws. Cross passes (tap = lob, hold = chest). L1 force-returns
    /// the ball for testing.
    ///
    /// Player control is single-active: a static Current reference points at
    /// the live input component. When a pass arrives at the intended target,
    /// TransferControl destroys this component and re-attaches a fresh one to
    /// the receiver.
    /// </summary>
    [RequireComponent(typeof(PlayerMovement))]
    [RequireComponent(typeof(PlayerZoneTracker))]
    public class DodgeballPlayerInput : MonoBehaviour
    {
        public static DodgeballPlayerInput Current { get; private set; }

        [Header("Power")]
        [SerializeField] private float throwPower = 24f;
        [SerializeField] private float lobPassSpeed = 6f;
        [SerializeField] private float chestPassSpeed = 12f;

        [Header("Pass timing")]
        [Tooltip("Hold longer than this (seconds) for chest pass; release sooner for lob.")]
        [SerializeField] private float passTapThreshold = 0.18f;

        [Header("Throw aim assist")]
        [Tooltip("Half-angle of the throw cone (degrees). Opponents whose bearing from the thrower " +
                 "lies within this many degrees of the press direction are eligible targets; the " +
                 "nearest one becomes the actual throw target.")]
        [SerializeField] private float throwConeHalfAngleDegrees = 15f;

        [Header("Run mode (D-pad double-tap)")]
        [Tooltip("Seconds with no movement input before run mode disengages. " +
                 "Tiny grace lets D-pad rolls between directions stay running.")]
        [SerializeField] private float runReleaseGrace = 0.08f;

        private PlayerMovement movement;
        private PlayerZoneTracker tracker;
        private DodgeballInputActions actions;

        private Vector2 lastMoveDirection = Vector2.right;
        private float passPressTime = -1f;

        private bool isRunning;
        private float idleTime;

        // Pending pass we issued: the catch handler watches for this ball to
        // arrive at intendedPassTarget so it can hand control off.
        private Ball passedBall;
        private PlayerZoneTracker intendedPassTarget;

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            tracker = GetComponent<PlayerZoneTracker>();
            actions = new DodgeballInputActions();
            actions.Jump.performed       += OnJumpPressed;
            actions.Throw.performed      += OnThrowPressed;
            actions.Pass.started         += OnPassStarted;
            actions.Pass.canceled        += OnPassCanceled;
            actions.ReturnBall.performed += OnReturnBallPressed;
            actions.Run.performed        += OnRunDoubleTap;

            Current = this;
        }

        private void OnEnable()  => actions?.Enable();
        private void OnDisable() => actions?.Disable();

        private void OnDestroy()
        {
            if (actions != null)
            {
                actions.Jump.performed       -= OnJumpPressed;
                actions.Throw.performed      -= OnThrowPressed;
                actions.Pass.started         -= OnPassStarted;
                actions.Pass.canceled        -= OnPassCanceled;
                actions.ReturnBall.performed -= OnReturnBallPressed;
                actions.Run.performed        -= OnRunDoubleTap;
                actions.Disable();
            }
            if (passedBall != null) passedBall.OnAttached -= OnPassedBallCaught;
            if (Current == this) Current = null;
        }

        private void Update()
        {
            Vector2 input = actions.Move.ReadValue<Vector2>();
            if (input.sqrMagnitude > 0.04f)
            {
                lastMoveDirection = input.normalized;
                idleTime = 0f;
            }
            else
            {
                idleTime += Time.deltaTime;
                if (idleTime >= runReleaseGrace) isRunning = false;
            }

            movement.IsSprinting = isRunning || actions.Sprint.IsPressed();
            movement.ApplyMove(input);
        }

        private void OnJumpPressed(InputAction.CallbackContext _) => movement.TryJump();
        private void OnRunDoubleTap(InputAction.CallbackContext _) => isRunning = true;

        private void OnThrowPressed(InputAction.CallbackContext _)
        {
            var ball = tracker.HeldBall;
            if (ball == null) return;
            Vector2 dir = ResolveThrowDirection(lastMoveDirection);
            ball.Throw(dir, throwPower);
        }

        /// <summary>
        /// Aim-assist for throws: if any opponent's bearing from the thrower
        /// is within throwConeHalfAngleDegrees of the press direction, return
        /// the direction to the nearest one. Otherwise return the press
        /// direction unchanged.
        /// </summary>
        private Vector2 ResolveThrowDirection(Vector2 desired)
        {
            if (desired.sqrMagnitude < 0.0001f) desired = Vector2.right;
            Vector2 dirNorm = desired.normalized;

            float cosThreshold = Mathf.Cos(throwConeHalfAngleDegrees * Mathf.Deg2Rad);
            Vector2 origin = transform.position;
            var team = tracker.Spawn.team;

            PlayerZoneTracker best = null;
            float bestDistSq = float.MaxValue;

            var trackers = PlayerZoneTracker.All;
            for (int i = 0; i < trackers.Count; i++)
            {
                var t = trackers[i];
                if (t == null || t == tracker) continue;
                if (t.Spawn.team == team) continue;  // throw targets opponents only

                Vector2 toT = (Vector2)t.transform.position - origin;
                float distSq = toT.sqrMagnitude;
                if (distSq < 0.0001f) continue;
                if (Vector2.Dot(dirNorm, toT / Mathf.Sqrt(distSq)) < cosThreshold) continue;

                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = t;
                }
            }

            return best != null
                ? ((Vector2)best.transform.position - origin).normalized
                : dirNorm;
        }

        private void OnPassStarted(InputAction.CallbackContext _)
        {
            passPressTime = Time.unscaledTime;
        }

        private void OnPassCanceled(InputAction.CallbackContext _)
        {
            if (passPressTime < 0f) return;
            float duration = Time.unscaledTime - passPressTime;
            passPressTime = -1f;

            var ball = tracker.HeldBall;
            if (ball == null) return;

            DoPass(ball, isChest: duration >= passTapThreshold);
        }

        private void DoPass(Ball ball, bool isChest)
        {
            var target = FindPassTarget();
            if (target == null) return;

            // Clear any stale subscription before recording the new pass.
            if (passedBall != null) passedBall.OnAttached -= OnPassedBallCaught;
            passedBall = ball;
            intendedPassTarget = target;
            passedBall.OnAttached += OnPassedBallCaught;

            // Parametric drive — ball lerps to target's current position; lob
            // adds an arc, chest stays flat. Speed sets the flight duration.
            ball.Pass(target.transform.position,
                      isChest ? chestPassSpeed : lobPassSpeed,
                      isLob: !isChest);
        }

        private void OnPassedBallCaught(PlayerZoneTracker catcher)
        {
            if (passedBall == null) return;
            passedBall.OnAttached -= OnPassedBallCaught;

            var target = intendedPassTarget;
            passedBall = null;
            intendedPassTarget = null;

            // Only the intended teammate's catch triggers a control handoff;
            // interceptions leave control where it was.
            if (catcher == target)
            {
                TransferControl(catcher.gameObject);
            }
        }

        /// <summary>
        /// Picks the same-team teammate whose direction from the holder is
        /// most aligned with lastMoveDirection (cosine similarity).
        /// </summary>
        private PlayerZoneTracker FindPassTarget()
        {
            PlayerZoneTracker best = null;
            float bestScore = -2f;
            var team = tracker.Spawn.team;
            Vector2 origin = transform.position;

            var trackers = PlayerZoneTracker.All;
            for (int i = 0; i < trackers.Count; i++)
            {
                var t = trackers[i];
                if (t == null || t == tracker) continue;
                if (t.Spawn.team != team) continue;

                Vector2 toT = (Vector2)t.transform.position - origin;
                if (toT.sqrMagnitude < 0.0001f) continue;

                float score = Vector2.Dot(toT.normalized, lastMoveDirection);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = t;
                }
            }
            return best;
        }

        private void OnReturnBallPressed(InputAction.CallbackContext _)
        {
            var ball = FindAnyObjectByType<Ball>();
            if (ball != null) ball.ForcePickup(tracker);
        }

        /// <summary>
        /// Move human control to <paramref name="target"/>: tears down the
        /// current input component and adds a fresh one on the target.
        /// </summary>
        public static void TransferControl(GameObject target)
        {
            if (Current != null)
            {
                Current.actions?.Disable();
                Destroy(Current);
            }
            target.AddComponent<DodgeballPlayerInput>();
        }
    }
}
