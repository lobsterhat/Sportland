# Sportland — Character Management & the World Player Pool

**Status:** Living design document (new plan)
**Last updated:** 2026-09-06
**Scope:** How Sportland staffs *every* team at *every* level of play — one shared athlete pool, three levels per sport, twelve clubs per level, and the rule that athletes can play anywhere but generally will not drop below the level their overall skill belongs in.

> Separate from `athlete_development.md` (one club's growth, ceilings, familiarity) and `character_creator.md` (your own character). This doc is the **world**: who exists, where they belong, and how we fill 36 rosters without hand-authoring them.
>
> Companion to `calendar_league.md` (the ladder these people play on), `rival_managers.md` (who runs the other 35 clubs), and `scouting.md` (how you learn who's in the pool).

---

## 1. Purpose

A career in Sportland is not twelve named starters and a fog of "AI team." Every fixture is against a real club with a real roster drawn from a **persistent world pool**. That pool has to survive this shape:

```
3 levels × 12 teams per level  =  36 teams to roster, per sport
```

Players (athletes) are not locked to a division. They **can** play at any level. They **generally will not** play in a *lesser* level than their overall skill rating for that sport. The pool, the AI clubs, and willingness all enforce that as the weather — not as a hard engine lock.

This is the plan for making 36 rosters a living ecology instead of a generation cliff.

## 2. Confirmed shape (this plan)

These numbers were left open in `calendar_league.md` §7. This plan pins them so we can count heads.

| Decision | Value | Why |
|---|---|---|
| Levels per sport | **3** | Long enough climb, short enough that every season still feels near something. |
| Teams per level | **12** | A real season (22-game single RR, or 22 of a double) without a 16-team slog. |
| Teams per sport | **36** | 12 × 3. Each is a distinct club, not a reserve side of the same twelve brands. |
| Ladder | **Promotion / relegation of clubs** | End of season, clubs move between levels. Athletes decide whether to stay. |
| Pool | **One world pool per save** | Shared across sports. An athlete has one body, many sport ratings. |

Level names (working):

| Level | Working name | Who belongs here |
|---|---|---|
| 1 | Summit | The sport's top flight. Its champion is the sport's champion. |
| 2 | City | The working middle. Promotion is real; relegation hurts. |
| 3 | Parks | Entry. New clubs start here. High churn, still real games. |

The player's franchise occupies **one club slot per enrolled sport**, starting in Parks of the sport they join. Other slots are AI clubs with manager-players (`rival_managers.md`).

**Not this plan:** twelve city brands each fielding a first team / reserves / academy at all three levels. That is a different game (one org, three squads). Here the 36 teams are 36 clubs. A star on a Parks club is a mismatch, not a reserve listing.

## 3. What an athlete is (in the pool)

Every person in the pool is one `CareerAthlete`-shaped record:

- Identity (name, age, look)
- General ratings (the cross-sport body)
- Per-sport ratings + hidden per-sport ceilings (`athlete_development.md` §3)
- A derived **sport overall** and **natural level** (Section 4)
- Personality card (expectations, dispositions, volatility) — empty if ego-immune
- Club membership (or free agent), and **per-sport roster assignment** (or none)
- Career phase (growth / peak / decline)

The player character and Skip live in this same pool. They are not a parallel type.

Sports never special-case anyone. Match code reads ratings. The pool decides *who is allowed to be there*.

## 4. Overall rating → natural level

**Sport overall** is a 0–20 (F–S) blend for *that sport*, not a single career-wide number. A C dodgeballer can be an F hockey player; their natural level is computed per sport.

Working blend (pin later with playtest):

```
sportOverall = 0.45 × sport-skill mix  +  0.55 × relevant generals
```

Dodgeball skill mix is the mean of Throw Power, Throw Technique, Catch Technique, Offensive Anticipation. Relevant generals are Speed, Agility, Stamina, Damage Capacity. Special abilities do **not** inflate overall — they are the elite layer on top of the grade (`attributes.md`).

**Natural level** from that grade:

| Sport overall | Grade | Natural level | Will generally refuse |
|---|---|---|---|
| 0–8 | F – D | Parks (3) | — (nowhere lower) |
| 9–14 | C – B | City (2) | Parks |
| 15–20 | A – S | Summit (1) | City and Parks |

"Generally will not play lesser" means:

- **Hard lock: off.** You *can* put an A on a Parks roster. The engine allows it.
- **Soft lock: on.** Willingness, ego, and AI rostering treat a drop as wrong.
  - An A asked to play Parks is almost always a no (Playing Time / Spotlight / "I don't belong here").
  - A B on the way down (decline) may accept City, then Parks, with a sour ego.
  - A C prospect *can* play Summit minutes — that's a bet, not a refusal.

Playing **up** is always legal and often desirable (minutes for a prospect, emergency call-up). Playing **down** is the thing the pool resists.

## 5. Headcount — can we actually fill 36 teams?

Yes, if roster sizes stay honest and generation is a pyramid, not a flat roll.

### 5.1 Roster size (working)

Per-sport **competition roster** (the people who can dress). Club pool can be larger; unrostered names are the development squad (`athlete_development.md` §6).

| Sport | Starters | Competition roster (working) | 36-team dressed |
|---|---|---|---|
| Dodgeball | 6 | 9 (6 + 3) | 324 |
| Basketball | 5 | 10 | 360 |
| Others | sport-defined | ~8–12 | same order |

Plus, **per sport**, a free-agent / unattached cushion so Parks clubs can always fill a hole:

| Band | Working extra | Role |
|---|---|---|
| Free agents | ~20% of dressed (~65–70 for dodgeball) | Signable now |
| Youth intake / year | ~1 per club (~36) + a small unattached class | Next season's Parks blood |
| Total in-sport bodies | **~420–450** for a 9-man roster sport | Fits "a pretty big pool" without a thousand names to remember |

A four-sport save that shares athletes (multi-sport people counted once) is not 4 × 450. Many Parks bodies only exist for one sport. Specialists dominate; genuine multi-sport names are the minority (`athlete_development.md` §3). Working save-wide unique athletes: **~800–1,200**, not 2,000.

### 5.2 The pyramid (who is generated where)

Do **not** roll 450 athletes uniformly on 0–20. Staff each level from a band, then let a few outliers exist.

For one sport, dressed + free agents:

| Natural level | Share of pool | ~count @ 420 | Feeds |
|---|---|---|---|
| Parks (F–D) | ~50% | ~210 | 12 Parks rosters + most free agents |
| City (C–B) | ~35% | ~145 | 12 City rosters + a thin FA list |
| Summit (A–S) | ~15% | ~65 | 12 Summit rosters — tight, on purpose |

12 × 9 = 108 dressed per level. Parks has spare bodies (churn). Summit has almost no spare A/S names — stars are scarce, so poaching and development matter.

A handful of **mismatches** are generated on purpose (Section 7): the declining vet still wearing a Starter-A ego in Parks; the high-ceiling kid on a City bench.

## 6. How a level gets rostered

On new-save generation, and again whenever a club is empty:

1. **Each club rolls a house style** from its manager-player (aggression, youth, win-now — `rival_managers.md`).
2. **Target band** = that club's current level. Prefer athletes whose natural level **equals** the club level.
3. Fill starters first (best available in-band), then bench (depth, then a prospect).
4. **At most one** "too good" (passing through / declining) and **at most one** "too raw" (prospect playing up) per roster, unless the manager personality says otherwise (a Developer-type rival will stock prospects; a Superstar-type will chase the one name above the band).
5. Remainder of the world stays free agent, sorted by natural level so Parks can always find an F/E body and Summit almost never finds a free A.

Your club does the same thing in the intro's "build a team" step, except **you** pick. Skip will warn if you try to park a City-natural name in Parks for cheap wins — legal, sour.

Seasonal refresh:

- Youth intake lands mostly Parks-natural, a few City-ceiling kids still Parks-current.
- Retirements punch holes; AI clubs resign from the FA list in-band.
- Promotion/relegation **moves the club**, not the bodies automatically (Section 8).

## 7. The eligibility rule in play

| Situation | Allowed? | What happens |
|---|---|---|
| C prospect on a Summit bench | Yes | Grows if they get minutes; ego is usually quiet (they're up). |
| A starter asked to play Parks | Yes, engine | Almost always refuses / demands out. AI will not do this on purpose. |
| Declining B, club relegated Parks | Yes | Stay/go check at the boundary. Many leave for a remaining City club. |
| You start an A in Parks for a cup | Yes | Soft lock only. Expect Spotlight / Playing Time fire, and a FA feeding frenzy after. |
| Injury call-up from unrostered pool | Yes | Natural-level still applies to *willingness*, not to the lineup button. |

League integrity (open, see §10): a mid-season assignment window so you cannot loan three Summit names into Parks the night before a fixture. Paperwork stays free (`hub_actions.md`); the **window** is what stops abuse.

## 8. When a club changes level

Promotion and relegation are club events (`calendar_league.md` §2). The roster is not teleported as a blob.

At the season boundary, every rostered athlete runs a stay/go that *includes natural level*:

- Natural level **matches** the new level → stay is the default (ego still applies).
- Natural level is **above** the new level (relegated out from under a good player) → strong leave pressure. City clubs will bid.
- Natural level is **below** the new level (promoted with a Parks body) → they can stay as depth / mascot, or get cut by the AI to make room for in-band signings.

This is how the pyramid stays a pyramid after five seasons. A promoted Parks club that keeps its entire F-roster gets slaughtered in City *and* starts losing anyone who outgrew them. A relegated Summit club that keeps its A-cores is a yo-yo — and those A's will try to jump to the clubs that stayed up.

Your franchise is not exempt. Promotion is how you meet better players; it is also how your old core starts to look small.

## 9. One pool, many sports

The pool is save-global. Assignment is per sport.

- Rostered in dodgeball Parks and unrostered for basketball: fine.
- Rostered in two sports whose seasons overlap: legal, costly (fatigue + Playing Time ego in both — `calendar_league.md` §4).
- Natural level is **per sport**. The same person can be Summit-natural in one and Parks-natural in another. They will accept a Parks hockey roster and refuse a Parks dodgeball roster.

Generation should stamp an **aptitude profile** first (specialist vs multi), then roll sports, then derive each sport overall / natural level. Do not roll four sports independently at 0–20 or everyone is a secret Superstar.

## 10. System dependencies

1. **World-pool generator** — N unique athletes, pyramid weights, aptitude profiles, enough in-band bodies to dress 36 clubs + FA cushion, refreshed each offseason (intake + retirement).
2. **Sport-overall + natural-level derivation** — deterministic from current ratings so a developed kid *changes band* when they grow through 9 or 15.
3. **Club fill / AI resign** — in-band first, personality outliers second, never a silent roster of the wrong level.
4. **Willingness hook for "playing down"** — the soft lock. Same interview/retention action as today; the reason code is new.
5. **Boundary stay/go** — promotion/relegation as an input to the existing offseason evaluation (`conflict_chemistry.md` §6.1).
6. **36 club records per sport** — identity, level, manager-player, roster pointers into the pool.
7. **Assignment windows** (if we keep the soft lock from being cheesed).

## 11. Code alignment (2026-09)

- `CareerAthlete` already has identity, age, generals, personality, captain/mentor/player flags. It does **not** yet have per-sport ratings, ceilings, sport-overall, natural level, or a world-pool container — only the names you signed for the intro match.
- `CareerManager` + hub screens generate a *club* roster, not 36. The Parks League fixture is a slice, not the ecology.
- `calendar_league.md` still says "3–4 divisions, ≥8 teams." This plan is the proposed ruling: **3 levels, 12 teams.** When accepted, that doc should drop the open question and point here for population.
- `athlete_development.md` §2 asked for "hundreds." This plan gives the first real number: **~400+ dressed-and-FA per sport, ~800–1,200 unique per save.**
- Match modules keep reading ratings only. They must not know what level a club is in.

## 12. Open questions

- **Exact overall blend and band cuts.** 9 and 15 are placeholders. If Parks is all F and City starts at D, the climb changes.
- **Season length at 12 teams.** Single round-robin (11 games) is short; double (22) plus 2–4 free days is a long Sportland year when two sports overlap.
- **Assignment windows.** Lock competition rosters for the last N fixtures, or allow emergency injury replacements only?
- **Cup / friendlies.** Cross-level games (Parks club vs City) — does the soft lock still apply when it's not "their" league?
- **Display.** Do we show natural level on a card ("City-level player"), or only imply it through grade + willingness text?
- **Your character's natural level.** Superstar-A is Summit-natural from day one but the club starts in Parks. Exception: the player character *always* dresses. Ego-immune. The rest of the roster still follows the rule — you are the one mismatch Parks is built around.
- **Skip.** Mentor, ego-immune, always willing. He can play Parks forever. That is flavor, not a hole in the rule.

## 13. Parking lot

- Reserve/academy squads under the same 12 brands (rejected for v1; revisit if 36 identities are too many to care about).
- Draft instead of free-agent cushion.
- Hard engine lock ("cannot assign below natural level") if the soft lock is too easy to ignore.
- Per-level salary / willingness costs so a Summit name in Parks is expensive, not just rude.
