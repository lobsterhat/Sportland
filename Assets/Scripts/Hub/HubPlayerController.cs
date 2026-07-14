using UnityEngine;

namespace Sportland.Hub
{
    /// <summary>
    /// Top-down hub movement: WASD/arrows/stick via the legacy axes (the
    /// project runs with both input backends enabled). No physics — the hub
    /// is a menu you walk around in, so transform movement + a bounds clamp
    /// is all it needs.
    /// </summary>
    public class HubPlayerController : MonoBehaviour
    {
        [Tooltip("Units per second.")]
        public float moveSpeed = 7f;

        [Tooltip("Half-extents of the walkable area, centred on the origin.")]
        public Vector2 bounds = new Vector2(11f, 7f);

        private void Update()
        {
            Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            if (input.sqrMagnitude > 1f) input.Normalize();

            Vector3 p = transform.position + (Vector3)(input * moveSpeed * Time.deltaTime);
            p.x = Mathf.Clamp(p.x, -bounds.x, bounds.x);
            p.y = Mathf.Clamp(p.y, -bounds.y, bounds.y);
            transform.position = p;
        }
    }
}
