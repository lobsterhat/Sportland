using UnityEngine;

namespace Sportland.Hub
{
    /// <summary>
    /// HubController - Manages the main hub world
    /// TODO: Implement hub world navigation and building interactions
    /// </summary>
    public class HubController : MonoBehaviour
    {
        [Header("Hub State")]
        public bool isInHub = true;

        // TODO: Implement building interaction system
        // TODO: Implement camera controls
        // TODO: Implement day/night cycle
        // TODO: Implement NPC interactions
        // TODO: Implement building selection UI

        private void Start()
        {
            Debug.Log("Hub World loaded");
        }

        // TODO: Methods for entering/exiting buildings
        // TODO: Arena selection → Load sport
        // TODO: Office → Team management
        // TODO: Hospital → Injury management
        // TODO: Cafe → Social interactions
        // TODO: Home → Rest/training
    }
}
