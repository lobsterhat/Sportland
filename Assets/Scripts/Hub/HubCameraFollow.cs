using UnityEngine;

namespace Sportland.Hub
{
    /// <summary>
    /// Minimal camera follow for the hub. The sports CameraFollow is coupled
    /// to the InputBroker control-transfer system; the hub has one walking
    /// character and needs none of that.
    /// </summary>
    public class HubCameraFollow : MonoBehaviour
    {
        public Transform target;
        public float smoothSpeed = 6f;

        [Tooltip("Camera centre is clamped inside these half-extents.")]
        public Vector2 bounds = new Vector2(6f, 3f);

        private void LateUpdate()
        {
            if (target == null) return;

            Vector3 desired = new Vector3(
                Mathf.Clamp(target.position.x, -bounds.x, bounds.x),
                Mathf.Clamp(target.position.y, -bounds.y, bounds.y),
                transform.position.z);

            transform.position = Vector3.Lerp(transform.position, desired, smoothSpeed * Time.deltaTime);
        }
    }
}
