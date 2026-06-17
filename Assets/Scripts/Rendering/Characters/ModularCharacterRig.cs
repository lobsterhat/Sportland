using UnityEngine;
using System.Collections.Generic;

namespace Sportland.Rendering.Characters
{
    /// <summary>
    /// Core component of the modular character system.
    ///
    /// One Animator state machine drives every character; visual variety comes
    /// entirely from swapping CharacterAppearance — no animation re-authoring.
    ///
    /// How it works:
    ///   1. ApplyAppearance() builds child GameObjects (one per body-part layer).
    ///   2. Each layer holds a SpriteRenderer and a CharacterLayer driver.
    ///   3. Every Update() the rig reads the current Animator state name and
    ///      advances each layer's frame counter independently at that layer's FPS.
    ///   4. Swapping a part (SwapPart / ApplyAppearance) only updates the sprite
    ///      arrays — the Animator and timing are untouched.
    ///
    /// Body scaling:
    ///   HeightNormalized (0–1) scales the BodyRoot uniformly on Y.
    ///   WeightNormalized (0–1) selects among Slim / Athletic / Heavy body variants
    ///   and additionally stretches the BodyRoot on X via the body type's
    ///   widthScaleMultiplier.
    ///
    /// Setup:
    ///   1. Add ModularCharacterRig to a character root GameObject.
    ///   2. Add an Animator component and assign a controller with states
    ///      matching the names in AnimationStates (Idle, Walk, Run …).
    ///   3. Assign a CharacterPartLibrary.
    ///   4. Call ApplyAppearance(someAppearance) at runtime or set the
    ///      Appearance field in the Inspector.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [DisallowMultipleComponent]
    public class ModularCharacterRig : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────

        [Header("Configuration")]
        [Tooltip("The character's visual definition. Can be swapped at any time.")]
        [SerializeField] private CharacterAppearance appearance;

        [Tooltip("The master catalog of all available parts. Usually a shared project asset.")]
        [SerializeField] private CharacterPartLibrary partLibrary;

        [Header("Hierarchy")]
        [Tooltip("Transform to scale for height/weight. Defaults to this transform if left empty.")]
        [SerializeField] private Transform bodyRoot;

        [Header("Sorting")]
        [Tooltip("Base sorting order. Layer-specific offsets are applied on top.")]
        [SerializeField] private int baseSortingOrder = 10;

        // ── Layer sort offsets (back → front) ─────────────────────

        // Shadow is behind everything; Hair is above head.
        private static readonly Dictionary<CharacterPartCategory, int> LayerSortOffsets =
            new Dictionary<CharacterPartCategory, int>
        {
            { CharacterPartCategory.Shadow,     -2 },
            { CharacterPartCategory.Body,        0 },
            { CharacterPartCategory.Ears,        1 },
            { CharacterPartCategory.Head,        2 },
            { CharacterPartCategory.Eyebrows,    4 },
            { CharacterPartCategory.Eyes,        4 },
            { CharacterPartCategory.Nose,        4 },
            { CharacterPartCategory.Mouth,       4 },
            { CharacterPartCategory.FacialHair,  5 },
            { CharacterPartCategory.Hair,        6 },
        };

        // ── Runtime State ─────────────────────────────────────────

        private Animator animator;
        private string currentStateName = AnimationStates.Idle;

        // One entry per active layer, keyed by category
        private readonly Dictionary<CharacterPartCategory, CharacterLayer> layers =
            new Dictionary<CharacterPartCategory, CharacterLayer>();

        // ── Lifecycle ─────────────────────────────────────────────

        private void Awake()
        {
            animator = GetComponent<Animator>();
            if (bodyRoot == null) bodyRoot = transform;
        }

        private void Start()
        {
            if (appearance != null)
                ApplyAppearance(appearance);
        }

        private void Update()
        {
            SyncAnimatorState();
            TickAllLayers();
        }

        // ── Public API ────────────────────────────────────────────

        /// <summary>
        /// Rebuilds the entire visual from <paramref name="newAppearance"/>.
        /// Safe to call at any time; existing layer GameObjects are destroyed first.
        /// </summary>
        public void ApplyAppearance(CharacterAppearance newAppearance)
        {
            if (newAppearance == null)
            {
                Debug.LogWarning($"[ModularCharacterRig] ApplyAppearance called with null on {name}.");
                return;
            }

            appearance = newAppearance;
            RebuildLayers();
            ApplyBodyScale();
        }

        /// <summary>Swap a single part without rebuilding the whole rig.</summary>
        public void SwapPart(CharacterPartCategory category, CharacterPartVariant newVariant)
        {
            if (!layers.TryGetValue(category, out var layer))
            {
                layer = CreateLayer(category);
            }

            Color tint = GetTintForCategory(category);
            layer.SetVariant(newVariant, currentStateName, tint);
        }

        /// <summary>Live-update height. Range 0–1 (0 = min height, 1 = max).</summary>
        public void SetHeight(float normalizedHeight)
        {
            if (appearance != null) appearance.heightNormalized = Mathf.Clamp01(normalizedHeight);
            ApplyBodyScale();
        }

        /// <summary>Live-update weight. Range 0–1 (0 = slim, 1 = heavy).</summary>
        public void SetWeight(float normalizedWeight)
        {
            if (appearance != null) appearance.weightNormalized = Mathf.Clamp01(normalizedWeight);

            // Weight class may have changed — body variant needs a swap
            if (appearance != null && partLibrary != null)
            {
                var bt = partLibrary.GetBodyType(appearance.gender, appearance.GetWeightClass());
                if (bt?.bodyPartVariant != null)
                    SwapPart(CharacterPartCategory.Body, bt.bodyPartVariant);
            }

            ApplyBodyScale();
        }

        /// <summary>Re-tints all layers that receive tinting from the current appearance colors.</summary>
        public void RefreshColors()
        {
            if (appearance == null) return;
            foreach (var kvp in layers)
            {
                Color tint = GetTintForCategory(kvp.Key);
                kvp.Value.ApplyTint(tint);
            }
        }

        // ── Layer Building ────────────────────────────────────────

        private void RebuildLayers()
        {
            DestroyAllLayers();

            if (appearance == null) return;

            var weightClass = appearance.GetWeightClass();
            var bodyType = partLibrary != null
                ? partLibrary.GetBodyType(appearance.gender, weightClass)
                : null;

            SetupLayer(CharacterPartCategory.Body,       bodyType?.bodyPartVariant);
            SetupLayer(CharacterPartCategory.Head,       appearance.headVariant);
            SetupLayer(CharacterPartCategory.Eyebrows,   appearance.eyebrowsVariant);
            SetupLayer(CharacterPartCategory.Eyes,       appearance.eyesVariant);
            SetupLayer(CharacterPartCategory.Nose,       appearance.noseVariant);
            SetupLayer(CharacterPartCategory.Mouth,      appearance.mouthVariant);
            SetupLayer(CharacterPartCategory.Hair,       appearance.hairVariant);

            if (appearance.facialHairVariant != null)
                SetupLayer(CharacterPartCategory.FacialHair, appearance.facialHairVariant);
        }

        private void SetupLayer(CharacterPartCategory category, CharacterPartVariant variant)
        {
            var layer = CreateLayer(category);
            Color tint = GetTintForCategory(category);
            layer.SetVariant(variant, currentStateName, tint);
        }

        private CharacterLayer CreateLayer(CharacterPartCategory category)
        {
            // Reuse existing child if there is one
            if (layers.TryGetValue(category, out var existing))
            {
                if (existing != null) return existing;
                layers.Remove(category);
            }

            var go = new GameObject(category.ToString());
            go.transform.SetParent(bodyRoot, worldPositionStays: false);

            go.AddComponent<SpriteRenderer>();
            var layer = go.AddComponent<CharacterLayer>();

            int sortOffset = LayerSortOffsets.TryGetValue(category, out int off) ? off : 0;
            layer.Initialize(category, baseSortingOrder + sortOffset);

            layers[category] = layer;
            return layer;
        }

        private void DestroyAllLayers()
        {
            foreach (var layer in layers.Values)
            {
                if (layer != null)
                    Destroy(layer.gameObject);
            }
            layers.Clear();
        }

        // ── Animation ─────────────────────────────────────────────

        private void SyncAnimatorState()
        {
            if (animator == null) return;

            string stateName = ResolveStateName(animator.GetCurrentAnimatorStateInfo(0));
            if (stateName == currentStateName) return;

            currentStateName = stateName;
            foreach (var layer in layers.Values)
                layer.SetState(currentStateName);
        }

        private void TickAllLayers()
        {
            float dt = Time.deltaTime;
            foreach (var layer in layers.Values)
                layer.Tick(dt);
        }

        private static string ResolveStateName(AnimatorStateInfo info)
        {
            if (info.IsName(AnimationStates.Walk))   return AnimationStates.Walk;
            if (info.IsName(AnimationStates.Run))    return AnimationStates.Run;
            if (info.IsName(AnimationStates.Jump))   return AnimationStates.Jump;
            if (info.IsName(AnimationStates.Land))   return AnimationStates.Land;
            if (info.IsName(AnimationStates.Shoot))  return AnimationStates.Shoot;
            if (info.IsName(AnimationStates.Pass))   return AnimationStates.Pass;
            if (info.IsName(AnimationStates.Dive))   return AnimationStates.Dive;
            if (info.IsName(AnimationStates.Defend)) return AnimationStates.Defend;
            return AnimationStates.Idle;
        }

        // ── Body Scaling ──────────────────────────────────────────

        private void ApplyBodyScale()
        {
            if (appearance == null || bodyRoot == null) return;

            var weightClass = appearance.GetWeightClass();
            var bodyType = partLibrary != null
                ? partLibrary.GetBodyType(appearance.gender, weightClass)
                : null;

            float yScale = bodyType != null
                ? bodyType.GetHeightScale(appearance.heightNormalized)
                : Mathf.Lerp(0.85f, 1.20f, appearance.heightNormalized);

            float xScale = bodyType?.widthScaleMultiplier ?? 1f;

            bodyRoot.localScale = new Vector3(xScale, yScale, 1f);
        }

        // ── Tinting Helpers ───────────────────────────────────────

        private Color GetTintForCategory(CharacterPartCategory category)
        {
            if (appearance == null) return Color.white;

            return category switch
            {
                CharacterPartCategory.Hair       => appearance.hairColor,
                CharacterPartCategory.FacialHair => appearance.hairColor,
                CharacterPartCategory.Eyes       => appearance.eyeColor,
                CharacterPartCategory.Head       => appearance.skinColor,
                CharacterPartCategory.Ears       => appearance.skinColor,
                _                                => Color.white
            };
        }

        // ── Editor Hot-Reload ─────────────────────────────────────

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Allow dragging in a new appearance in the Inspector during play
            if (Application.isPlaying && appearance != null)
            {
                RebuildLayers();
                ApplyBodyScale();
            }
        }
#endif
    }
}
