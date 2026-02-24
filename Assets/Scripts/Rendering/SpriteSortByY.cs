using System.Collections;
using UnityEngine;

namespace Sportland.Rendering
{
    public class SpriteSortByY : MonoBehaviour
    {
        private SpriteRenderer spriteRenderer;

        void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        void LateUpdate()
        {
            // Lower Y = higher sorting order = renders in front
            spriteRenderer.sortingOrder = Mathf.RoundToInt(-transform.parent.position.y * 100);
        }
    }
}