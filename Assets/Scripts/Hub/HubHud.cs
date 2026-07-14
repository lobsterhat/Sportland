using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Sportland.Career;

namespace Sportland.Hub
{
    /// <summary>
    /// Runtime-built HUD for the hub: day/actions status, interaction prompt,
    /// toast messages, and a toggleable roster panel that shows the discovery
    /// state of every trait (revealed grade or "?").
    /// Built entirely in code so the slice needs no hand-authored UI assets.
    /// </summary>
    public class HubHud : MonoBehaviour
    {
        private TextMeshProUGUI statusText;
        private TextMeshProUGUI promptText;
        private TextMeshProUGUI toastText;
        private TextMeshProUGUI hintText;
        private GameObject rosterPanel;
        private TextMeshProUGUI rosterText;
        private GameObject creatorPanel;
        private TextMeshProUGUI creatorText;
        private Coroutine toastRoutine;

        public static HubHud Create()
        {
            var root = new GameObject("HubHud");
            var hud = root.AddComponent<HubHud>();

            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(root.transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            hud.statusText = MakeText(canvasGo.transform, "Status", 30f,
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(0f, 1f),
                pivot: new Vector2(0f, 1f), pos: new Vector2(24f, -20f),
                size: new Vector2(900f, 90f), align: TextAlignmentOptions.TopLeft);

            hud.promptText = MakeText(canvasGo.transform, "Prompt", 32f,
                anchorMin: new Vector2(0.5f, 0f), anchorMax: new Vector2(0.5f, 0f),
                pivot: new Vector2(0.5f, 0f), pos: new Vector2(0f, 110f),
                size: new Vector2(1400f, 50f), align: TextAlignmentOptions.Center);

            hud.toastText = MakeText(canvasGo.transform, "Toast", 30f,
                anchorMin: new Vector2(0.5f, 0f), anchorMax: new Vector2(0.5f, 0f),
                pivot: new Vector2(0.5f, 0f), pos: new Vector2(0f, 170f),
                size: new Vector2(1500f, 50f), align: TextAlignmentOptions.Center);
            hud.toastText.color = new Color(1f, 0.95f, 0.6f);

            hud.hintText = MakeText(canvasGo.transform, "Hints", 24f,
                anchorMin: new Vector2(0.5f, 0f), anchorMax: new Vector2(0.5f, 0f),
                pivot: new Vector2(0.5f, 0f), pos: new Vector2(0f, 24f),
                size: new Vector2(1400f, 40f), align: TextAlignmentOptions.Center);
            hud.hintText.text = "Move: WASD / left stick    Confirm: E / Cross    Cancel: Esc / Circle    Roster: R / Triangle";
            hud.hintText.color = new Color(1f, 1f, 1f, 0.55f);

            // Roster panel: dark backdrop + monospaced-ish listing, hidden by default.
            hud.rosterPanel = new GameObject("RosterPanel");
            hud.rosterPanel.transform.SetParent(canvasGo.transform, false);
            var bg = hud.rosterPanel.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.82f);
            var bgRect = hud.rosterPanel.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0.5f, 0.5f);
            bgRect.anchorMax = new Vector2(0.5f, 0.5f);
            bgRect.pivot = new Vector2(0.5f, 0.5f);
            bgRect.anchoredPosition = Vector2.zero;
            bgRect.sizeDelta = new Vector2(1500f, 760f);

            hud.rosterText = MakeText(hud.rosterPanel.transform, "RosterText", 26f,
                anchorMin: Vector2.zero, anchorMax: Vector2.one,
                pivot: new Vector2(0.5f, 0.5f), pos: Vector2.zero,
                size: new Vector2(-60f, -50f), align: TextAlignmentOptions.TopLeft);
            hud.rosterPanel.SetActive(false);

            // Character creator panel: same construction as the roster.
            hud.creatorPanel = new GameObject("CreatorPanel");
            hud.creatorPanel.transform.SetParent(canvasGo.transform, false);
            var creatorBg = hud.creatorPanel.AddComponent<Image>();
            creatorBg.color = new Color(0.02f, 0.05f, 0.10f, 0.92f);
            var creatorRect = hud.creatorPanel.GetComponent<RectTransform>();
            creatorRect.anchorMin = new Vector2(0.5f, 0.5f);
            creatorRect.anchorMax = new Vector2(0.5f, 0.5f);
            creatorRect.pivot = new Vector2(0.5f, 0.5f);
            creatorRect.anchoredPosition = Vector2.zero;
            creatorRect.sizeDelta = new Vector2(1300f, 820f);

            hud.creatorText = MakeText(hud.creatorPanel.transform, "CreatorText", 28f,
                anchorMin: Vector2.zero, anchorMax: Vector2.one,
                pivot: new Vector2(0.5f, 0.5f), pos: Vector2.zero,
                size: new Vector2(-70f, -50f), align: TextAlignmentOptions.TopLeft);
            hud.creatorPanel.SetActive(false);

            return hud;
        }

        private static TextMeshProUGUI MakeText(Transform parent, string name, float fontSize,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size,
            TextAlignmentOptions align)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.alignment = align;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            var rect = text.rectTransform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            return text;
        }

        public void SetStatus(string s) => statusText.text = s;
        public void SetPrompt(string s) => promptText.text = s;

        public void Toast(string s, float seconds = 3.5f)
        {
            if (toastRoutine != null) StopCoroutine(toastRoutine);
            toastRoutine = StartCoroutine(ToastRoutine(s, seconds));
        }

        private IEnumerator ToastRoutine(string s, float seconds)
        {
            toastText.text = s;
            yield return new WaitForSeconds(seconds);
            toastText.text = "";
            toastRoutine = null;
        }

        public bool RosterVisible => rosterPanel.activeSelf;

        public void CloseRoster() => rosterPanel.SetActive(false);

        // ── Character creator ───────────────────────────────────────────

        public bool CreatorVisible => creatorPanel.activeSelf;

        public void ShowCreator(int selectedIndex)
        {
            creatorText.text = BuildCreatorText(selectedIndex);
            creatorPanel.SetActive(true);
        }

        public void CloseCreator() => creatorPanel.SetActive(false);

        private static string BuildCreatorText(int selected)
        {
            var all = Archetypes.All;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<b>CREATE YOUR CHARACTER</b> — choose an archetype");
            sb.AppendLine("<alpha=#88>The trade-off is the choice: playing yourself vs. running the club.</alpha>");
            sb.AppendLine();

            for (int i = 0; i < all.Length; i++)
            {
                var a = all[i];
                if (i == selected)
                    sb.AppendLine($"<color=#FFD75F>>  {a.displayName}</color>   <alpha=#AA>Playing {a.playingGrade} · Management {a.managementGrade} · {a.actionsPerDay} actions/day</alpha>");
                else
                    sb.AppendLine($"<alpha=#66>   {a.displayName}</alpha>");
            }

            var sel = all[selected];
            sb.AppendLine();
            sb.AppendLine($"<b>{sel.displayName}</b> — <i>{sel.fantasy}</i>");
            sb.AppendLine($"Perk: <b>{sel.perkName}</b> — {sel.perkDescription}");
            sb.AppendLine($"The catch: {sel.theCatch}");
            sb.AppendLine();
            sb.AppendLine($"<color=#7FDBFF>Skip:</color> {sel.skipLine}");
            sb.AppendLine();
            sb.AppendLine("<alpha=#88>W/S or D-Pad: select    E/Cross: confirm    Esc/Circle: decide later</alpha>");
            return sb.ToString();
        }

        public void ToggleRoster(Club club)
        {
            if (rosterPanel.activeSelf) { rosterPanel.SetActive(false); return; }
            rosterText.text = BuildRosterText(club);
            rosterPanel.SetActive(true);
        }

        private static string BuildRosterText(Club club)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<b>{club.clubName}</b> — club pool ({club.pool.Count})   " +
                          "<alpha=#88>ratings SPD/AGI/END/TGH · ego PT/POS/ST/SPT/REC/WL · disp SOC/CMP/OPN · VOL</alpha>");
            sb.AppendLine();

            foreach (var a in club.pool)
            {
                string archetypeName = a.isPlayerCharacter ? Archetypes.NameOf(a.archetypeId) : "";
                string tag = a.isPlayerCharacter
                    ? (archetypeName.Length > 0 ? $" <color=#7FDBFF>(you — {archetypeName})</color>" : " <color=#7FDBFF>(you)</color>")
                    : a.isMentor ? " <color=#7FDBFF>(mentor)</color>" : "";

                sb.Append($"{a.FullName,-18}{tag}  {a.age,2}   ");
                sb.Append($"{a.GetGeneral(GeneralRating.Speed).DisplayGrade}/" +
                          $"{a.GetGeneral(GeneralRating.Agility).DisplayGrade}/" +
                          $"{a.GetGeneral(GeneralRating.Endurance).DisplayGrade}/" +
                          $"{a.GetGeneral(GeneralRating.Toughness).DisplayGrade}   ");

                if (a.IsEgoImmune)
                {
                    sb.Append("<alpha=#88>ego: — (immune)</alpha>");
                }
                else
                {
                    sb.Append("ego ");
                    for (int i = 0; i < a.expectations.Length; i++)
                        sb.Append(a.expectations[i].DisplayGrade).Append(i < a.expectations.Length - 1 ? "/" : "   ");
                    sb.Append("disp ");
                    for (int i = 0; i < a.dispositions.Length; i++)
                        sb.Append(a.dispositions[i].DisplayGrade).Append(i < a.dispositions.Length - 1 ? "/" : "   ");
                    sb.Append("VOL ").Append(a.volatility.DisplayGrade);
                }

                sb.Append($"   <alpha=#88>fatigue {Mathf.RoundToInt(a.fatigue)}%</alpha>");
                sb.AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine("<alpha=#88>? = not yet discovered — scout, or learn them over dinner at the Cafe.</alpha>");
            return sb.ToString();
        }
    }
}
