using UnityEngine;

namespace Sportland.Hub
{
    /// <summary>The hub's interaction points (design/hub_world.md §2).</summary>
    public enum HubBuildingType
    {
        Office,
        Practice,
        Hospital,
        Cafe,
        Arena,
        Home,
    }

    /// <summary>
    /// A building marker in the hub: an identity and an interaction radius.
    /// What interacting *does* lives in HubInteractor, so buildings stay data.
    /// </summary>
    public class HubBuilding : MonoBehaviour
    {
        public HubBuildingType type;
        public string displayName;

        [Tooltip("How close the player must stand to interact.")]
        public float interactionRadius = 2.2f;

        /// <summary>The prompt line shown when the player is in range.</summary>
        public string PromptText
        {
            get
            {
                switch (type)
                {
                    case HubBuildingType.Office:   return "[E] Office — front-office work (1 action)";
                    case HubBuildingType.Practice: return "[E] Practice Field — run practice (1 action)";
                    case HubBuildingType.Hospital: return "[E] Hospital — treat the most tired athlete (1 action)";
                    case HubBuildingType.Cafe:     return "[E] Cafe — one-on-one over dinner (1 action)";
                    case HubBuildingType.Arena:    return "[E] Arena — check the schedule (free)";
                    case HubBuildingType.Home:     return "[E] Home — end the day";
                    default:                       return "[E] Interact";
                }
            }
        }
    }
}
