using UnityEngine;
using Sportland.Core.Athletes;

namespace Sportland.Rendering.Characters
{
    /// <summary>
    /// Convenience component: reads the Athlete's CharacterAppearance and feeds
    /// it into the ModularCharacterRig on the same GameObject at Start.
    ///
    /// Typical use:
    ///   Add both ModularCharacterRig and CharacterRigBootstrap to a player prefab,
    ///   then assign the Athlete asset to this component.  The rig will configure
    ///   itself automatically when the scene starts.
    ///
    ///   You can also call Bootstrap(athlete) at runtime to swap the athlete
    ///   (e.g. after roster changes or entering a game).
    /// </summary>
    [RequireComponent(typeof(ModularCharacterRig))]
    public class CharacterRigBootstrap : MonoBehaviour
    {
        [SerializeField] private Athlete athlete;

        private ModularCharacterRig rig;

        private void Awake()
        {
            rig = GetComponent<ModularCharacterRig>();
        }

        private void Start()
        {
            if (athlete != null)
                Bootstrap(athlete);
        }

        public void Bootstrap(Athlete newAthlete)
        {
            athlete = newAthlete;

            if (athlete?.appearance == null)
            {
                Debug.LogWarning($"[CharacterRigBootstrap] Athlete '{athlete?.GetFullName()}' " +
                                 $"has no CharacterAppearance assigned.", this);
                return;
            }

            rig.ApplyAppearance(athlete.appearance);

            // Mirror the athlete's height to the rig's scale
            float normalizedHeight = NormalizeHeight(athlete.height, athlete.appearance.gender);
            rig.SetHeight(normalizedHeight);
        }

        // ── Height Normalization ───────────────────────────────────

        // Height ranges (in units, matching HeightConstants)
        private static float NormalizeHeight(float heightUnits, CharacterGender gender)
        {
            float min = gender == CharacterGender.Female ? 1.525f : 1.676f; // 5'0" / 5'6"
            float max = gender == CharacterGender.Female ? 1.906f : 2.135f; // 6'3" / 7'0"
            return Mathf.InverseLerp(min, max, heightUnits);
        }
    }
}
