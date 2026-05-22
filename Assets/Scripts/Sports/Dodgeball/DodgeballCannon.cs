using UnityEngine;
using UnityEngine.InputSystem;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Debug tool for exercising the catch mechanic. Fires the ball from a
    /// movable cannon position at the human-controlled player. Fire with C
    /// (keyboard) / R1 (gamepad) / the on-screen Fire button, or enable
    /// auto-fire. A real-time IMGUI panel (top-right) tweaks speed and
    /// position while playing, and a marker shows where the cannon sits.
    ///
    /// The shot is attributed to an opponent of the target as the virtual
    /// thrower, so the controlled player is a legal catch/hit target: press
    /// Catch as it arrives to test the skill check, or let it hit you.
    /// </summary>
    public class DodgeballCannon : MonoBehaviour
    {
        [SerializeField] private float firePower = 20f;
        [Tooltip("Absolute world position the ball is fired from.")]
        [SerializeField] private Vector2 firePosition = new Vector2(3f, 0f);
        [Tooltip("0 = aim where the target is now; 1 = lead to where it will be on arrival.")]
        [SerializeField, Range(0f, 1f)] private float anticipation = 0f;
        [SerializeField] private bool autoFire = false;
        [SerializeField] private float autoFireInterval = 3f;

        [Header("Tool")]
        [SerializeField] private bool showTool = true;
        [SerializeField] private float minSpeed = 4f;
        [SerializeField] private float maxSpeed = 50f;
        [Tooltip("Right stick nudges the cannon position at this speed (units/sec).")]
        [SerializeField] private float positionMoveSpeed = 8f;
        [SerializeField] private bool showMarker = true;
        [SerializeField] private Color markerColor = new Color(1f, 0.5f, 0.1f, 0.9f);

        // Court bounds (incl. outfielder strips) that the cannon position is clamped to.
        private const float MaxX = 12f;
        private const float MaxY = 7.5f;

        private Ball ball;
        private Transform marker;
        private float nextAutoFireTime;

        // Public accessors so the panel can drive them live.
        public float FirePower { get => firePower; set => firePower = Mathf.Max(0f, value); }
        public Vector2 FirePosition { get => firePosition; set => firePosition = value; }
        public bool AutoFire { get => autoFire; set => autoFire = value; }

        private void Update()
        {
            HandlePositionStick();
            UpdateMarker();

            if (FireRequested()) Fire();

            if (autoFire && Time.time >= nextAutoFireTime)
            {
                Fire();
                nextAutoFireTime = Time.time + autoFireInterval;
            }
        }

        // Right stick nudges the cannon around the court for debugging.
        private void HandlePositionStick()
        {
            var gp = Gamepad.current;
            if (gp == null) return;
            Vector2 stick = gp.rightStick.ReadValue();
            if (stick.sqrMagnitude < 0.02f) return;   // deadzone

            firePosition += stick * (positionMoveSpeed * Time.deltaTime);
            firePosition.x = Mathf.Clamp(firePosition.x, -MaxX, MaxX);
            firePosition.y = Mathf.Clamp(firePosition.y, -MaxY, MaxY);
        }

        private static bool FireRequested()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.cKey.wasPressedThisFrame) return true;
            var gp = Gamepad.current;
            if (gp != null && gp.rightShoulder.wasPressedThisFrame) return true;
            return false;
        }

        public void Fire()
        {
            if (ball == null) ball = FindFirstObjectByType<Ball>();
            if (ball == null) return;

            var input = DodgeballPlayerInput.Current;
            if (input == null) return;
            var target = input.GetComponent<PlayerZoneTracker>();
            if (target == null) return;

            var rb = target.GetComponent<Rigidbody2D>();
            Vector2 tvel = rb != null ? rb.linearVelocity : Vector2.zero;
            Vector2 aim = Ball.LeadAim(firePosition, target.transform.position, tvel, firePower, anticipation);
            ball.LaunchFrom(firePosition, aim, firePower, FindOpponent(target));
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

        // ── Marker ──

        private void UpdateMarker()
        {
            if (!showMarker)
            {
                if (marker != null) marker.gameObject.SetActive(false);
                return;
            }
            if (marker == null) BuildMarker();
            marker.gameObject.SetActive(true);
            marker.position = new Vector3(firePosition.x, firePosition.y, 0f);
        }

        private void BuildMarker()
        {
            var go = new GameObject("CannonMarker");
            go.transform.SetParent(transform, false);

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = markerColor };
            mr.sortingOrder = 30;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            const int segs = 24;
            const float radius = 0.3f;
            var verts = new Vector3[segs + 1];
            var tris = new int[segs * 3];
            verts[0] = Vector3.zero;
            for (int i = 0; i < segs; i++)
            {
                float a = i * Mathf.PI * 2f / segs;
                verts[i + 1] = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f);
                tris[i * 3]     = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = (i + 1) % segs + 1;
            }
            var mesh = new Mesh { name = "CannonMarker", vertices = verts, triangles = tris };
            mesh.RecalculateNormals();
            mf.mesh = mesh;

            marker = go.transform;
        }

        // ── Real-time control panel ──

        private void OnGUI()
        {
            if (!showTool) return;

            const float w = 250f;
            float x = Screen.width - w - 12f;
            GUILayout.BeginArea(new Rect(x, 12f, w, 270f), GUI.skin.box);

            GUILayout.Label("== CANNON ==");

            GUILayout.Label($"Speed: {firePower:F1} u/s ({DodgeballUnits.ToMph(firePower):F0} mph)");
            firePower = GUILayout.HorizontalSlider(firePower, minSpeed, maxSpeed);

            GUILayout.Label($"Pos X: {firePosition.x:F1}  (right stick moves)");
            firePosition.x = GUILayout.HorizontalSlider(firePosition.x, -MaxX, MaxX);
            GUILayout.Label($"Pos Y: {firePosition.y:F1}");
            firePosition.y = GUILayout.HorizontalSlider(firePosition.y, -MaxY, MaxY);

            GUILayout.Label($"Anticipation: {anticipation:F2}");
            anticipation = GUILayout.HorizontalSlider(anticipation, 0f, 1f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Fire (C / R1)")) Fire();
            autoFire = GUILayout.Toggle(autoFire, $" Auto {autoFireInterval:F0}s");
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }
    }
}
