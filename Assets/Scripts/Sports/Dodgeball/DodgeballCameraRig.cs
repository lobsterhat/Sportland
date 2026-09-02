using UnityEngine;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Frames the dodgeball court. Squashing the depth axis changes the shape of
    /// the playfield on screen a great deal — the play area goes from 24 x 15
    /// metres to roughly 24 x 7.5 units — so the framing has to be derived from
    /// <see cref="CourtProjection"/> rather than left as a hand-set orthographic
    /// size that only happened to suit the flat view.
    ///
    /// <see cref="FramingMode.FitPlayArea"/> is the shipped behaviour: hold the
    /// whole court and both outfields on screen at once, with headroom above the
    /// far sideline for the stands. All twelve players stay visible, which a 6v6
    /// game with outfielders on every side needs.
    ///
    /// <see cref="FramingMode.Follow"/> is the seam for the arcade's scrolling
    /// camera. It works — point <see cref="followTarget"/> at the ball or the
    /// controlled player and switch modes — but scrolling also wants parallax on
    /// the backdrop and off-screen markers for the players you can no longer
    /// see, so it is not the default yet.
    /// </summary>
    [DefaultExecutionOrder(11000)]
    public class DodgeballCameraRig : MonoBehaviour
    {
        public enum FramingMode
        {
            /// <summary>Static shot holding the entire play area.</summary>
            FitPlayArea,
            /// <summary>Zoomed in, tracking a target and clamped to the court.</summary>
            Follow,
        }

        [SerializeField] private FramingMode mode = FramingMode.FitPlayArea;
        [Tooltip("Camera to drive. Empty uses Camera.main.")]
        [SerializeField] private Camera targetCamera;

        [Header("Fit")]
        [Tooltip("Metres of breathing room past the left and right edges of the play area.")]
        [SerializeField] private float sideMargin = 1.2f;
        [Tooltip("Screen units of apron kept below the near sideline.")]
        [SerializeField] private float bottomMargin = 1.5f;
        [Tooltip("Screen units kept above the far sideline for the stands. The backdrop is built taller than this on purpose so it overfills.")]
        [SerializeField] private float headroom = 5.5f;

        [Header("Follow")]
        [SerializeField] private Transform followTarget;
        [SerializeField] private float followOrthoSize = 6f;
        [Tooltip("Higher snaps to the target faster.")]
        [SerializeField] private float followLerp = 6f;

        private Camera cam;

        /// <summary>Switch framing at runtime (the hook a scrolling camera would come in through).</summary>
        public void SetMode(FramingMode value) => mode = value;

        /// <summary>Who the Follow mode tracks. Projected like everything else, so it works on the angled floor.</summary>
        public Transform FollowTarget { get => followTarget; set => followTarget = value; }

        private void LateUpdate()
        {
            if (cam == null) cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam == null || !cam.orthographic) return;

            if (mode == FramingMode.Follow && followTarget != null) Follow();
            else Fit();
        }

        // Widest at the near edge, since convergence only ever narrows the court
        // going back. Vertically it runs from the near sideline up past the far
        // one into the stands.
        private void Fit()
        {
            float halfWidth = CourtSetup.PlayAreaHalfWidth + sideMargin;
            float bottom = CourtProjection.NearScreenY - bottomMargin;
            float top = CourtProjection.FarScreenY + headroom;

            float aspect = cam.aspect > 0.01f ? cam.aspect : 16f / 9f;
            cam.orthographicSize = Mathf.Max((top - bottom) * 0.5f, halfWidth / aspect);
            Place(0f, (top + bottom) * 0.5f);
        }

        private void Follow()
        {
            cam.orthographicSize = followOrthoSize;

            Vector3 t = followTarget.position;
            Vector2 focus = CourtProjection.Ground(t.x, t.y);

            // Never show past the edge of the world. The near edge is the widest
            // point, so clamping against it keeps the whole frame on the floor.
            float halfH = cam.orthographicSize;
            float halfW = halfH * (cam.aspect > 0.01f ? cam.aspect : 16f / 9f);
            float limitX = Mathf.Max(0f, CourtSetup.PlayAreaHalfWidth - halfW);
            float minY = CourtProjection.NearScreenY + halfH;
            float maxY = CourtProjection.FarScreenY + headroom - halfH;

            float x = Mathf.Clamp(focus.x, -limitX, limitX);
            float y = maxY >= minY ? Mathf.Clamp(focus.y, minY, maxY) : (minY + maxY) * 0.5f;

            Vector3 now = cam.transform.position;
            float k = 1f - Mathf.Exp(-followLerp * Time.deltaTime);   // frame-rate independent
            Place(Mathf.Lerp(now.x, x, k), Mathf.Lerp(now.y, y, k));
        }

        private void Place(float x, float y)
        {
            Vector3 p = cam.transform.position;
            cam.transform.position = new Vector3(x, y, p.z);
        }
    }
}
