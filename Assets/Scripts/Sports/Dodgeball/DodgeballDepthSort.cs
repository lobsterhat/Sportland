using UnityEngine;
using UnityEngine.Rendering;

namespace Sportland.Sports.Dodgeball
{
    /// <summary>
    /// Depth-sorts a multi-renderer object (a player or the ball) as a unit by
    /// its world-Y, so things lower on screen (front) draw over things higher
    /// up (back) — the Tecmo-Bowl look for a flat field with side-view sprites.
    ///
    /// Uses a SortingGroup so the object's internal renderer layering (body,
    /// role mark, arrows, ring, shadow) is preserved while the whole object
    /// slots into the global order by depth. baseOrder is kept high so every
    /// sorted object stays above the court / center-line / floor renderers.
    /// Players and the ball share the same scale, so they interleave correctly
    /// by depth (a player in front of the ball draws over it, behind draws under).
    /// </summary>
    [DefaultExecutionOrder(9000)]
    public class DodgeballDepthSort : MonoBehaviour
    {
        [Tooltip("Global order at world-Y 0. High so every player/ball stays above the court and center-line renderers.")]
        [SerializeField] private int baseOrder = 200;
        [Tooltip("Sorting steps per world-Y unit. Higher = stronger front/back separation.")]
        [SerializeField] private float perUnit = 16f;

        private SortingGroup group;

        private void Awake()
        {
            group = GetComponent<SortingGroup>();
            if (group == null) group = gameObject.AddComponent<SortingGroup>();
        }

        private void LateUpdate()
        {
            if (group == null) return;
            // Lower world-Y (front) → higher order → drawn on top.
            group.sortingOrder = baseOrder - Mathf.RoundToInt(transform.position.y * perUnit);
        }
    }
}
