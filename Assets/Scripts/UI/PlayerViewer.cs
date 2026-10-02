using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Sportland.Career;
using Sportland.Sports.Dodgeball;
using CoreRating = Sportland.Core.Rating;

namespace Sportland.UI
{
    /// <summary>
    /// One graded skill on the player card. <see cref="BaseValue"/> is the
    /// sheet. <see cref="CurrentValue"/> is the sheet after a temporary
    /// multiplier (an ability that's firing, stamina). Both are 0–20; the
    /// card prints F–S.
    /// </summary>
    public struct SkillReadout
    {
        public string name;
        public float baseValue;
        public float currentValue;
    }

    /// <summary>An on/off attribute. <see cref="ActiveNow"/> means it is firing this moment.</summary>
    public struct AttributeReadout
    {
        public string name;
        public bool on;
        public bool activeNow;
    }

    /// <summary>Everything the player card draws. Built from a career record or a live court player.</summary>
    public class PlayerCard
    {
        public string name;
        public string detail;
        public List<SkillReadout> general = new List<SkillReadout>();
        public List<SkillReadout> dodgeball = new List<SkillReadout>();
        public List<AttributeReadout> attributes = new List<AttributeReadout>();

        public static PlayerCard FromAthlete(CareerAthlete athlete)
        {
            athlete.EnsureDodgeballSkills();
            var card = new PlayerCard
            {
                name = athlete.FullName,
                detail = $"{athlete.age}   {athlete.phase}",
            };
            card.general.Add(General(athlete, "Speed", GeneralRating.Speed));
            card.general.Add(General(athlete, "Agility", GeneralRating.Agility));
            card.general.Add(General(athlete, "Endurance", GeneralRating.Endurance));
            card.general.Add(General(athlete, "Toughness", GeneralRating.Toughness));
            card.dodgeball.Add(Sheet(athlete, "Throw Power", DodgeballSkill.ThrowPower));
            card.dodgeball.Add(Sheet(athlete, "Throw Tech", DodgeballSkill.ThrowTechnique));
            card.dodgeball.Add(Sheet(athlete, "Catch Tech", DodgeballSkill.CatchTechnique));
            card.dodgeball.Add(Sheet(athlete, "Anticipation", DodgeballSkill.Anticipation));
            card.attributes.Add(Flag(athlete, "Hot Head", AthleteAbility.HotHead));
            card.attributes.Add(Flag(athlete, "Sole Survivor", AthleteAbility.SoleSurvivor));
            return card;
        }

        /// <summary>
        /// Live court player. Current dodgeball grades follow Effective*
        /// (base × the abilities and stamina that are on right now).
        /// </summary>
        public static PlayerCard FromMatchPlayer(GameObject player)
        {
            var tag = player.GetComponent<CareerAthleteTag>();
            var tracker = player.GetComponent<PlayerZoneTracker>();
            var athlete = tag != null ? FindAthlete(tag.athleteId) : null;

            var card = athlete != null
                ? FromAthlete(athlete)
                : new PlayerCard { name = player.name, detail = "" };

            if (tracker != null)
            {
                string role = tracker.Spawn.role == PlayerRole.Infielder ? "Infielder" : "Outfielder";
                string team = tracker.Spawn.team == Team.A ? "Team A" : "Team B";
                card.detail = string.IsNullOrEmpty(card.detail) ? $"{team}  {role}" : $"{card.detail}   {team}  {role}";
            }

            var dba = player.GetComponent<DodgeballAttributes>();
            if (dba != null)
            {
                card.dodgeball.Clear();
                card.dodgeball.Add(Live("Throw Power", dba.throwSpeedRating, dba.EffectiveThrowSpeed01));
                card.dodgeball.Add(Live("Throw Tech", dba.throwAccuracyRating, dba.EffectiveThrowAccuracy01));
                card.dodgeball.Add(Live("Catch Tech", dba.catchTechniqueRating, dba.EffectiveCatching01));
                // Anticipation is still stored 0–100 on the match component.
                card.dodgeball.Add(Live("Anticipation", dba.anticipation / 5f, dba.EffectiveAnticipation01));
            }

            var gen = player.GetComponent<GeneralAttributes>();
            if (gen != null && athlete == null)
            {
                card.general.Add(Flat("Agility", gen.changeOfDirection / 5f));
                card.general.Add(Flat("Endurance", gen.endurance / 5f));
                card.general.Add(Flat("Toughness", gen.toughness / 5f));
            }

            var host = player.GetComponent<PlayerAbilities>();
            if (host != null)
                ApplyLiveAttributes(card, host);
            else if (card.attributes.Count == 0)
            {
                card.attributes.Add(new AttributeReadout { name = "Hot Head", on = false });
                card.attributes.Add(new AttributeReadout { name = "Sole Survivor", on = false });
            }

            return card;
        }

        private static void ApplyLiveAttributes(PlayerCard card, PlayerAbilities host)
        {
            bool hotOn = false, hotLive = false, soleOn = false, soleLive = false;
            var runtimes = host.Runtimes;
            for (int i = 0; i < runtimes.Count; i++)
            {
                var rt = runtimes[i];
                if (rt.ability == null) continue;
                if (rt.ability.id == "hot_head") { hotOn = true; hotLive = rt.active; }
                else if (rt.ability.id == "sole_survivor") { soleOn = true; soleLive = rt.active; }
            }

            // A career flag means they have it even before the runtime has been built.
            for (int i = 0; i < card.attributes.Count; i++)
            {
                var row = card.attributes[i];
                if (row.name == "Hot Head")
                    card.attributes[i] = new AttributeReadout { name = row.name, on = row.on || hotOn, activeNow = hotLive };
                else if (row.name == "Sole Survivor")
                    card.attributes[i] = new AttributeReadout { name = row.name, on = row.on || soleOn, activeNow = soleLive };
            }

            if (card.attributes.Count == 0)
            {
                card.attributes.Add(new AttributeReadout { name = "Hot Head", on = hotOn, activeNow = hotLive });
                card.attributes.Add(new AttributeReadout { name = "Sole Survivor", on = soleOn, activeNow = soleLive });
            }
        }

        private static SkillReadout General(CareerAthlete athlete, string name, GeneralRating rating)
        {
            float v = athlete.GetGeneral(rating).value;
            return new SkillReadout { name = name, baseValue = v, currentValue = v };
        }

        private static SkillReadout Sheet(CareerAthlete athlete, string name, DodgeballSkill skill)
        {
            float v = athlete.GetDodgeball(skill).current;
            return new SkillReadout { name = name, baseValue = v, currentValue = v };
        }

        private static SkillReadout Live(string name, float baseValue, float effective01)
        {
            return new SkillReadout
            {
                name = name,
                baseValue = Mathf.Clamp(baseValue, 0f, CoreRating.Max),
                currentValue = Mathf.Clamp(effective01 * CoreRating.Max, 0f, CoreRating.Max),
            };
        }

        private static SkillReadout Flat(string name, float value)
        {
            float v = Mathf.Clamp(value, 0f, CoreRating.Max);
            return new SkillReadout { name = name, baseValue = v, currentValue = v };
        }

        private static AttributeReadout Flag(CareerAthlete athlete, string name, AthleteAbility flag)
        {
            return new AttributeReadout
            {
                name = name,
                on = (athlete.abilities & flag) != 0,
                activeNow = false,
            };
        }

        private static CareerAthlete FindAthlete(string id)
        {
            var career = CareerManager.Instance;
            if (career == null || string.IsNullOrEmpty(id)) return null;
            var own = career.AthleteById(id);
            if (own != null) return own;
            if (career.freeAgents != null)
            {
                for (int i = 0; i < career.freeAgents.Count; i++)
                    if (career.freeAgents[i] != null && career.freeAgents[i].id == id)
                        return career.freeAgents[i];
            }
            if (career.league == null || career.league.rivals == null) return null;
            for (int c = 0; c < career.league.rivals.Count; c++)
            {
                var roster = career.league.rivals[c].roster;
                if (roster == null) continue;
                for (int i = 0; i < roster.Count; i++)
                    if (roster[i] != null && roster[i].id == id)
                        return roster[i];
            }
            return null;
        }
    }

    /// <summary>
    /// Power-Pros-style player card. Left: a portrait frame (the character
    /// sprite sits in it until a dedicated portrait exists). Right: skills in
    /// sport groups, each with its base grade and its current grade. Bottom:
    /// attributes as on/off, with a LIVE mark when one is firing.
    ///
    /// On the court, Tab opens it and pauses play. The hub opens it itself.
    /// </summary>
    public class PlayerViewer : MonoBehaviour
    {
        [Tooltip("Court: Tab opens the card on the controlled player and pauses. Hub leaves this off and calls Show.")]
        public bool controlMatch;

        private PlayerCard card;
        private Sprite portrait;
        private GameObject focus;
        private bool pausedMatch;
        private float savedScale = 1f;

        private GUIStyle nameStyle;
        private GUIStyle detailStyle;
        private GUIStyle groupStyle;
        private GUIStyle skillNameStyle;
        private GUIStyle gradeStyle;
        private GUIStyle baseStyle;
        private GUIStyle attrStyle;
        private GUIStyle hintStyle;
        private Texture2D pixel;

        public bool IsOpen => card != null;

        public void Show(PlayerCard next, Sprite picture)
        {
            card = next;
            portrait = picture;
        }

        public void Close()
        {
            card = null;
            portrait = null;
            focus = null;
            if (pausedMatch)
            {
                Time.timeScale = savedScale;
                pausedMatch = false;
            }
        }

        private void OnDestroy()
        {
            if (pausedMatch) Time.timeScale = savedScale;
        }

        private void Update()
        {
            if (!controlMatch) return;
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.tabKey.wasPressedThisFrame)
            {
                if (IsOpen) Close();
                else OpenOn(ControlledOrFirst());
                return;
            }

            if (!IsOpen) return;

            if (kb.escapeKey.wasPressedThisFrame)
            {
                Close();
                return;
            }

            var pad = Gamepad.current;
            bool prev = kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame
                || (pad != null && pad.dpad.left.wasPressedThisFrame);
            bool next = kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame
                || (pad != null && pad.dpad.right.wasPressedThisFrame);
            if (prev) Cycle(-1);
            else if (next) Cycle(1);
        }

        private void OpenOn(GameObject player)
        {
            if (player == null) return;
            focus = player;
            if (!pausedMatch)
            {
                savedScale = Time.timeScale;
                Time.timeScale = 0f;
                pausedMatch = true;
            }
            var sprite = player.GetComponentInChildren<SpriteRenderer>();
            Show(PlayerCard.FromMatchPlayer(player), sprite != null ? sprite.sprite : null);
        }

        private void Cycle(int dir)
        {
            var all = PlayerZoneTracker.All;
            if (all == null || all.Count == 0) return;
            int index = 0;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].gameObject == focus) { index = i; break; }
            }
            for (int n = 0; n < all.Count; n++)
            {
                index = (index + dir + all.Count) % all.Count;
                if (all[index] != null && all[index].gameObject.activeInHierarchy)
                {
                    OpenOn(all[index].gameObject);
                    return;
                }
            }
        }

        private static GameObject ControlledOrFirst()
        {
            var current = DodgeballPlayerInput.Current;
            if (current != null) return current.gameObject;
            var all = PlayerZoneTracker.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].gameObject.activeInHierarchy)
                    return all[i].gameObject;
            return null;
        }

        private void OnGUI()
        {
            if (!IsOpen) return;
            EnsureStyles();

            float scale = Screen.height / 1080f;
            if (scale < 0.01f) scale = 1f;
            var previous = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            float sw = Screen.width / scale;
            float sh = Screen.height / scale;
            var panel = new Rect((sw - 1040f) * 0.5f, (sh - 640f) * 0.5f, 1040f, 640f);

            DrawRect(panel, new Color(0.05f, 0.09f, 0.16f, 0.96f));
            DrawRect(new Rect(panel.x, panel.y, panel.width, 8f), new Color(0.95f, 0.78f, 0.28f, 1f));

            var portraitRect = new Rect(panel.x + 36f, panel.y + 48f, 250f, 320f);
            DrawPortrait(portraitRect);

            var nameRect = new Rect(portraitRect.x, portraitRect.yMax + 16f, portraitRect.width, 36f);
            GUI.Label(nameRect, card.name, nameStyle);
            GUI.Label(new Rect(nameRect.x, nameRect.yMax, nameRect.width, 28f), card.detail, detailStyle);

            float right = panel.x + 320f;
            float top = panel.y + 36f;
            DrawGroup("GENERAL", card.general, new Rect(right, top, 680f, 200f));
            DrawGroup("DODGEBALL", card.dodgeball, new Rect(right, top + 210f, 680f, 200f));
            DrawAttributes(new Rect(panel.x + 36f, panel.y + 500f, panel.width - 72f, 80f));

            string hint = controlMatch
                ? "Tab / Esc close    Left / Right  next player"
                : "F / Esc close";
            GUI.Label(new Rect(panel.x, panel.yMax - 40f, panel.width, 28f), hint, hintStyle);

            GUI.matrix = previous;
        }

        private void DrawPortrait(Rect rect)
        {
            DrawRect(rect, new Color(0.10f, 0.14f, 0.22f, 1f));
            var inner = new Rect(rect.x + 4f, rect.y + 4f, rect.width - 8f, rect.height - 8f);
            DrawRect(inner, new Color(0.16f, 0.22f, 0.32f, 1f));

            if (portrait != null && portrait.texture != null)
            {
                Rect tr = portrait.textureRect;
                var coords = new Rect(
                    tr.x / portrait.texture.width,
                    tr.y / portrait.texture.height,
                    tr.width / portrait.texture.width,
                    tr.height / portrait.texture.height);
                var fit = Fit(inner, tr.width, tr.height);
                GUI.DrawTextureWithTexCoords(fit, portrait.texture, coords, true);
            }
            else
            {
                var mark = new Rect(inner.x + 40f, inner.y + 70f, inner.width - 80f, 120f);
                DrawRect(mark, new Color(0.22f, 0.38f, 0.62f, 1f));
                GUI.Label(mark, Initials(card.name), gradeStyle);
            }
        }

        private void DrawGroup(string title, List<SkillReadout> skills, Rect area)
        {
            GUI.Label(new Rect(area.x, area.y, area.width, 28f), title, groupStyle);
            if (skills == null || skills.Count == 0) return;
            float cell = area.width / skills.Count;
            for (int i = 0; i < skills.Count; i++)
            {
                var skill = skills[i];
                var cellRect = new Rect(area.x + cell * i, area.y + 36f, cell - 12f, 150f);
                DrawRect(cellRect, new Color(0.08f, 0.13f, 0.22f, 1f));
                GUI.Label(new Rect(cellRect.x, cellRect.y + 8f, cellRect.width, 24f), skill.name, skillNameStyle);

                string current = CoreRating.Grade(skill.currentValue);
                string basis = CoreRating.Grade(skill.baseValue);
                var previous = gradeStyle.normal.textColor;
                gradeStyle.normal.textColor = GradeColor(current);
                GUI.Label(new Rect(cellRect.x, cellRect.y + 36f, cellRect.width, 64f), current, gradeStyle);
                gradeStyle.normal.textColor = previous;

                string arrow = "";
                if (skill.currentValue > skill.baseValue + 0.05f) arrow = "  ^";
                else if (skill.currentValue < skill.baseValue - 0.05f) arrow = "  v";
                var prevBase = baseStyle.normal.textColor;
                baseStyle.normal.textColor = arrow.Length > 0 && skill.currentValue > skill.baseValue
                    ? new Color(0.45f, 0.9f, 0.5f)
                    : arrow.Length > 0
                        ? new Color(0.95f, 0.45f, 0.4f)
                        : new Color(0.65f, 0.7f, 0.78f);
                GUI.Label(new Rect(cellRect.x, cellRect.y + 108f, cellRect.width, 28f), "base " + basis + arrow, baseStyle);
                baseStyle.normal.textColor = prevBase;
            }
        }

        private void DrawAttributes(Rect area)
        {
            GUI.Label(new Rect(area.x, area.y, 200f, 24f), "ATTRIBUTES", groupStyle);
            if (card.attributes == null) return;
            float x = area.x;
            for (int i = 0; i < card.attributes.Count; i++)
            {
                var row = card.attributes[i];
                string text = row.on
                    ? (row.activeNow ? row.name + "   ON  LIVE" : row.name + "   ON")
                    : row.name + "   OFF";
                var chip = new Rect(x, area.y + 32f, 280f, 36f);
                DrawRect(chip, row.on
                    ? new Color(0.18f, 0.32f, 0.48f, 1f)
                    : new Color(0.10f, 0.12f, 0.16f, 1f));
                var prev = attrStyle.normal.textColor;
                attrStyle.normal.textColor = !row.on
                    ? new Color(0.45f, 0.48f, 0.54f)
                    : row.activeNow
                        ? new Color(0.5f, 0.95f, 0.55f)
                        : new Color(0.95f, 0.84f, 0.45f);
                GUI.Label(chip, text, attrStyle);
                attrStyle.normal.textColor = prev;
                x += 296f;
            }
        }

        private static Color GradeColor(string grade)
        {
            switch (grade)
            {
                case "S": return new Color(1f, 0.35f, 0.42f);
                case "A": return new Color(1f, 0.62f, 0.25f);
                case "B": return new Color(1f, 0.84f, 0.35f);
                case "C": return new Color(0.5f, 0.9f, 0.5f);
                case "D": return new Color(0.5f, 0.82f, 1f);
                case "E": return new Color(0.82f, 0.84f, 0.88f);
                default:  return new Color(0.55f, 0.58f, 0.62f);
            }
        }

        private static Rect Fit(Rect frame, float srcW, float srcH)
        {
            if (srcW < 1f || srcH < 1f) return frame;
            float scale = Mathf.Min(frame.width / srcW, frame.height / srcH);
            float w = srcW * scale;
            float h = srcH * scale;
            return new Rect(frame.x + (frame.width - w) * 0.5f, frame.y + (frame.height - h) * 0.5f, w, h);
        }

        private static string Initials(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            var parts = name.Split(' ');
            if (parts.Length == 1) return parts[0].Substring(0, 1).ToUpperInvariant();
            return (parts[0].Substring(0, 1) + parts[parts.Length - 1].Substring(0, 1)).ToUpperInvariant();
        }

        private void DrawRect(Rect rect, Color color)
        {
            var prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, pixel);
            GUI.color = prev;
        }

        private void EnsureStyles()
        {
            if (pixel == null)
            {
                pixel = new Texture2D(1, 1);
                pixel.SetPixel(0, 0, Color.white);
                pixel.Apply();
                pixel.hideFlags = HideFlags.HideAndDontSave;
            }
            if (nameStyle != null) return;

            nameStyle = Make(22, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            detailStyle = Make(16, FontStyle.Normal, TextAnchor.MiddleCenter, new Color(0.7f, 0.78f, 0.88f));
            groupStyle = Make(16, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.95f, 0.78f, 0.28f));
            skillNameStyle = Make(14, FontStyle.Normal, TextAnchor.MiddleCenter, new Color(0.75f, 0.8f, 0.88f));
            gradeStyle = Make(48, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            baseStyle = Make(15, FontStyle.Normal, TextAnchor.MiddleCenter, new Color(0.65f, 0.7f, 0.78f));
            attrStyle = Make(16, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            hintStyle = Make(15, FontStyle.Normal, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.55f));

            var font = DodgeballUI.Font;
            if (font == null) return;
            nameStyle.font = font;
            detailStyle.font = font;
            groupStyle.font = font;
            skillNameStyle.font = font;
            gradeStyle.font = font;
            baseStyle.font = font;
            attrStyle.font = font;
            hintStyle.font = font;
        }

        private static GUIStyle Make(int size, FontStyle style, TextAnchor anchor, Color color)
        {
            var gui = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = style,
                alignment = anchor,
            };
            gui.normal.textColor = color;
            return gui;
        }
    }
}
