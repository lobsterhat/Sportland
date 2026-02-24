using UnityEditor;
using UnityEngine;

namespace Sportland.Sports.Tag
{
    public class StartAsIt : MonoBehaviour
    {
        void Start()
        {
            GetComponent<TagMovementController>().BecomeIt();
        }
    }
}