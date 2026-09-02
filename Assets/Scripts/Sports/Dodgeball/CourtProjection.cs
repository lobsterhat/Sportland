using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Maps the flat simulation plane onto the angled ("3/4", Technos Super
    /// Dodge Ball) view.
    ///
    /// The sim never moves. It stays a flat 18 x 9 m court where X runs along
    /// the court and Y is depth into the screen, exactly as before — only the
    /// drawing changes. Everything that draws asks this class where a world
    /// point lands, which is what keeps the floor, the players, the ball, the
    /// shadows and the world-anchored IMGUI from disagreeing with each other.
    ///
    /// Three independent effects make up the look, each its own knob:
    ///   • <see cref="DepthSquash"/> compresses the depth axis, so the floor
    ///     reads as receding rather than as a wall seen head-on. This is the
    ///     effect doing most of the work.
    ///   • <see cref="FarScale"/> narrows the court toward the back — the
    ///     trapezoid. The arcade courts converge only slightly and the NES one
    ///     not at all, so this stays mild.
    ///   • <see cref="SpriteDepthScale"/> shrinks sprites with depth. Defaults
    ///     to 0: Technos sprites are the same size at every depth. That cheat
    ///     is a large part of the look, and it also keeps pixel art from
    ///     resampling to fractional sizes.
    ///
    /// With <see cref="Enabled"/> off every mapping is the identity and the old
    /// flat top-down view comes back, so there is only ever one code path.
    ///
    /// Two vocabularies matter here and are easy to confuse:
    ///   • a GROUND point is a spot on the floor, given in sim metres (x along
    ///     the court, y into it). <see cref="Ground"/> projects it.
    ///   • a HEIGHT is metres straight up off the floor — a jump arc, a ball in
    ///     flight. <see cref="Point"/> projects a ground point plus a height.
    /// Anything lying flat on the floor (shadows, the control ring, the
    /// direction arrows) is a ground point. Anything standing upright (a
    /// character sprite, the ball) is a ground point plus a height.
    /// </summary>
    public static class CourtProjection
    {
        /// <summary>Near (bottom of screen) edge of the projected depth range, in sim metres.</summary>
        public const float NearDepth = -CourtSetup.PlayAreaHalfHeight;   // -7.5
        /// <summary>Far (top of screen) edge of the projected depth range, in sim metres.</summary>
        public const float FarDepth = CourtSetup.PlayAreaHalfHeight;     // +7.5

        // ---- Defaults ----
        // Tuned against the reference screenshots rather than the old spike,
        // which converged hard (farScale 0.5) but never squashed depth at all —
        // it was missing the one ingredient that actually sells the angle.
        public const float DefaultFarScale = 0.82f;
        public const float DefaultDepthSquash = 0.5f;
        public const float DefaultDepthBunch = 1.15f;
        public const float DefaultSpriteDepthScale = 0f;
        public const float DefaultHeightLift = 1f;

        /// <summary>Off = identity mapping = the old flat top-down view (kept as a debug A/B toggle).</summary>
        public static bool Enabled = true;

        /// <summary>
        /// Width of the far edge relative to the near edge. 1 = no convergence
        /// (a plain rectangle, the NES look); lower narrows the back of the
        /// court into a trapezoid (the arcade look).
        /// </summary>
        public static float FarScale = DefaultFarScale;

        /// <summary>
        /// Screen units per metre of depth, against 1 unit per metre of width.
        /// This is what tilts the floor away from the camera. At 0.5 the whole
        /// 15 m play area is 7.5 units tall on screen, which leaves the top of
        /// the frame free for the crowd and stands.
        /// </summary>
        public static float DepthSquash = DefaultDepthSquash;

        /// <summary>
        /// Extra non-linearity in the depth axis: 1 is a pure parallel
        /// (oblique) projection with evenly spaced rows, above 1 bunches rows
        /// toward the back for a touch of real perspective. Stays near 1 —
        /// heavy bunching makes movement speed visibly change with depth.
        /// </summary>
        public static float DepthBunch = DefaultDepthBunch;

        /// <summary>
        /// How much sprites shrink with depth, 0 = not at all (the Technos
        /// cheat, and the default), 1 = shrink all the way to
        /// <see cref="FarScale"/> at the back edge.
        /// </summary>
        public static float SpriteDepthScale = DefaultSpriteDepthScale;

        /// <summary>
        /// Screen units per metre of height. 1 is honest — a metre of jump
        /// covers as much screen as a metre of court width. Because depth is
        /// squashed, that already makes height read roughly twice as strongly
        /// as depth, which is what keeps a jump from looking like a step
        /// toward the camera. Raise it if hops still need to pop.
        /// </summary>
        public static float HeightLift = DefaultHeightLift;

        /// <summary>Screen Y of the near (bottom) edge of the play area.</summary>
        public static float NearScreenY => Enabled ? NearDepth * DepthSquash : NearDepth;

        /// <summary>
        /// Screen Y of the far (top) edge of the play area — the floor's
        /// horizon, where the backdrop starts.
        /// </summary>
        public static float FarScreenY => Enabled ? FarDepth * DepthSquash : FarDepth;

        /// <summary>Restore the shipped defaults. Also runs on play so editor tinkering doesn't leak between sessions.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetToDefaults()
        {
            Enabled = true;
            FarScale = DefaultFarScale;
            DepthSquash = DefaultDepthSquash;
            DepthBunch = DefaultDepthBunch;
            SpriteDepthScale = DefaultSpriteDepthScale;
            HeightLift = DefaultHeightLift;
        }

        public static void Configure(float farScale, float depthSquash, float depthBunch,
                                     float spriteDepthScale, float heightLift)
        {
            FarScale = Mathf.Clamp(farScale, 0.2f, 1f);
            DepthSquash = Mathf.Clamp(depthSquash, 0.1f, 1f);
            DepthBunch = Mathf.Clamp(depthBunch, 1f, 3f);
            SpriteDepthScale = Mathf.Clamp01(spriteDepthScale);
            HeightLift = Mathf.Clamp(heightLift, 0.1f, 3f);
        }

        /// <summary>0 at the near edge of the play area, 1 at the far edge. Not clamped — outfielders and stray balls can sit outside.</summary>
        public static float Depth01(float worldY) => (worldY - NearDepth) / (FarDepth - NearDepth);

        /// <summary>
        /// Horizontal narrowing at a given depth. Ground offsets sideways from a
        /// point get multiplied by this, which is what bends the sidelines in.
        /// </summary>
        public static float Converge(float worldY)
        {
            if (!Enabled) return 1f;
            return Mathf.LerpUnclamped(1f, FarScale, Depth01(worldY));
        }

        /// <summary>
        /// Scale for an upright sprite standing at this depth. 1 everywhere
        /// while <see cref="SpriteDepthScale"/> is 0, which is the default.
        /// </summary>
        public static float SpriteScale(float worldY)
        {
            if (!Enabled || SpriteDepthScale <= 0f) return 1f;
            return Mathf.LerpUnclamped(1f, Converge(worldY), SpriteDepthScale);
        }

        /// <summary>Project a point on the floor to where it draws on screen.</summary>
        public static Vector2 Ground(float worldX, float worldY)
        {
            if (!Enabled) return new Vector2(worldX, worldY);

            float t = Depth01(worldY);
            float x = worldX * Mathf.LerpUnclamped(1f, FarScale, t);
            return new Vector2(x, DepthToScreenY(t));
        }

        /// <inheritdoc cref="Ground(float,float)"/>
        public static Vector2 Ground(Vector2 world) => Ground(world.x, world.y);

        /// <summary>
        /// Project a point <paramref name="height"/> metres above the floor —
        /// a jumping player's feet, a ball in flight. Height lifts straight up
        /// the screen; it never moves the point sideways or in depth.
        /// </summary>
        public static Vector2 Point(float worldX, float worldY, float height)
        {
            Vector2 g = Ground(worldX, worldY);
            if (height == 0f) return g;
            g.y += Enabled ? height * HeightLift * SpriteScale(worldY) : height;
            return g;
        }

        /// <inheritdoc cref="Point(float,float,float)"/>
        public static Vector2 Point(Vector2 world, float height) => Point(world.x, world.y, height);

        /// <summary>
        /// Project a point on the floor, keeping the caller's Z so sorting and
        /// any authored Z offsets survive.
        /// </summary>
        public static Vector3 GroundWorld(Vector3 world)
        {
            Vector2 g = Ground(world.x, world.y);
            return new Vector3(g.x, g.y, world.z);
        }

        /// <summary>
        /// Screen Y that a given height above the floor lifts to, before the
        /// ground point is added. Callers that already have a projected ground
        /// point use this rather than re-projecting.
        /// </summary>
        public static float Lift(float height, float worldY)
        {
            if (!Enabled) return height;
            return height * HeightLift * SpriteScale(worldY);
        }

        /// <summary>
        /// Inverse of <see cref="Ground(float,float)"/> — turn a point in the
        /// projected view back into sim coordinates. Used by mouse picking and
        /// drag handles, which work in screen space.
        /// </summary>
        public static Vector2 Unproject(float screenX, float screenY)
        {
            if (!Enabled) return new Vector2(screenX, screenY);

            float t = ScreenYToDepth01(screenY);
            float worldY = Mathf.LerpUnclamped(NearDepth, FarDepth, t);
            float converge = Mathf.LerpUnclamped(1f, FarScale, t);
            // Guard the degenerate farScale=0 case; the clamp in Configure
            // should already prevent it, but a stray direct assignment would
            // otherwise divide by zero here.
            float worldX = Mathf.Abs(converge) < 0.0001f ? screenX : screenX / converge;
            return new Vector2(worldX, worldY);
        }

        /// <inheritdoc cref="Unproject(float,float)"/>
        public static Vector2 Unproject(Vector2 screen) => Unproject(screen.x, screen.y);

        // Depth 0..1 → screen Y. DepthBunch of 1 leaves this a straight
        // worldY * DepthSquash; above 1 it packs the far rows together.
        private static float DepthToScreenY(float t)
        {
            float bunched = DepthBunch <= 1f ? t : 1f - Mathf.Pow(1f - Mathf.Clamp01(t), DepthBunch);
            // Outside the play area there is no bunching curve to follow, so
            // extend the near/far edges linearly instead of clamping — a ball
            // that overshoots the boundary still draws in a sensible place.
            if (t < 0f || t > 1f) bunched = t;
            return Mathf.LerpUnclamped(NearDepth * DepthSquash, FarDepth * DepthSquash, bunched);
        }

        private static float ScreenYToDepth01(float screenY)
        {
            float near = NearDepth * DepthSquash;
            float far = FarDepth * DepthSquash;
            float bunched = (screenY - near) / (far - near);
            if (DepthBunch <= 1f || bunched < 0f || bunched > 1f) return bunched;
            return 1f - Mathf.Pow(1f - bunched, 1f / DepthBunch);
        }
    }
}
