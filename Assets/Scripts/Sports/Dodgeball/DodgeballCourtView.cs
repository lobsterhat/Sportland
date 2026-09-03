using System.Collections.Generic;
using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Draws the live objects — players, the ball, their shadows and the debug
    /// markers — onto the angled court that <see cref="CourtProjection"/>
    /// defines. The static floor is drawn by <see cref="DodgeballCourtRenderer"/>
    /// through the same projection; this class handles everything that moves.
    ///
    /// The sim is never touched. Roots stay exactly where the physics put them,
    /// in flat metres; only their visual children are placed. Turning the
    /// projection off makes this a no-op and the flat top-down view returns.
    ///
    /// Two rules keep this from fighting the components that own these
    /// transforms (<see cref="PlayerMovement"/> writes the jump bob and duck
    /// squash, <see cref="DodgeballPlayerVisual"/> writes the shadow shrink and
    /// arrow orbits, <see cref="Ball"/> writes its own height and shadow):
    ///
    ///   1. Only ever set POSITION here, except for the upright sprites, whose
    ///      scale is multiplied by the depth scale — and that multiply is a
    ///      no-op at the default constant-size setting. Never overwrite a scale
    ///      outright, or the duck squash and the shadow's jump shrink vanish.
    ///   2. Run last. Execution order is above every writer, and everything is
    ///      recomputed from the authored values each frame rather than
    ///      accumulated, so nothing drifts.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class DodgeballCourtView : MonoBehaviour
    {
        // Cached child transforms for one projected entity. Looking these up by
        // name every frame for twelve players would be needless string work.
        private class Rig
        {
            public Transform Root;
            public Transform Visual;
            public Transform Shadow;
            public Transform MovementArrow;
            public Transform FacingArrow;
            public Transform ControlRing;
            public PlayerMovement Movement;
            public DodgeballPlayerVisual Visuals;

            // The ring is positioned once when it is built and never again, so
            // unlike everything else here it cannot restore itself when the
            // projection is switched off. Remember where it belongs.
            public Vector3 RingRestPosition;
        }

        // A flat list scanned by reference, rather than a dictionary. Keying a
        // dictionary on a Transform is a trap here: Unity's overloaded equality
        // reports every destroyed object as equal to every other, so two dead
        // players would collide on one entry. Keying on an id instead would
        // mean GetInstanceID, which Unity 6.5 rejects outright. With twelve
        // players a reference scan costs nothing and dodges both.
        private readonly List<Rig> rigs = new List<Rig>();

        private Ball ball;
        private DodgeballCannon cannon;
        private bool projectedLastFrame;

        private void LateUpdate()
        {
            if (!CourtProjection.Enabled)
            {
                if (projectedLastFrame) RestoreFlat();
                return;
            }
            projectedLastFrame = true;

            var all = PlayerZoneTracker.All;
            for (int i = 0; i < all.Count; i++)
            {
                var tracker = all[i];
                if (tracker == null) continue;
                ProjectPlayer(RigFor(tracker.transform));
            }

            if (ball == null) ball = FindAnyObjectByType<Ball>();
            if (ball != null) ProjectBall(ball);

            if (cannon == null) cannon = FindAnyObjectByType<DodgeballCannon>();
            if (cannon != null && cannon.Marker != null)
                cannon.Marker.position = CourtProjection.GroundWorld(cannon.Marker.position);

            PruneDestroyed();
        }

        private void OnDisable() => RestoreFlat();

        private void ProjectPlayer(Rig rig)
        {
            if (rig == null || rig.Root == null) return;

            Vector3 root = rig.Root.position;
            Vector2 ground = CourtProjection.Ground(root.x, root.y);
            float depthScale = CourtProjection.SpriteScale(root.y);

            // The sim's Y is the depth coordinate, and the flat view drew the
            // sprite straddling it — centre on the coordinate, feet hanging
            // FootOffset below. Projected, the coordinate IS the floor, so the
            // sprite is lifted to stand on it and the shadow sits right on it.
            // That is also why the floor mesh and the shadows finally agree.
            float footRise = rig.Movement != null ? -rig.Movement.FootOffset : 0.79f;
            float jump = rig.Movement != null ? rig.Movement.CurrentJumpHeight : 0f;

            if (rig.Visual != null)
            {
                float y = ground.y + footRise * depthScale + CourtProjection.Lift(jump, root.y);
                rig.Visual.position = new Vector3(ground.x, y, rig.Visual.position.z);
                if (depthScale != 1f)
                {
                    // PlayerMovement rewrote this from its authored base earlier
                    // in the frame (including any duck squash), so scaling it
                    // here composes rather than compounds.
                    Vector3 s = rig.Visual.localScale;
                    rig.Visual.localScale = new Vector3(s.x * depthScale, s.y * depthScale, s.z);
                }
            }

            PlaceOnGround(rig.Shadow, ground);
            PlaceOnGround(rig.ControlRing, ground);
            if (rig.Visuals != null)
            {
                ProjectOrbit(rig.MovementArrow, root, ground, rig.Visuals.MovementArrowOffset);
                ProjectOrbit(rig.FacingArrow, root, ground, rig.Visuals.FacingArrowOffset);
            }
        }

        private void ProjectBall(Ball b)
        {
            Vector3 p = b.transform.position;
            Vector2 ground = CourtProjection.Ground(p.x, p.y);

            var visual = b.transform.Find("Visual");
            if (visual != null)
            {
                float y = ground.y + CourtProjection.Lift(b.Height, p.y);
                visual.position = new Vector3(ground.x, y, visual.position.z);
            }
            // Scale is left alone so Ball's own height-falloff shrink survives.
            PlaceOnGround(b.transform.Find("Shadow"), ground);
        }

        private static void PlaceOnGround(Transform t, Vector2 ground)
        {
            if (t == null) return;
            t.position = new Vector3(ground.x, ground.y, t.position.z);
        }

        // The direction arrows orbit their player at a fixed radius on the
        // floor. Projecting the orbit rather than the arrow squashes it into an
        // ellipse, so "forward" points the right way and covers the right
        // distance at every depth. The old spike just hid these.
        //
        // The offset is passed in rather than read off the transform, because
        // the transform holds last frame's projected position — reading it back
        // would feed this its own output.
        private void ProjectOrbit(Transform arrow, Vector3 root, Vector2 ground, Vector2 offset)
        {
            if (arrow == null || !arrow.gameObject.activeSelf) return;

            Vector2 tip = CourtProjection.Ground(root.x + offset.x, root.y + offset.y);
            arrow.position = new Vector3(tip.x, tip.y, arrow.position.z);

            Vector2 dir = tip - ground;
            if (dir.sqrMagnitude > 0.000001f)
                arrow.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        }

        // Everything except the control ring is rewritten from scratch by its
        // owning component every Update, so dropping out of the projection is
        // enough for it to snap back on its own.
        private void RestoreFlat()
        {
            projectedLastFrame = false;
            for (int i = 0; i < rigs.Count; i++)
            {
                if (rigs[i].Root != null && rigs[i].ControlRing != null)
                    rigs[i].ControlRing.localPosition = rigs[i].RingRestPosition;
            }
        }

        private Rig RigFor(Transform root)
        {
            int slot = -1;
            for (int i = 0; i < rigs.Count; i++)
            {
                // ReferenceEquals, not ==: we want "the same managed object",
                // not Unity's notion of equality, which lies about dead ones.
                if (!ReferenceEquals(rigs[i].Root, root)) continue;
                if (IsComplete(rigs[i])) return rigs[i];
                slot = i;
                break;
            }

            var rig = new Rig
            {
                Root = root,
                Visual = root.childCount > 0 ? root.GetChild(0) : null,
                Shadow = root.Find("Shadow"),
                MovementArrow = root.Find("MovementArrow"),
                FacingArrow = root.Find("FacingArrow"),
                ControlRing = root.Find("ControlRing"),
                Movement = root.GetComponent<PlayerMovement>(),
                Visuals = root.GetComponentInChildren<DodgeballPlayerVisual>(),
            };
            if (rig.ControlRing != null) rig.RingRestPosition = rig.ControlRing.localPosition;
            if (slot >= 0) rigs[slot] = rig;
            else rigs.Add(rig);
            return rig;
        }

        // DodgeballPlayerVisual builds all of these in Configure, so a rig with
        // a missing piece was cached mid-spawn. Re-resolving until it is whole
        // costs one set of lookups on a player that arrives late (a career
        // fixture restat, an attack-lab swap) and none at all afterward.
        private static bool IsComplete(Rig rig)
        {
            return rig.Root != null
                && rig.Visual != null
                && rig.Shadow != null
                && rig.MovementArrow != null
                && rig.FacingArrow != null
                && rig.ControlRing != null
                && rig.Visuals != null;
        }

        // Eliminated players are deactivated rather than destroyed, but career
        // fixtures and the attack lab do tear players down, so don't leak.
        private void PruneDestroyed()
        {
            for (int i = rigs.Count - 1; i >= 0; i--)
                if (rigs[i].Root == null) rigs.RemoveAt(i);
        }
    }
}
