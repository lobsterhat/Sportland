using UnityEngine;
using Sportland.Core;

namespace Sportland.Career
{
    /// <summary>
    /// Where an athlete is on the growth arc. Age sets it
    /// (design/athlete_development.md §2): under 24 growth, through 30 peak,
    /// 31 and older decline.
    /// </summary>
    public enum DevelopmentPhase
    {
        Growth,
        Peak,
        Decline,
    }

    /// <summary>
    /// Dodgeball skills stored on the career athlete. Order is the array index
    /// on <see cref="CareerAthlete.dodgeball"/>.
    /// </summary>
    public enum DodgeballSkill
    {
        ThrowPower = 0,
        ThrowTechnique = 1,
        CatchTechnique = 2,
        Anticipation = 3,
    }

    /// <summary>How many <see cref="DodgeballSkill"/> values the sheet holds.</summary>
    public static class DodgeballSkills
    {
        public const int Count = 4;
    }

    /// <summary>
    /// On/off special abilities fixed to the athlete. A flag is present or it
    /// isn't — no per-match loadout (design/special_abilities.md).
    /// </summary>
    [System.Flags]
    public enum AthleteAbility
    {
        None = 0,
        HotHead = 1 << 0,
        SoleSurvivor = 1 << 1,
    }

    /// <summary>
    /// One skill on the 0–20 scale. <see cref="current"/> is the base the match
    /// reads. <see cref="floor"/> is where regression stops. <see cref="ceiling"/>
    /// is where progression stops. The scale itself is still 0–20; these three
    /// are the athlete's band inside it. Players see <see cref="Grade"/> (F–S),
    /// never the raw numbers. Ceilings stay hidden until scouting exists.
    /// </summary>
    [System.Serializable]
    public struct SkillRating
    {
        public float current;
        public float floor;
        public float ceiling;

        /// <summary>Player-facing grade of the base the match reads.</summary>
        public string Grade => Rating.Grade(current);

        public static SkillRating Make(float current, float floor, float ceiling)
        {
            var skill = new SkillRating
            {
                current = current,
                floor = floor,
                ceiling = ceiling,
            };
            skill.Clamp();
            return skill;
        }

        /// <summary>Force floor ≤ current ≤ ceiling, each inside 0–20.</summary>
        public void Clamp()
        {
            floor = Mathf.Clamp(floor, 0f, Rating.Max);
            ceiling = Mathf.Clamp(ceiling, 0f, Rating.Max);
            if (ceiling < floor) ceiling = floor;
            current = Mathf.Clamp(current, floor, ceiling);
        }

        /// <summary>
        /// One development tick. Growth steps <see cref="current"/> toward the
        /// ceiling. Decline steps it toward the floor. Peak holds. An F current
        /// still plays — the step never removes the skill, it only moves the base.
        /// </summary>
        public void Develop(DevelopmentPhase phase, float step)
        {
            if (step < 0f) step = 0f;
            if (phase == DevelopmentPhase.Growth)
                current = Mathf.Min(ceiling, current + step);
            else if (phase == DevelopmentPhase.Decline)
                current = Mathf.Max(floor, current - step);
            Clamp();
        }
    }
}
