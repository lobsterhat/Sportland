using System;
using System.Text;
using Sportland.Career;

namespace Sportland.Hub
{
    /// <summary>Which tab of the Office roster screen is active.</summary>
    public enum RosterTab
    {
        Squad,
        Pool,
        Lineup,
    }

    /// <summary>
    /// Text builders for the hub's full-screen panels (character creator,
    /// league sign-up, schedule calendar, roster & recruiting, lineup). Pure
    /// string building — input and state live in HubInteractor, display in
    /// HubHud.
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
            sb.AppendLine($"  On the court: {rules.courtSize}   ({rules.courtSize / 2} infielders + {rules.courtSize / 2} outfielders)");
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
            DateTime first = league.fixtures[0].Date;
            DateTime last = league.fixtures[league.fixtures.Count - 1].Date;
            return (last.Year * 12 + last.Month) - (first.Year * 12 + first.Month) + 1;
        }

        public static string Calendar(CareerManager career, int page)
        {
            var league = career.league;
            DateTime anchor = league.fixtures[0].Date;
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
                if (f.Date.Year != month.Year || f.Date.Month != month.Month) continue;
                any = true;
                string venue = f.home ? "vs" : "at";
                string line = $"  {f.Date:MMM d (ddd)} — {venue} {f.opponent} — {f.slot}";
                if (f.played)
                    line += f.Won
                        ? $"   <color=#7FE87F>W {f.ourScore}-{f.theirScore}</color>"
                        : $"   <color=#FF6B6B>L {f.ourScore}-{f.theirScore}</color>";
                var conflicts = career.ConflictsWith(f);
                if (conflicts.Count > 0)
                    line += "   <color=#FF6B6B>! conflict</color>";
                sb.AppendLine(line);
            }
            if (!any) sb.AppendLine($"{Dim}  No games this month.</alpha>");

            sb.AppendLine();
            sb.AppendLine($"{Hint}<color=#7FDBFF>today</color> · <color=#FFD75F>game day</color>    A/D or D-Pad ◄ ►: month    R/Triangle: division    Esc/Circle: close</alpha>");
            return sb.ToString();
        }

        private static bool HasFixture(LeagueMembership league, DateTime date)
        {
            foreach (var f in league.fixtures)
                if (f.Date.Date == date.Date) return true;
            return false;
        }

        // ── Game day ────────────────────────────────────────────────────

        public static string PreGame(CareerManager career)
        {
            var f = career.TodayFixture;
            var league = career.league;
            var sb = new StringBuilder();

            sb.AppendLine($"<b>GAME DAY</b> — {league.leagueName}, {league.divisionName}");
            sb.AppendLine($"{(f.home ? "vs" : "at")} <b>{f.opponent}</b> — {f.slot}{(f.home ? "   (home court)" : "")}");
            sb.AppendLine();

            var starters = new System.Collections.Generic.List<CareerAthlete>();
            var bench = new System.Collections.Generic.List<CareerAthlete>();
            career.RivalMatchSquad(f.opponent, starters, bench);

            sb.AppendLine($"<b>Their starting lineup</b>");
            for (int i = 0; i < starters.Count; i++)
            {
                string pos = i < 3 ? "IN" : "OUT";
                sb.AppendLine($"  {Dim}{pos,-4}</alpha>{AthleteLine(starters[i])}");
            }
            sb.AppendLine();
            sb.AppendLine("<b>Their bench</b>");
            foreach (var a in bench)
                sb.AppendLine($"  {Dim}RES </alpha>{AthleteLine(a)}");
            sb.AppendLine();

            sb.AppendLine($"<b>Your squad</b>: {league.StartersFilled}/6 starters, {league.ReservesFilled}/4 reserves set" +
                          (league.StartersFilled < 6 ? "   <color=#FF6B6B>— thin! empty slots play as dead weight</color>" : ""));
            sb.AppendLine();
            sb.AppendLine($"{Hint}The match is simulated for now — the playable dodgeball bridge is next on the list.</alpha>");
            sb.AppendLine($"{Hint}E/Cross: play the game    Esc/Circle: not yet (the day can't end until you do)</alpha>");
            return sb.ToString();
        }

        public static string MatchResult(CareerManager career, Fixture f)
        {
            var sb = new StringBuilder();
            bool won = f.Won;
            sb.AppendLine($"<b>FULL TIME</b> — {(f.home ? "vs" : "at")} {f.opponent} ({f.slot})");
            sb.AppendLine();
            sb.AppendLine(won
                ? $"<size=140%><color=#7FE87F>WIN  {f.ourScore} — {f.theirScore}</color></size>"
                : $"<size=140%><color=#FF6B6B>LOSS  {f.ourScore} — {f.theirScore}</color></size>");
            sb.AppendLine();
            sb.AppendLine($"Record: <b>{career.RecordString}</b>");
            sb.AppendLine();
            sb.AppendLine(won
                ? "<color=#7FDBFF>Skip:</color> \"That's how it's done! Get some rest — recovery is tomorrow's problem, and also tomorrow's solution.\""
                : "<color=#7FDBFF>Skip:</color> \"We'll take our lumps and learn. Check who's gassed before next practice.\"");
            sb.AppendLine();
            sb.AppendLine($"{Hint}The match-day squad picked up fatigue. E/Cross or Esc/Circle: back to the hub</alpha>");
            return sb.ToString();
        }

        // ── Division (rival clubs & their rosters) ──────────────────────

        public static string Division(CareerManager career, int selectedIndex)
        {
            var league = career.league;
            var sb = new StringBuilder();
            sb.AppendLine($"<b>DIVISION</b> — {league.leagueName}, {league.divisionName} ({league.sport})");
            sb.AppendLine();
            sb.AppendLine($"  {career.club.clubName}   <color=#7FDBFF>(you)</color>   {Dim}squad {career.club.pool.Count}</alpha>");
            sb.AppendLine();

            for (int i = 0; i < league.rivals.Count; i++)
            {
                var rival = league.rivals[i];
                var captain = rival.Captain;
                string captainNote = captain != null
                    ? $"{Dim}capt. {captain.FullName} ({Archetypes.NameOf(captain.archetypeId)}) · squad {rival.roster.Count}</alpha>"
                    : $"{Dim}squad {rival.roster.Count}</alpha>";

                sb.AppendLine(i == selectedIndex
                    ? $"<color=#FFD75F>> {rival.clubName,-18}</color>   {captainNote}"
                    : $"{Dim}  {rival.clubName,-18}</alpha>   {captainNote}");
            }

            sb.AppendLine();
            sb.AppendLine($"{Hint}Every rival player — captains included — can be poached once the season ends.</alpha>");
            sb.AppendLine($"{Hint}W/S: select club    E/Cross: view roster    R/Triangle: schedule    Esc/Circle: close</alpha>");
            return sb.ToString();
        }

        public static string RivalRoster(CareerManager career, int clubIndex)
        {
            var rival = career.league.rivals[clubIndex];
            var sb = new StringBuilder();
            sb.AppendLine($"<b>{rival.clubName.ToUpperInvariant()}</b> — {career.league.divisionName}, squad of {rival.roster.Count}");
            sb.AppendLine();

            var captain = rival.Captain;
            if (captain != null)
                sb.AppendLine("  " + AthleteLine(captain));
            foreach (var a in rival.roster)
                if (!a.isCaptain)
                    sb.AppendLine("  " + AthleteLine(a));

            sb.AppendLine();
            sb.AppendLine($"{Hint}The captain is their manager-player — same parts as you, different choices.</alpha>");
            sb.AppendLine($"{Hint}Poaching opens in the offseason. Esc/Circle: back to division</alpha>");
            return sb.ToString();
        }

        // ── Roster, recruiting & lineup ─────────────────────────────────

        private static string Header(CareerManager career)
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
            return sb.ToString();
        }

        private static string Tabs(CareerManager career, RosterTab active)
        {
            string squadTab = $"[ Squad ({career.club.pool.Count}) ]";
            string poolTab = $"[ Player Pool ({career.freeAgents.Count}) ]";
            string lineupTab = $"[ Lineup {career.league.StartersFilled}/6 · {career.league.ReservesFilled}/4 ]";

            string T(string label, bool on) => on ? $"<color=#FFD75F>{label}</color>" : $"{Dim}{label}</alpha>";
            return $"{T(squadTab, active == RosterTab.Squad)}  {T(poolTab, active == RosterTab.Pool)}  {T(lineupTab, active == RosterTab.Lineup)}\n";
        }

        public static string Roster(CareerManager career, RosterTab tab, int selectedIndex)
        {
            var sb = new StringBuilder();
            sb.Append(Header(career));
            sb.AppendLine(Tabs(career, tab));

            if (tab == RosterTab.Squad)
            {
                foreach (var a in career.club.pool)
                {
                    string lineupTag = career.league.LineupTagOf(a.id);
                    string suffix = lineupTag != null
                        ? $"   <color=#7FE87F>[{lineupTag}]</color>"
                        : $"   {Dim}[inactive]</alpha>";
                    sb.AppendLine("  " + AthleteLine(a) + suffix);
                }
                sb.AppendLine();
                sb.AppendLine($"{Hint}R/Triangle: next tab    Esc/Circle: close</alpha>");
            }
            else // Pool
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
                sb.AppendLine($"{Hint}W/S: select    E/Cross: sign    R/Triangle: next tab    Esc/Circle: close</alpha>");
            }

            return sb.ToString();
        }

        /// <summary>
        /// The lineup builder: 6 starter slots with positions (3 infield, 3
        /// outfield), 4 reserve slots, everyone else inactive. In assign mode
        /// the lower half becomes a candidate picker for the selected slot.
        /// </summary>
        public static string Lineup(CareerManager career, int slotIndex, bool assignMode, int pickIndex)
        {
            var league = career.league;
            var sb = new StringBuilder();
            sb.Append(Header(career));
            sb.AppendLine(Tabs(career, RosterTab.Lineup));

            // Slots: 0-5 starters, 6-9 reserves.
            for (int i = 0; i < 6; i++)
                sb.AppendLine(SlotLine(career, LeagueMembership.StarterSlotLabel(i),
                    LeagueMembership.StarterPosition(i).ToString(), league.starterIds[i],
                    selected: !assignMode && slotIndex == i));
            sb.AppendLine();
            for (int i = 0; i < 4; i++)
                sb.AppendLine(SlotLine(career, $"R{i + 1}", "Reserve", league.reserveIds[i],
                    selected: !assignMode && slotIndex == 6 + i));

            // Inactive: everyone in the pool without a slot.
            sb.AppendLine();
            var inactive = new StringBuilder();
            foreach (var a in career.club.pool)
                if (league.LineupTagOf(a.id) == null)
                    inactive.Append(inactive.Length > 0 ? ", " : "").Append(a.FullName);
            sb.AppendLine($"{Dim}Inactive: {(inactive.Length > 0 ? inactive.ToString() : "none")}</alpha>");
            sb.AppendLine();

            if (assignMode)
            {
                string slotLabel = slotIndex < 6 ? LeagueMembership.StarterSlotLabel(slotIndex) : $"R{slotIndex - 5}";
                sb.AppendLine($"<b>Assign to {slotLabel}:</b>");
                sb.AppendLine(pickIndex == 0
                    ? "<color=#FFD75F>>  — clear slot —</color>"
                    : $"{Dim}   — clear slot —</alpha>");
                for (int i = 0; i < career.club.pool.Count; i++)
                {
                    var a = career.club.pool[i];
                    string tag = league.LineupTagOf(a.id);
                    string current = tag != null ? $"  {Dim}[{tag}]</alpha>" : "";
                    sb.AppendLine(pickIndex == i + 1
                        ? $"<color=#FFD75F>>  {AthleteLine(a)}</color>{current}"
                        : $"{Dim}   {AthleteLine(a)}</alpha>{current}");
                }
                sb.AppendLine();
                sb.AppendLine($"{Hint}W/S: choose    E/Cross: confirm    Esc/Circle: back</alpha>");
            }
            else
            {
                sb.AppendLine($"{Hint}W/S: slot    E/Cross: assign    F/Square: auto-fill    R/Triangle: next tab    Esc/Circle: close</alpha>");
            }

            return sb.ToString();
        }

        private static string SlotLine(CareerManager career, string label, string position, string athleteId, bool selected)
        {
            var athlete = string.IsNullOrEmpty(athleteId) ? null : career.AthleteById(athleteId);
            string body = athlete != null ? AthleteLine(athlete) : $"{Dim}— empty —</alpha>";
            string line = $"{label,-5} {Dim}{position,-10}</alpha> {body}";
            return selected ? $"<color=#FFD75F>> {line}</color>" : $"  {line}";
        }

        private static string AthleteLine(CareerAthlete a)
        {
            string archetypeName = Archetypes.NameOf(a.archetypeId);
            string tag = a.isPlayerCharacter
                ? (archetypeName.Length > 0 ? $" <color=#7FDBFF>(you — {archetypeName})</color>" : " <color=#7FDBFF>(you)</color>")
                : a.isMentor ? " <color=#7FDBFF>(mentor)</color>"
                : a.isCaptain ? $" <color=#FFD75F>(captain — {archetypeName})</color>" : "";

            return $"{a.FullName,-18}{tag}  {a.age,2}   " +
                   $"SPD {a.GetGeneral(GeneralRating.Speed).DisplayGrade} · " +
                   $"AGI {a.GetGeneral(GeneralRating.Agility).DisplayGrade} · " +
                   $"END {a.GetGeneral(GeneralRating.Endurance).DisplayGrade} · " +
                   $"TGH {a.GetGeneral(GeneralRating.Toughness).DisplayGrade}";
        }
    }
}
