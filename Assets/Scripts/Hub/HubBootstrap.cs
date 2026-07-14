using UnityEngine;
using TMPro;
using Sportland.Career;

namespace Sportland.Hub
{
    /// <summary>
    /// Builds the walkable hub at runtime: ground, building markers, the
    /// player, camera follow, and the HUD. Everything is generated in code so
    /// the vertical slice needs no hand-authored sprites, prefabs, or UI —
    /// drop this on one GameObject in HubWorld.unity and press Play.
    ///
    /// The old menu canvas ("Play Basketball") is disabled here rather than
    /// deleted, so reverting is trivial while the slice settles.
    /// </summary>
    public class HubBootstrap : MonoBehaviour
    {
        private HubHud hud;
        private CareerManager career;

        private struct BuildingSpec
        {
            public HubBuildingType type;
            public string name;
            public Vector2 pos;
            public Color color;

            public BuildingSpec(HubBuildingType type, string name, Vector2 pos, Color color)
            {
                this.type = type; this.name = name; this.pos = pos; this.color = color;
            }
        }

        private void Start()
        {
            DisableLegacyMenu();
            EnsureCareerManager();

            hud = HubHud.Create();
            BuildWorld();

            career = CareerManager.Instance;
            career.StateChanged += RefreshStatus;
            RefreshStatus();

            if (!career.PlayerCreated)
                hud.Toast($"Skip: \"Welcome to {career.club.clubName}, coach! First things first — head to the Office and we'll figure out who you are.\"", 8f);
            else
                hud.Toast($"Welcome back to {career.club.clubName}. Skip: \"Have a wander — try the Cafe, and check the roster with R.\"");
        }

        private void OnDestroy()
        {
            if (career != null) career.StateChanged -= RefreshStatus;
        }

        private void RefreshStatus()
        {
            string status = $"<b>{career.club.clubName}</b>    Day {career.day} — {career.currentDate:ddd, MMM d}    " +
                            $"Actions: {career.actionsRemaining}/{career.actionsPerDay}";
            if (!career.PlayerCreated)
                status += "\n<color=#FFD75F>→ Visit the Office to create your character</color>";
            hud.SetStatus(status);
        }

        private void DisableLegacyMenu()
        {
            var oldButton = GameObject.Find("PlayBasketballButton");
            if (oldButton != null) oldButton.SetActive(false);
        }

        private void EnsureCareerManager()
        {
            if (CareerManager.Instance == null && Object.FindFirstObjectByType<CareerManager>() == null)
                new GameObject("CareerManager").AddComponent<CareerManager>();
        }

        private void BuildWorld()
        {
            // Ground.
            var ground = MakeSpriteObject("Ground", new Color(0.16f, 0.28f, 0.18f), Vector2.zero, sortingOrder: 0);
            ground.transform.localScale = new Vector3(26f, 17f, 1f);

            // Buildings.
            var specs = new[]
            {
                new BuildingSpec(HubBuildingType.Arena,    "Arena",          new Vector2(0f, 5f),   new Color(0.55f, 0.35f, 0.75f)),
                new BuildingSpec(HubBuildingType.Office,   "Office",         new Vector2(-7f, 3f),  new Color(0.30f, 0.50f, 0.85f)),
                new BuildingSpec(HubBuildingType.Practice, "Practice Field", new Vector2(7f, 3f),   new Color(0.85f, 0.55f, 0.20f)),
                new BuildingSpec(HubBuildingType.Hospital, "Hospital",       new Vector2(-7f, -3f), new Color(0.90f, 0.90f, 0.92f)),
                new BuildingSpec(HubBuildingType.Cafe,     "Cafe",           new Vector2(7f, -3f),  new Color(0.75f, 0.55f, 0.35f)),
                new BuildingSpec(HubBuildingType.Home,     "Home",           new Vector2(0f, -5.5f), new Color(0.35f, 0.70f, 0.65f)),
            };

            var buildings = new HubBuilding[specs.Length];
            for (int i = 0; i < specs.Length; i++)
            {
                var spec = specs[i];
                var go = MakeSpriteObject(spec.name, spec.color, spec.pos, sortingOrder: 2);
                go.transform.localScale = new Vector3(3f, 2f, 1f);

                var building = go.AddComponent<HubBuilding>();
                building.type = spec.type;
                building.displayName = spec.name;
                buildings[i] = building;

                MakeLabel(go.transform, spec.name, new Vector2(0f, 0.85f));
            }

            // Player.
            var player = MakeSpriteObject("HubPlayer", new Color(0.95f, 0.85f, 0.30f), Vector2.zero, sortingOrder: 5);
            player.transform.localScale = new Vector3(0.7f, 1f, 1f);
            player.AddComponent<HubPlayerController>();
            player.AddComponent<HubInteractor>().Init(buildings, hud);

            // Camera.
            var cam = Camera.main;
            if (cam != null)
            {
                cam.orthographic = true;
                cam.orthographicSize = 6.5f;
                cam.backgroundColor = new Color(0.09f, 0.12f, 0.10f);
                var follow = cam.GetComponent<HubCameraFollow>();
                if (follow == null) follow = cam.gameObject.AddComponent<HubCameraFollow>();
                follow.target = player.transform;
            }
        }

        private GameObject MakeSpriteObject(string name, Color color, Vector2 pos, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = SolidSprite();
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            return go;
        }

        private static Sprite solidSprite;

        /// <summary>A shared 1x1-unit white sprite; tinted per renderer.</summary>
        private static Sprite SolidSprite()
        {
            if (solidSprite != null) return solidSprite;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            tex.filterMode = FilterMode.Point;
            solidSprite = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), pixelsPerUnit: 1f);
            return solidSprite;
        }

        private static void MakeLabel(Transform parent, string text, Vector2 localOffset)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localOffset;

            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.fontSize = 5f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.rectTransform.sizeDelta = new Vector2(4f, 1f);

            // Counter the parent's non-uniform scale so text isn't stretched.
            var parentScale = parent.localScale;
            go.transform.localScale = new Vector3(1f / parentScale.x, 1f / parentScale.y, 1f);

            var meshRenderer = go.GetComponent<MeshRenderer>();
            if (meshRenderer != null) meshRenderer.sortingOrder = 6;
        }
    }
}
