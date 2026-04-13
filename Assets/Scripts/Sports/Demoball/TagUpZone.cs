using UnityEngine;

namespace Sportland.Sports.Demoball
{
    /// <summary>
    /// A trigger zone placed inside the scoring ring. When a tackled player (NeedsTagUp = true)
    /// physically enters this zone they are cleared to return to play.
    ///
    /// Tackled players must reach the scoring ring to tag up — they cannot re-enter play
    /// anywhere on the open field. This creates a natural flow: tackled → sprint to ring →
    /// tag up → re-enter offense.
    ///
    /// Place one or more of these around the inner edge of the scoring ring.
    /// Requires any Collider2D component; the component forces isTrigger in Awake.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class TagUpZone : MonoBehaviour
    {
        private void Awake()
        {
            GetComponent<Collider2D>().isTrigger = true;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            var player = other.GetComponent<DemoballMovementController>();
            if (player != null && player.NeedsTagUp)
                player.TagUp();
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0f, 1f, 0.5f, 0.35f);
            var col = GetComponent<Collider2D>();
            if (col != null)
                Gizmos.DrawCube(transform.position, col.bounds.size);
            else
                Gizmos.DrawCube(transform.position, Vector3.one);

#if UNITY_EDITOR
            UnityEditor.Handles.Label(transform.position + Vector3.up * 0.6f, "TAG UP");
#endif
        }
    }
}
