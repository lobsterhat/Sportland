using System;
using System.Text;
using Sportland.Career;

namespace Sportland.Hub
{
    /// <summary>
    /// Text builders for the hub's full-screen panels (character creator,
    /// league sign-up, schedule calendar, roster & recruiting). Pure string
    /// building — input and state live in HubInteractor, display in HubHud.
    /// </summary>
    public static class HubScreens
    {
        private const string Dim = "<alpha=#66>";
        private const string Hint = "<alpha=#88>";

        // ── Character creator ───────────────────────────────────────────

        public static string Creator(int selected)
        {
            var all = Archetypes.All;
            var sb = new StringBuilder();
            sb.AppendLine("<b>CREATE YOUR CHARACTER</b> — choose an archetype");
            sb.AppendLine($"{Hint}The trade-off is the choice: playing yourself vs. running the club.</alpha>");
            sb.AppendLine();

            for (int i = 0; i < all.Length; i++)
            {
                var a = all[i];
                if (i == selected)
                    sb.AppendLine($"<color=#FFD75F>>  {a.displayName}</color>   <alpha=#AA>Playing {a.playingGrade} · Management {a.managementGrade} · {a.actionsPerDay} actions/day</alpha>");
                else
                    sb.AppendLine($"{Dim}   {a.displayName}</alpha>");
            }

            var sel = all[selected];
            sb.AppendLine();
            sb.AppendLine($"<b>{sel.displayName}</b> — <i>{sel.fantasy}</i>");
            sb.AppendLine($"Perk: <b>{sel.perkName}</b> — {sel.perkDescription}");
            sb.AppendLine($"The catch: {sel.theCatch}");
            sb.AppendLine();
            sb.AppendLine($"<color=#7FDBFF>Skip:</color> {sel.skipLine}");
            sb.AppendLine();
            sb.AppendLine($"{Hint}W/S or D-Pad: select    E/Cross: confirm    Esc/Circle: decide later</alpha>");
            return sb.ToString();
        }

        // ── League sign-up ──────────────────────────────────────────────

        public static string LeagueSignup(CareerManager career)
        {
            var rules = new RosterRules(); // dodgeball's requirements, shown before committing
            var sb = new StringBuilder();
            sb.AppendLine("<b>LEAGUE SIGN-UP</b>");
            sb.AppendLine();
            sb.AppendLine("<color=#FFD75F>>  Dodgeball — Parks League, Division 4</color>");
            sb.AppendLine($"{Dim}   Basketball — locked (arrives with its season)</alpha>");
            sb.AppendLine($"{Dim}   More sports join the calendar as Sportland grows</alpha>");
            sb.AppendLine();
            sb.AppendLine("<b>Roster requirements</b>");
            sb.AppendLine($"  On the court: {rules.courtSize}");
            sb.AppendLine($"  Reserves dressing: {rules.reserveSize}   (match-day squad: {rules.DressedSize})");
            sb.AppendLine($"  Inactive allowed (healthy scratches): {rules.inactiveMax}");
            sb.AppendLine($"  <b>Max roster: {rules.MaxRoster}</b>");
            sb.AppendLine();
            sb.AppendLine("<b>Game schedule</b>");
            sb.AppendLine("  Season: 14 games (each rival twice), one game every 3 days,");
            sb.AppendLine("  starting one week from today. Every game has a time slot —");
            sb.AppendLine("  Morning / Afternoon / Evening / Night — shown on the Arena calendar.");
            sb.AppendLine("  Slot clashes with other sports, and players of yours booked by");
            sb.AppendLine("  another team, get flagged on the calendar. <i>(You're joining your");
            sb.AppendLine("  first league — no conflicts possible yet.)</i>");
            sb.AppendLine();
            sb.AppendLine($"{Hint}After joining: fill your roster from the player pool at the Office.</alpha>");
            sb.AppendLine();
            sb.AppendLine($"{Hint}E/Cross: join the Parks League    Esc/Circle: not yet</alpha>");
            return sb.ToString();
        }

        // ── Schedule calendar ───────────────────────────────────────────

        /// <summary>Months spanned by the fixture list (for page clamping).</summary>
        public static int CalendarMonthCount(LeagueMembership league)
        {
            if (league == null || league.fixtures.Count == 0) return 1;
            DateTime first = league.fixtures[0].date;
            DateTime last = league.fixtures[league.fixtures.Count - 1].date;
            return (last.Year * 12 + last.Month) - (first.Year * 12 + first.Month) + 1;
        }

        public static string Calendar(CareerManager career, int page)
        {
            var league = career.league;
            DateTime anchor = league.fixtures[0].date;
            var month = new DateTime(anchor.Year, anchor.Month, 1).AddMonths(page);
            var sb = new StringBuilder();

            sb.AppendLine($"<b>SCHEDULE</b> — {league.leagueName}, {league.divisionName} ({league.sport})");
            sb.AppendLine();
            sb.AppendLine($"<b>{month:MMMM yyyy}</b>   {Hint}({page + 1}/{CalendarMonthCount(league)})</alpha>");
            sb.AppendLine();

            // Month grid: Monday-first, game days gold, today cyan.
            sb.Append("<mspace=0.62em>");
            sb.AppendLine("Mo Tu We Th Fr Sa Su");
            int lead = ((int)month.DayOfWeek + 6) % 7;
            int daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);
            int cell = 0;
            for (int i = 0; i < lead; i++) { sb.Append("   "); cell++; }
            for (int d = 1; d <= daysInMonth; d++)
            {
                var date = new DateTime(month.Year, month.Month, d);
                bool game = HasFixture(league, date);
                bool today = date.Date == career.currentDate.Date;

                string num = d.ToString().PadLeft(2);
                if (today) sb.Append($"<color=#7FDBFF>{num}</color> ");
                else if (game) sb.Append($"<color=#FFD75F>{num}</color> ");
                else sb.Append($"{num} ");

                if (++cell % 7 == 0) sb.AppendLine();
            }
            sb.AppendLine("</mspace>");

            // The month's fixtures, with the conflict seam wired in.
            sb.AppendLine();
            bool any = false;
            foreach (var f in league.fixtures)
            {
                if (f.date.Year != month.Year || f.date.Month != month.Month) continue;
                any = true;
                string venue = f.home ? "vs" : "at";
                string line = $"  {f.date:MMM d (ddd)} — {venue} {f.opponent} — {f.slot}";
                var conflicts = career.ConflictsWith(f);
                if (conflicts.Count > 0)
                    line += "   <color=#FF6B6B>! conflict</color>";
                sb.AppendLine(line);
            }
            if (!any) sb.AppendLine($"{Dim}  No games this month.</alpha>");

            sb.AppendLine();
            sb.AppendLine($"{Hint}<color=#7FDBFF>today</color> · <color=#FFD75F>game day</color>    A/D or D-Pad ◄ ►: month    Esc/Circle: close</alpha>");
            return sb.ToString();
        }

        private static bool HasFixture(LeagueMembership league, DateTime date)
        {
            foreach (var f in league.fixtures)
                if (f.date.Date == date.Date) return true;
            return false;
        }

        // ── Roster & recruiting ─────────────────────────────────────────

        public static string Roster(CareerManager career, bool poolTab, int selectedIndex)
        {
            var rules = career.league.rules;
            int squad = career.club.pool.Count;
            var sb = new StringBuilder();

            sb.AppendLine($"<b>ROSTER & RECRUITING</b> — {career.club.clubName}");
            sb.AppendLine($"Squad: <b>{squad}/{rules.MaxRoster}</b>   " +
                          $"Match-day squad needs {rules.DressedSize} ({rules.courtSize} court + {rules.reserveSize} reserve), " +
                          $"up to {rules.inactiveMax} inactive.");
            sb.AppendLine(squad >= rules.DressedSize
                ? "<color=#7FE87F>Full match-day squad — ready for the season.</color>"
                : squad >= rules.courtSize
                    ? $"<color=#FFD75F>Enough to field a team ({rules.courtSize}), but short of a full match-day squad ({rules.DressedSize}).</color>"
                    : $"<color=#FF6B6B>Not enough players to field a team — need at least {rules.courtSize}.</color>");
            sb.AppendLine();

            sb.AppendLine(poolTab
                ? $"{Dim}[ Current Roster ]</alpha>  <color=#FFD75F>[ Player Pool ({career.freeAgents.Count}) ]</color>"
                : $"<color=#FFD75F>[ Current Roster ]</color>  {Dim}[ Player Pool ({career.freeAgents.Count}) ]</alpha>");
            sb.AppendLine();

            if (!poolTab)
            {
                foreach (var a in career.club.pool)
                    sb.AppendLine("  " + AthleteLine(a));
                sb.AppendLine();
                sb.AppendLine($"{Hint}R/Triangle: player pool    Esc/Circle: close</alpha>");
            }
            else
            {
                if (career.freeAgents.Count == 0)
                {
                    sb.AppendLine($"{Dim}  The pool is empty — everyone signable has been signed.</alpha>");
                }
                else
                {
                    for (int i = 0; i < career.freeAgents.Count; i++)
                    {
                        var a = career.freeAgents[i];
                        sb.AppendLine(i == selectedIndex
                            ? $"<color=#FFD75F>> {AthleteLine(a)}</color>"
                            : $"{Dim}  {AthleteLine(a)}</alpha>");
                    }
                }
                sb.AppendLine();
                sb.AppendLine($"{Hint}Bottom-division pool — modest talent, and everyone accepts (for now; interviews come later).</alpha>");
                sb.AppendLine($"{Hint}W/S: select    E/Cross: sign    R/Triangle: current roster    Esc/Circle: close</alpha>");
            }

            return sb.ToString();
        }

        private static string AthleteLine(CareerAthlete a)
        {
            string archetypeName = a.isPlayerCharacter ? Archetypes.NameOf(a.archetypeId) : "";
            string tag = a.isPlayerCharacter
                ? (archetypeName.Length > 0 ? $" <color=#7FDBFF>(you — {archetypeName})</color>" : " <color=#7FDBFF>(you)</color>")
                : a.isMentor ? " <color=#7FDBFF>(mentor)</color>" : "";

            return $"{a.FullName,-18}{tag}  {a.age,2}   " +
                   $"SPD {a.GetGeneral(GeneralRating.Speed).DisplayGrade} · " +
                   $"AGI {a.GetGeneral(GeneralRating.Agility).DisplayGrade} · " +
                   $"END {a.GetGeneral(GeneralRating.Endurance).DisplayGrade} · " +
                   $"TGH {a.GetGeneral(GeneralRating.Toughness).DisplayGrade}";
        }
    }
}
