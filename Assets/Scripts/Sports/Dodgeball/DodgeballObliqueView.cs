using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// THROWAWAY SPIKE — fakes a 3/4 / oblique view on top of the top-down sim
    /// without touching physics, so you can feel the camera ANGLE before
    /// investing in front-facing art. Placeholder sprites stay top-down blobs;
    /// only the *world* reads as receding.
    ///
    /// How it works: the sim already separates ground position (a player/ball's
    /// world XY) from height (a local-Y offset on a "Visual" child — the jump
    /// bob / ball arc). Both owning scripts ASSIGN that child's localPosition
    /// every Update frame, so this component runs afterward (high execution
    /// order + LateUpdate) and, per renderable:
    ///   - foreshortens DEPTH: shifts the Visual child toward courtCenterY by
    ///     (1 - depthFactor) of its world-Y, so the court looks like it recedes;
    ///   - exaggerates HEIGHT: lifts the jump bob / ball arc so hops pop against
    ///     the compressed depth.
    /// The shift is re-applied from scratch each frame (the owning scripts wipe
    /// it with their own assignment), so it never accumulates.
    ///
    /// Sim is untouched — positions, collisions, zones, throw math all run in the
    /// original top-down world space. CourtSetup scales the court visual to match.
    ///
    /// Known v1 gaps (it's a spike): the debug arrows / control ring stay pinned
    /// to the true (uncompressed) ground position so they drift a little; no
    /// depth sorting between overlapping players; assumes the Visual child rests
    /// at local-Y 0 (true for the procedural / default sprites).
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class DodgeballObliqueView : MonoBehaviour
    {
        [Tooltip("Fraction of true depth (world Y) shown on screen. <1 foreshortens the court so it reads as receding. 0.6 ≈ a shallow 3/4. Tune live and tell Claude what looks right.")]
        [Range(0.2f, 1f)] public float depthFactor = 0.6f;
        [Tooltip("Multiplier on jump / ball-arc height so hops pop against the compressed depth.")]
        [Range(1f, 3f)] public float heightExaggeration = 1.5f;
        [Tooltip("Court depth-axis center (world Y) that depth compresses toward. 0 = court center.")]
        public float courtCenterY = 0f;

        private Ball ball;
        private Vector3 courtBaseScale, lineBaseScale;
        private bool courtCaptured, courtCompressed;

        // Compress the court visual's depth on enable, restore on disable, so the
        // Q toggle flips the whole effect on/off live. (Player/ball projection
        // reverts on its own when this component stops running — the movement/ball
        // scripts reassign their Visual localY every frame without the offset.)
        private void OnEnable() => CompressCourt();
        private void OnDisable() => RestoreCourt();

        private void CompressCourt()
        {
            if (courtCompressed) return;
            var court = transform.Find("Court");
            var line = transform.Find("CenterLine");
            if (!courtCaptured)
            {
                if (court != null) courtBaseScale = court.localScale;
                if (line != null) lineBaseScale = line.localScale;
                courtCaptured = true;
            }
            if (court != null) court.localScale = new Vector3(courtBaseScale.x, courtBaseScale.y * depthFactor, courtBaseScale.z);
            if (line != null) line.localScale = new Vector3(lineBaseScale.x, lineBaseScale.y * depthFactor, lineBaseScale.z);
            courtCompressed = true;
        }

        private void RestoreCourt()
        {
            if (!courtCompressed || !courtCaptured) { courtCompressed = false; return; }
            var court = transform.Find("Court");
            var line = transform.Find("CenterLine");
            if (court != null) court.localScale = courtBaseScale;
            if (line != null) line.localScale = lineBaseScale;
            courtCompressed = false;
        }

        private void LateUpdate()
        {
            float keep = 1f - depthFactor;   // how far to pull each unit of depth back toward center

            var all = PlayerZoneTracker.All;
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (t == null || t.transform.childCount == 0) continue;
                var visual = t.transform.GetChild(0);   // child[0] = "Visual" (movement bobs this)

                float depthShift = -(t.transform.position.y - courtCenterY) * keep;
                float extraLift = 0f;
                var mv = t.GetComponent<PlayerMovement>();
                if (mv != null) extraLift = mv.CurrentJumpHeight * (heightExaggeration - 1f);

                var lp = visual.localPosition;
                visual.localPosition = new Vector3(lp.x, lp.y + depthShift + extraLift, lp.z);
            }

            if (ball == null) ball = FindAnyObjectByType<Ball>();
            if (ball != null)
            {
                float depthShift = -(ball.transform.position.y - courtCenterY) * keep;
                float ballLift = ball.Height * (heightExaggeration - 1f);
                ShiftChild(ball.transform, "Visual", depthShift + ballLift);
                ShiftChild(ball.transform, "Shadow", depthShift);   // shadow stays on the floor
            }
        }

        private static void ShiftChild(Transform root, string name, float dy)
        {
            var c = root.Find(name);
            if (c == null) return;
            var lp = c.localPosition;
            c.localPosition = new Vector3(lp.x, lp.y + dy, lp.z);
        }
    }
}
