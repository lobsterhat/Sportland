using UnityEngine;
using UnityEngine.UI;
using Sportland.InputHandling;
using Sportland.Sports.Tag;

namespace Sportland.Rendering
{
    /// <summary>
    /// Builds and drives a bottom-screen HUD bar showing the currently controlled
    /// character's name, stamina, stats, and available abilities.
    ///
    /// Layout (left → right):
    ///   [Headshot] | [Name / Stamina / Stats] | [Ability 1] [Ability 2] [Ability 3]
    ///
    /// The bar height is read from PixelArtRenderer.HudBarHeight on the Main Camera
    /// so both components always agree on how much screen space is reserved.
    ///
    /// TODO: abstract ability population behind a sport-agnostic interface once
    ///       additional sports are added. Currently hardcoded for Tag.
    ///
    /// Setup: attach to any scene GameObject (e.g. Main Camera). No other config needed.
    /// </summary>
    public class PlayerInfoBar : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        //  COLOURS
        // ──────────────────────────────────────────────

        private static readonly Color ColBackground  = new Color(0.08f, 0.08f, 0.10f, 0.96f);
        private static readonly Color ColDivider     = new Color(0.25f, 0.25f, 0.30f, 1.00f);
        private static readonly Color ColHeadshot    = new Color(0.35f, 0.20f, 0.50f, 1.00f);
        private static readonly Color ColStaminaBg   = new Color(0.18f, 0.18f, 0.18f, 1.00f);
        private static readonly Color ColStatLabel   = new Color(0.55f, 0.55f, 0.60f, 1.00f);
        private static readonly Color ColStatValue   = new Color(0.90f, 0.90f, 0.95f, 1.00f);
        private static readonly Color ColAbilityAvail  = new Color(0.15f, 0.60f, 0.25f, 1.00f);
        private static readonly Color ColAbilityCool   = new Color(0.75f, 0.38f, 0.05f, 1.00f);
        private static readonly Color ColAbilityLocked = new Color(0.22f, 0.22f, 0.25f, 1.00f);
        private static readonly Color ColRoleIt      = new Color(1.00f, 0.30f, 0.30f, 1.00f);
        private static readonly Color ColRoleRunner  = new Color(0.90f, 0.90f, 0.90f, 1.00f);

        // ──────────────────────────────────────────────
        //  INNER TYPE
        // ──────────────────────────────────────────────

        private class AbilitySlotUI
        {
            public Image     background;
            public Image     iconPlaceholder;
            public Text      nameLabel;
            public Image     cooldownTrack;
            public Image     cooldownFill;
            public Text      statusLabel;
        }

        // ──────────────────────────────────────────────
        //  RUNTIME STATE
        // ──────────────────────────────────────────────

        private float barHeight;

        // Tracked player
        private TagMovementController trackedPlayer;

        // UI references — populated in BuildBar()
        private Image  headshot;
        private Text   nameLabel;
        private Text   roleLabel;
        private Image  staminaFill;
        private Text   statSpeed;
        private Text   statAgility;
        private Text   statEndurance;
        private readonly AbilitySlotUI[] slots = new AbilitySlotUI[3];

        // ──────────────────────────────────────────────
        //  LIFECYCLE
        // ──────────────────────────────────────────────

        private void Start()
        {
            // Read bar height from PixelArtRenderer so both agree
            var renderer = Camera.main != null
                ? Camera.main.GetComponent<PixelArtRenderer>()
                : null;
            barHeight = renderer != null ? renderer.HudBarHeight : 200f;

            BuildBar();
            TryFindPlayer();
        }

        private void Update()
        {
            if (trackedPlayer == null)
            {
                TryFindPlayer();
                return;
            }

            Refresh();
        }

        // ──────────────────────────────────────────────
        //  PLAYER ACQUISITION
        // ──────────────────────────────────────────────

        private void TryFindPlayer()
        {
            var brokers = Object.FindObjectsByType<InputBroker>(FindObjectsSortMode.None);
            foreach (var broker in brokers)
            {
                if (!broker.IsPlayerControlled) continue;
                var mc = broker.GetComponent<TagMovementController>();
                if (mc != null) { trackedPlayer = mc; return; }
            }
            trackedPlayer = null;
        }

        // ──────────────────────────────────────────────
        //  REFRESH
        // ──────────────────────────────────────────────

        private void Refresh()
        {
            // Name & role
            nameLabel.text  = trackedPlayer.gameObject.name;
            bool isIt       = trackedPlayer.CurrentRole == TagMovementController.TagRole.It;
            roleLabel.text  = trackedPlayer.IsEliminated ? "ELIMINATED"
                            : isIt                        ? "IT"
                                                          : "RUNNER";
            roleLabel.color = trackedPlayer.IsEliminated ? ColAbilityLocked
                            : isIt                        ? ColRoleIt
                                                          : ColRoleRunner;

            // Headshot tint mirrors role colour
            headshot.color = trackedPlayer.IsEliminated ? new Color(0.25f, 0.25f, 0.25f)
                           : isIt                        ? new Color(0.6f, 0.15f, 0.15f)
                                                         : ColHeadshot;

            // Stamina bar
            float stamina = trackedPlayer.GetNormalizedStamina();
            staminaFill.fillAmount = stamina;
            staminaFill.color = Color.Lerp(new Color(0.85f, 0.15f, 0.15f),
                                           new Color(0.15f, 0.80f, 0.25f), stamina);

            // Stats from movement profile
            var profile = trackedPlayer.Profile;
            if (profile != null)
            {
                statSpeed.text    = $"{profile.topSpeed:F1}";
                statAgility.text  = $"{profile.turnSpeed:F1}";
                statEndurance.text = $"{profile.maxStamina:F0}";
            }

            // Abilities — Tag-specific, 3 slots
            // TODO: replace with sport-agnostic IAbilityProvider interface
            if (isIt)
            {
                RefreshSlot(slots[0], "LUNGE",
                    available:        trackedPlayer.LungeReady,
                    cooldownNorm:     0f,
                    locked:           trackedPlayer.IsEliminated);

                RefreshSlot(slots[1], "TAG",
                    available:        trackedPlayer.CanAct() && !trackedPlayer.IsEliminated,
                    cooldownNorm:     0f,
                    locked:           trackedPlayer.IsEliminated);

                RefreshSlot(slots[2], "—",
                    available:        false,
                    cooldownNorm:     0f,
                    locked:           true);
            }
            else
            {
                RefreshSlot(slots[0], "BARGE",
                    available:        trackedPlayer.BargeReady,
                    cooldownNorm:     trackedPlayer.BargeCooldownNormalized,
                    locked:           trackedPlayer.IsEliminated);

                RefreshSlot(slots[1], "EVADE",
                    available:        trackedPlayer.EvasionReady,
                    cooldownNorm:     trackedPlayer.EvasionCooldownNormalized,
                    locked:           trackedPlayer.IsEliminated);

                RefreshSlot(slots[2], "—",
                    available:        false,
                    cooldownNorm:     0f,
                    locked:           true);
            }
        }

        private static void RefreshSlot(AbilitySlotUI slot, string label,
                                        bool available, float cooldownNorm, bool locked)
        {
            slot.nameLabel.text = label;

            if (locked || label == "—")
            {
                slot.background.color   = ColAbilityLocked;
                slot.cooldownFill.fillAmount = 0f;
                slot.statusLabel.text   = "—";
                slot.statusLabel.color  = ColStatLabel;
            }
            else if (cooldownNorm > 0f)
            {
                slot.background.color        = ColAbilityCool;
                slot.cooldownFill.fillAmount = 1f - cooldownNorm;
                slot.statusLabel.text        = $"{cooldownNorm * 5f:F1}s"; // approx display
                slot.statusLabel.color       = new Color(1f, 0.75f, 0.3f);
            }
            else if (available)
            {
                slot.background.color        = ColAbilityAvail;
                slot.cooldownFill.fillAmount = 1f;
                slot.statusLabel.text        = "READY";
                slot.statusLabel.color       = new Color(0.6f, 1f, 0.65f);
            }
            else
            {
                slot.background.color        = ColAbilityLocked;
                slot.cooldownFill.fillAmount = 0f;
                slot.statusLabel.text        = "...";
                slot.statusLabel.color       = ColStatLabel;
            }
        }

        // ──────────────────────────────────────────────
        //  UI CONSTRUCTION
        // ──────────────────────────────────────────────

        private void BuildBar()
        {
            Font font = ResolveFont();

            // ── Root canvas ───────────────────────────
            var canvasGO = new GameObject("PlayerInfoBar");
            var canvas   = canvasGO.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20; // above game HUD (10) and game view (0)
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            // ── Background strip (bottom of screen) ───
            var bg     = MakePanel(canvasGO.transform, "Background", ColBackground);
            var bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin        = new Vector2(0f, 0f);
            bgRect.anchorMax        = new Vector2(1f, 0f);
            bgRect.pivot            = new Vector2(0.5f, 0f);
            bgRect.anchoredPosition = Vector2.zero;
            bgRect.sizeDelta        = new Vector2(0f, barHeight);

            float pad   = barHeight * 0.08f;
            float inner = barHeight - pad * 2f;

            // ── Headshot (left square) ────────────────
            float headW = inner;
            var headGO  = MakePanel(bg.transform, "Headshot", ColHeadshot);
            headshot    = headGO.GetComponent<Image>();
            var headRect = headGO.GetComponent<RectTransform>();
            headRect.anchorMin        = new Vector2(0f, 0f);
            headRect.anchorMax        = new Vector2(0f, 1f);
            headRect.pivot            = new Vector2(0f, 0.5f);
            headRect.anchoredPosition = new Vector2(pad, 0f);
            headRect.sizeDelta        = new Vector2(headW, -pad * 2f);

            // "IT" / "RUNNER" label inside headshot
            roleLabel = MakeText(headGO.transform, "RoleLabel", font,
                fontSize: Mathf.RoundToInt(barHeight * 0.14f),
                color: ColRoleRunner, style: FontStyle.Bold);
            StretchFill(roleLabel.GetComponent<RectTransform>());
            roleLabel.text      = "RUNNER";
            roleLabel.alignment = TextAnchor.LowerCenter;

            // ── Info section ──────────────────────────
            float infoLeft  = pad + headW + pad;
            float infoRight = 0.38f; // 38% from the right edge is where abilities start

            var infoRT = MakeAnchoredRect(bg.transform, "InfoSection",
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(infoRight, 1f),
                offsetMin: new Vector2(infoLeft, pad),
                offsetMax: new Vector2(-pad, -pad));

            // Player name
            nameLabel = MakeText(infoRT, "PlayerName", font,
                fontSize: Mathf.RoundToInt(barHeight * 0.22f),
                color: Color.white, style: FontStyle.Bold);
            var nameRect = nameLabel.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 0.6f);
            nameRect.anchorMax = Vector2.one;
            nameRect.offsetMin = Vector2.zero;
            nameRect.offsetMax = Vector2.zero;
            nameLabel.alignment = TextAnchor.UpperLeft;
            nameLabel.text      = "—";

            // Stamina track + fill
            var staminaBg   = MakePanel(infoRT, "StaminaBg", ColStaminaBg);
            var staminaBgRT = staminaBg.GetComponent<RectTransform>();
            staminaBgRT.anchorMin = new Vector2(0f, 0.35f);
            staminaBgRT.anchorMax = new Vector2(1f, 0.58f);
            staminaBgRT.offsetMin = Vector2.zero;
            staminaBgRT.offsetMax = Vector2.zero;

            var staminaFillGO    = MakePanel(staminaBg.transform, "StaminaFill",
                                             new Color(0.2f, 0.8f, 0.3f));
            staminaFill = staminaFillGO.GetComponent<Image>();
            staminaFill.type      = Image.Type.Filled;
            staminaFill.fillMethod = Image.FillMethod.Horizontal;
            staminaFill.fillAmount = 1f;
            StretchFill(staminaFillGO.GetComponent<RectTransform>());

            // "STAMINA" label beside bar
            var stLbl = MakeText(infoRT, "StaminaLabel", font,
                fontSize: Mathf.RoundToInt(barHeight * 0.11f),
                color: ColStatLabel, style: FontStyle.Normal);
            var stLblRT       = stLbl.GetComponent<RectTransform>();
            stLblRT.anchorMin = new Vector2(0f, 0.55f);
            stLblRT.anchorMax = new Vector2(1f, 0.70f);
            stLblRT.offsetMin = Vector2.zero;
            stLblRT.offsetMax = Vector2.zero;
            stLbl.text        = "STAMINA";
            stLbl.alignment   = TextAnchor.LowerLeft;

            // Stats row (bottom): SPD  AGI  END
            BuildStatRow(infoRT, font, pad);

            // ── Ability divider ───────────────────────
            var divRT = MakeAnchoredRect(bg.transform, "Divider",
                anchorMin: new Vector2(infoRight, 0f),
                anchorMax: new Vector2(infoRight, 1f),
                offsetMin: new Vector2(-1f, pad),
                offsetMax: new Vector2(1f, -pad));
            MakePanel(divRT, "DividerLine", ColDivider);

            // ── Ability slots (right 62%) ─────────────
            var abRT = MakeAnchoredRect(bg.transform, "Abilities",
                anchorMin: new Vector2(infoRight, 0f),
                anchorMax: new Vector2(1f, 1f),
                offsetMin: new Vector2(pad, pad),
                offsetMax: new Vector2(-pad, -pad));

            float slotW = 1f / 3f;
            for (int i = 0; i < 3; i++)
            {
                slots[i] = BuildAbilitySlot(abRT, font, i, slotW, pad * 0.5f);
            }
        }

        private AbilitySlotUI BuildAbilitySlot(Transform parent, Font font,
                                               int index, float slotFraction, float pad)
        {
            float x0 = index * slotFraction;
            float x1 = x0 + slotFraction;

            var slotRT = MakeAnchoredRect(parent, $"Slot{index}",
                anchorMin: new Vector2(x0, 0f),
                anchorMax: new Vector2(x1, 1f),
                offsetMin: new Vector2(pad, 0f),
                offsetMax: new Vector2(-pad, 0f));

            var slot = new AbilitySlotUI();

            // Background
            slot.background = MakePanel(slotRT, "Bg", ColAbilityLocked).GetComponent<Image>();
            StretchFill(slot.background.GetComponent<RectTransform>());

            // Icon placeholder (top 40%)
            var iconGO = MakePanel(slotRT, "Icon", new Color(0f, 0f, 0f, 0.3f));
            slot.iconPlaceholder = iconGO.GetComponent<Image>();
            var iconRT       = iconGO.GetComponent<RectTransform>();
            iconRT.anchorMin = new Vector2(0.1f, 0.52f);
            iconRT.anchorMax = new Vector2(0.9f, 0.95f);
            iconRT.offsetMin = Vector2.zero;
            iconRT.offsetMax = Vector2.zero;

            // Ability name (middle)
            slot.nameLabel = MakeText(slotRT, "Name", font,
                fontSize: Mathf.RoundToInt(barHeight * 0.13f),
                color: Color.white, style: FontStyle.Bold);
            var nameRT       = slot.nameLabel.GetComponent<RectTransform>();
            nameRT.anchorMin = new Vector2(0f, 0.30f);
            nameRT.anchorMax = new Vector2(1f, 0.54f);
            nameRT.offsetMin = Vector2.zero;
            nameRT.offsetMax = Vector2.zero;
            slot.nameLabel.alignment = TextAnchor.MiddleCenter;
            slot.nameLabel.text      = "—";

            // Status text (below name)
            slot.statusLabel = MakeText(slotRT, "Status", font,
                fontSize: Mathf.RoundToInt(barHeight * 0.11f),
                color: ColStatLabel, style: FontStyle.Normal);
            var statusRT       = slot.statusLabel.GetComponent<RectTransform>();
            statusRT.anchorMin = new Vector2(0f, 0.12f);
            statusRT.anchorMax = new Vector2(1f, 0.32f);
            statusRT.offsetMin = Vector2.zero;
            statusRT.offsetMax = Vector2.zero;
            slot.statusLabel.alignment = TextAnchor.MiddleCenter;
            slot.statusLabel.text      = "—";

            // Cooldown track (thin bar at bottom)
            var trackGO = MakePanel(slotRT, "CooldownTrack", ColStaminaBg);
            slot.cooldownTrack = trackGO.GetComponent<Image>();
            var trackRT       = trackGO.GetComponent<RectTransform>();
            trackRT.anchorMin = new Vector2(0f, 0f);
            trackRT.anchorMax = new Vector2(1f, 0.12f);
            trackRT.offsetMin = Vector2.zero;
            trackRT.offsetMax = Vector2.zero;

            var fillGO = MakePanel(trackGO.transform, "Fill", ColAbilityAvail);
            slot.cooldownFill       = fillGO.GetComponent<Image>();
            slot.cooldownFill.type  = Image.Type.Filled;
            slot.cooldownFill.fillMethod = Image.FillMethod.Horizontal;
            slot.cooldownFill.fillAmount = 0f;
            StretchFill(fillGO.GetComponent<RectTransform>());

            return slot;
        }

        private void BuildStatRow(Transform parent, Font font, float pad)
        {
            // Three mini stat pairs: label / value
            string[] labels = { "SPD", "AGI", "END" };
            float w = 1f / 3f;

            var rowRT = MakeAnchoredRect(parent, "StatsRow",
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(1f, 0.33f),
                offsetMin: Vector2.zero,
                offsetMax: Vector2.zero);

            var texts = new Text[3];
            for (int i = 0; i < 3; i++)
            {
                float x0 = i * w;
                var cell = MakeAnchoredRect(rowRT, $"Stat{i}",
                    anchorMin: new Vector2(x0, 0f),
                    anchorMax: new Vector2(x0 + w, 1f),
                    offsetMin: Vector2.zero,
                    offsetMax: Vector2.zero);

                var lbl = MakeText(cell, "Label", font,
                    fontSize: Mathf.RoundToInt(barHeight * 0.10f),
                    color: ColStatLabel, style: FontStyle.Normal);
                lbl.text      = labels[i];
                lbl.alignment = TextAnchor.UpperLeft;
                var lblRT     = lbl.GetComponent<RectTransform>();
                lblRT.anchorMin = new Vector2(0f, 0.5f);
                lblRT.anchorMax = Vector2.one;
                StretchFill(lblRT);

                var val = MakeText(cell, "Value", font,
                    fontSize: Mathf.RoundToInt(barHeight * 0.13f),
                    color: ColStatValue, style: FontStyle.Bold);
                val.text      = "—";
                val.alignment = TextAnchor.LowerLeft;
                var valRT     = val.GetComponent<RectTransform>();
                valRT.anchorMin = Vector2.zero;
                valRT.anchorMax = new Vector2(1f, 0.5f);
                StretchFill(valRT);

                texts[i] = val;
            }

            statSpeed    = texts[0];
            statAgility  = texts[1];
            statEndurance = texts[2];
        }

        // ──────────────────────────────────────────────
        //  UI FACTORY HELPERS
        // ──────────────────────────────────────────────

        private static Image MakePanel(Transform parent, string name, Color color)
        {
            var go    = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var img   = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static Text MakeText(Transform parent, string name, Font font,
                                     int fontSize, Color color, FontStyle style)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var txt       = go.AddComponent<Text>();
            txt.font      = font;
            txt.fontSize  = fontSize;
            txt.fontStyle = style;
            txt.color     = color;
            txt.raycastTarget = false;
            return txt;
        }

        /// <summary>Creates a RectTransform-only container with specified anchors and offsets.</summary>
        private static RectTransform MakeAnchoredRect(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt         = go.AddComponent<RectTransform>();
            rt.anchorMin   = anchorMin;
            rt.anchorMax   = anchorMax;
            rt.offsetMin   = offsetMin;
            rt.offsetMax   = offsetMax;
            return rt;
        }

        private static void StretchFill(RectTransform rt)
        {
            rt.anchorMin  = Vector2.zero;
            rt.anchorMax  = Vector2.one;
            rt.offsetMin  = Vector2.zero;
            rt.offsetMax  = Vector2.zero;
        }

        private static Font ResolveFont()
        {
            var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f != null) return f;
            f = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (f != null) return f;
            return Font.CreateDynamicFontFromOSFont("Arial", 12);
        }
    }
}
