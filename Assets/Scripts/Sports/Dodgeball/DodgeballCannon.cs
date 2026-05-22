using UnityEngine;
using UnityEngine.InputSystem;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Debug tool for exercising the catch mechanic. On C (keyboard) or R1
    /// (gamepad) — or on an optional auto-fire timer — it teleports the ball
    /// to a point offset from the human-controlled player and launches it at
    /// them. The ball is attributed to an opponent of the target as the
    /// "thrower", so the controlled player is a legal catch/hit target: press
    /// Catch (Circle / E) as it arrives to test the skill check; do nothing and
    /// it caroms off you (a hit).
    /// </summary>
    public class DodgeballCannon : MonoBehaviour
    {
        [SerializeField] private float firePower = 20f;
        [Tooltip("Where the ball starts, relative to the target. Default: 6 units to the +X side, flying in.")]
        [SerializeField] private Vector2 fireOffset = new Vector2(6f, 0f);
        [SerializeField] private bool autoFire = false;
        [SerializeField] private float autoFireInterval = 3f;

        private Ball ball;
        private float nextAutoFireTime;

        private void Update()
        {
            if (FireRequested()) Fire();

            if (autoFire && Time.time >= nextAutoFireTime)
            {
                Fire();
                nextAutoFireTime = Time.time + autoFireInterval;
            }
        }

        private static bool FireRequested()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.cKey.wasPressedThisFrame) return true;
            var gp = Gamepad.current;
            if (gp != null && gp.rightShoulder.wasPressedThisFrame) return true;
            return false;
        }

        private void Fire()
        {
            if (ball == null) ball = FindFirstObjectByType<Ball>();
            if (ball == null) return;

            var input = DodgeballPlayerInput.Current;
            if (input == null) return;
            var target = input.GetComponent<PlayerZoneTracker>();
            if (target == null) return;

            Vector2 targetPos = target.transform.position;
            Vector2 fromPos = targetPos + fireOffset;
            ball.LaunchFrom(fromPos, targetPos, firePower, FindOpponent(target));
        }

        // Any player on the other team — used as the virtual thrower so the
        // target counts as an opponent (a catchable / hittable shot).
        private static PlayerZoneTracker FindOpponent(PlayerZoneTracker of)
        {
            var all = PlayerZoneTracker.All;
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (t != null && t != of && t.Spawn.team != of.Spawn.team) return t;
            }
            return null;
        }
    }
}
