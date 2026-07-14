# Sportland — Calendar & League Structure

**Status:** Living design document
**Last updated:** 2026-07-14
**Scope:** The Sportland year — how each sport's league is structured into divisions and seasons, how seasons fit together into a multi-sport calendar, and what season boundaries mean for the rest of the game.

> Part of the `design/` canvas. Companion to `hub_actions.md` (days as currency — this doc defines the year those days live in), `hub_world.md` (the "join a league" intro step), and `conflict_chemistry.md` (season boundaries as ego checkpoints); code mapping in **Code alignment** below.

---

## 1. Purpose

The calendar is the game's clock: hub actions spend days, and this doc defines what the days add up to. Leagues are what make scheduled games *mean* something — every fixture is a rung on a ladder the player is trying to climb.

## 2. League Structure — Divisions per Sport

Each sport runs its own league, structured as **3–4 tiered divisions** *(final count open — see Section 7)*:

```
Division 1  — the summit; its champion is the sport's champion
Division 2
Division 3
(Division 4 — entry tier, if we go with four)
```

- **Each division holds at least 8 teams** (confirmed floor; exact count per division open) playing a scheduled season against each other (placeholder: double round-robin — 14+ game days). Populating this many teams across every tier requires a big, living player pool — see `athlete_development.md`.
- **Promotion & relegation.** Season's end moves teams between tiers (placeholder: top 2 up, bottom 2 down). The player's team can absolutely be relegated — the ladder goes both directions.
- **New teams enter at the bottom.** The intro's "join a league" step (see `hub_world.md` §3) enrolls the player's fresh team in the lowest division of their chosen sport.
- **The crown.** Winning Division 1 makes you that sport's champion. The long-game fantasy — the reason the hub has more than one arena — is taking one club to the top of *every* sport in Sportland.

AI teams occupy every other slot in every division, each run by its own **manager-player** — a rival built from the same archetype/skills/personality parts as the player's character. Full design in `rival_managers.md`.

## 3. Anatomy of a Season

Each sport's season passes through four phases:

1. **Preseason** — no league fixtures; the natural window for recruiting pushes, training camps, and friendlies. The fixture list for the coming season is published here.
2. **Regular season** — scheduled game days against division rivals, with free days between them (the hub economy's rhythm: 2–4 free days per fixture, placeholder). Standings update game by game.
3. **Postseason** — the season's sharp end (placeholder: a small playoff among the division's top finishers to crown the division champion; whether promotion is decided by standings alone or a promotion playoff is open).
4. **Offseason** — the league sleeps. Transfers and roster building happen here, and it's the ego system's annual checkpoint: expectations get renegotiated (a breakout season inflates a Playing Time ego; a veteran mellows), contentment resets toward neutral, and every athlete runs a stay/go evaluation — chronically frustrated players jump ship unless retained (`conflict_chemistry.md` §6.1).

Season results feed reputation: promotion raises the willingness of free agents to sign (everyone wants to join a riser); relegation sours it and can spark departure conflicts.

## 4. The Multi-Sport Year

Sportland's year is a wheel of overlapping seasons, roughly one sport per "season" of the year with deliberate overlap at the edges:

```
        ┌──────────── Sportland Year ────────────┐
Sport A ██████████░░
Sport B        ░░██████████░░
Sport C                  ░░██████████░░
Sport D                            ░░██████████
        (█ regular season   ░ pre/postseason overlap)
```

- **One club, many sports.** The player's franchise can field teams in multiple sports' leagues simultaneously. Each sport's league membership, division standing, and promotion track is independent.
- **Overlap is the designed crunch.** During overlap windows, two sports' game days compete for the same calendar — the "holding two jobs" pressure from `hub_actions.md` §5 is created *here*, on purpose, at the edges of seasons rather than constantly.
- **Multi-sport athletes are the tempting mistake.** Athletes carry per-sport stats, so your basketball star may also be a gifted volleyball player. Rostering someone in two overlapping sports doubles their value and their fatigue — and their Playing Time ego applies in *both* sports. The ego budget and the fatigue system make dual-rostering a high-wire act rather than a free win. (Aptitude profiles, specialization, and the club-pool-vs-rosters model live in `athlete_development.md`.)
- **The off-window is never empty.** When one sport sleeps, another is peaking — a full-franchise career has no true dead time, but a single-sport club gets genuine offseasons to rebuild.

## 5. Scheduling Mechanics

- **Fixture generation at season start.** The full slate of game days (dates, opponents, venues) is generated in preseason and visible immediately — the player always plans against a known calendar.
- **The Office calendar is the planning surface.** One view showing every enrolled sport's fixtures, practice bookings, known events, and today — the tool for spending free days well.
- **Friendlies** (from the Office's league business) can be booked into free days: practice-game benefits, real fatigue, no standings stakes.
- **The overnight tick owns transitions.** Day advancement (from `hub_actions.md` §2) also processes season-phase boundaries: publishing fixtures, closing seasons, running promotion/relegation, opening offseasons.

## 6. System Dependencies

1. **A calendar system** — dates, per-sport season windows and phases, fixture lists, and phase-transition processing on the overnight tick.
2. **League data** — divisions, member teams, standings, results history, promotion/relegation rules; per sport.
3. **AI teams** — persistent opponent clubs with rosters, occupying all non-player league slots, carried across seasons.
4. **Standings & results tracking** — game results (already produced by sport modules) feeding league tables.
5. **Season-boundary hooks** — the offseason ego renegotiation, willingness/reputation adjustments, and roster-turnover events all key off phase transitions.
6. **Multi-roster support** — one athlete rosterable in multiple sports for the same club, with shared fatigue and per-sport ego evaluation.

## 7. Open Questions

- **Three or four divisions?** Four gives a longer climb and a gentler entry tier; three keeps every season closer to the summit. May vary per sport (a niche sport might only sustain three).
- **Tiers confirmed?** This doc assumes divisions are a promotion/relegation ladder. The alternative reading — parallel/regional divisions feeding a shared playoff — is a different game shape; confirming the ladder is the first ruling this doc needs.
- **Teams per division & season length.** At least 8 teams is confirmed; the exact count, double round-robin, and 2–4 free days per fixture remain placeholders — these numbers *are* the game's pacing and want playtesting.
- **Postseason shape.** Standings-only promotion vs. promotion playoffs; whether D1 gets a grander championship event than lower tiers.
- **Sport count & the year wheel.** Which sports occupy which windows, how many the wheel holds at launch, and how much overlap is fun vs. punishing.
- **Entry choice.** Does a new career enroll in one sport's league (expanding later), or can an ambitious player enroll in several from day one?
- **Season failure softening.** Any mercy mechanics for a brutal first season (re-entry drafts, wildcard promotion), or is relegation's sting the point?
- **Cup competitions.** A cross-division knockout cup (giant-killing runs, extra fixtures as opt-in pressure) — flavorful, deferrable.

## Code Alignment (2026-07)

- **Seeds only**: `CoreGameManager` holds `currentDate`/`currentDay`; no calendar, league, or fixture system exists.
- **League/division/season configs should be data assets**, following the dodgeball `GameMode` preset pattern (per-sport match rules as ScriptableObjects).
- **Standings have a feed ready**: `ISportModule.GameResult` already returns scores/stats per game — league tables consume what sport modules already emit.
- **Match structure per sport** is an open question `game_flow.md` owns; this doc owns the year around the matches.
