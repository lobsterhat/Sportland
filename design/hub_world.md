# Sportland — Hub World Design

**Status:** Living design document
**Last updated:** 2026-07-14
**Scope:** The hub world experience — game introduction, character creation, and team/player management outside of scheduled games.

> Part of the `design/` canvas (see `README.md` for the chat → docs → memory loop). Design-first: describes what the systems must do; code mapping lives in each doc's **Code alignment** section.

---

## 1. Purpose & Scope

The hub world is the player's home base and the connective tissue of Sportland. Everything that happens outside of playing a scheduled game happens here:

- **Introduction** — the new player's first session: welcome, guided setup, first game.
- **Character creation** — building the player's own character (identity, class, skill vs. management trade-offs).
- **Team & player management** — rosters, training, scouting, scheduling, and recovery between games.

Scheduled games themselves are owned by the individual sport modules; the hub hands off to them on game day and receives the results (stats, fatigue, triggered Special Abilities, consequences) when the player returns.

## 2. Core Loop

1. **First launch → Introduction.** New game intro, then character creation, guided end-to-end by the mentor (Section 4).
2. **Hub as home base.** A walkable town with buildings as interaction points:
   - **Arena** — launches scheduled games for whichever sport is in season.
   - **Office** — roster and team management, league standings, scheduling.
   - **Hospital** — fatigue and injury management.
   - **Cafe / Home** — morale, social events, and advancing to the next day.
3. **Between-game management.** Days between scheduled games are spent on management actions — training, scouting, roster moves — each consuming time until the next game day arrives. Full design of the daily action economy in `hub_actions.md`.
4. **Game day round trip.** The hub launches the sport, the game plays out, and the player returns to the hub where results are surfaced: stat changes, Special Abilities gained or triggered, fatigue accrued, and anything else with consequences for the days ahead.

## 3. Introduction Flow

The introduction is not a menu-driven tutorial overlay — it is a guided first play-through of the real loop, led by the mentor. Every step teaches by doing:

1. **Welcome.** The mentor greets the new player and frames Sportland: a city of sports where you play *and* manage.
2. **Character creation.** The mentor walks the player through building their character — name, look, and archetype choice, with the skill-vs-management trade-off explained in plain terms. Full design in `character_creator.md`.
3. **Build a team.** Guided first roster construction: what the stats and letter grades mean, how to read an athlete, how to sign one.
4. **Join a league.** Picking a league/competition, what the schedule commitment means, and how the season calendar works. Full design in `calendar_league.md`.
5. **Schedule practice.** First management action: setting up practice, what training does, and the cost of a day.
6. **First game.**
   - **Pre-game:** lineup selection, opponent scouting intel from the mentor.
   - **In-game:** the mentor's hint system (Section 4.3) is live from the bench.
   - **Post-game:** results recap — what the stats mean, what abilities/fatigue were earned, and what to do about it tomorrow.
7. **Hub freedom.** The guided rails come off. The mentor remains available but the player now drives.

Tutorial progress is recorded persistently per step, so the introduction never repeats — including across the mentor being released and rejoining (Section 4.4).

## 4. The Mentor — "Skip" *(working name, provisional)*

Inspired by Marvin from Pawapuro Baseball: an assigned companion who turns the tutorial into a relationship instead of a menu.

> **Naming note:** "Skip" is baseball slang for the skipper/manager — it fits the assistant-coach flavor. The name, look, and final personality are open flavor decisions (Section 6).

### 4.1 Identity & Personality

- Friendly, patient, and completely without ego. Genuinely delighted to help.
- An assistant coach/scout archetype: knows the game deeply, just can't play it.
- Unshakeable: never guilt-trips, never sulks, never holds a grudge (see 4.4).

### 4.2 Dual Role

**In the hub — tutorial guide.** Skip runs the entire introduction flow (Section 3) and remains a source of contextual tips afterward: he explains any screen, system, or decision the player is facing.

**Around games — assistant coach/scout.** Before, during, and after games Skip provides hints, tips, and suggestions (see 4.3).

### 4.3 Playable Athlete & Hint System

Skip is a **real, playable athlete on the roster** — not a UI element:

- **Very poor skills.** His athletic stats sit near the bottom of the scale in every sport. Fielding him is always allowed and almost always a competitive sacrifice.
- **No special-casing in gameplay.** Sports treat him like any other athlete; what makes him the mentor is a permanent mentor trait on his athlete data, which the hint and tutorial systems key off.

**Hint rules — being on the team is enough.** As long as Skip is signed to the player's team, his hints flow from the bench/sideline like a real assistant coach. Fielding him is optional and grants nothing extra — the choice to play him is flavor (and risk), not a hint unlock.

Hint categories:

| When | What Skip provides |
|---|---|
| Pre-game | Opponent scouting intel, matchup notes, lineup suggestions |
| In-game | Situational tips and suggestions (read the defense, who's hot, when to rest a player) |
| Post-game | Results interpretation, development suggestions, what to prioritize before the next game |

### 4.4 Release & Rejoin Contract

- Skip can be **released from the team at any time**, exactly like any other athlete.
- Releasing him carries **zero penalty**: no morale hit, no chemistry hit, no cost, no guilt dialogue.
- He is **always willing to rejoin**, at any later point, instantly and without conditions — permanently available in free agency (or equivalent).
- **Tutorial progress survives release.** Rejoining never restarts the introduction; completed steps stay completed.
- While released, his hints are simply absent — the only "penalty" is the natural loss of what he provides.

This makes Skip a knob the player turns freely: keep him for the coaching layer, release him for the roster spot and the purist experience, re-sign him whenever the training wheels sound nice again.

## 5. System Dependencies

What the mentor design requires from the architecture, stated as requirements (not mapped to current code — the latest implementation lives ahead of this session's checkout):

1. **A team/roster concept.** "Release from the team" and "rejoin" only mean something once the player's team is a real entity with sign/release operations.
2. **A mentor trait on athlete data.** A permanent trait that marks Skip as the mentor, so hint and tutorial systems can key off it without sports special-casing him.
3. **Persistent tutorial-progress record.** Per-step completion state that survives sessions and survives Skip being released/re-signed.
4. **A hint-delivery hook in the game loop.** Pre-game, in-game, and post-game moments where the hint system can surface Skip's tips when he's on the team.
5. **A free-agency (or equivalent) pool.** Somewhere Skip lives while released, always signable at no cost.

## 6. Open Questions

Deferred decisions, to be settled as the design conversation continues:

- **Final name, look, and voice.** "Skip" is provisional; his sprite/appearance and dialogue tone are undecided.
- **Hint frequency & UX.** How often in-game tips appear, how they're presented (speech bubble, ticker, pause-menu advisor?), and whether they can be muted without releasing him.
- **Physical presence in the hub.** Does Skip walk around the hub world as an NPC you approach, follow the player, or live in a menu/contact list?
- **Dialogue system.** What drives his conversations — a simple linear script per tutorial step, or a reusable dialogue system other NPCs will share?
- **Hint content sourcing.** Are tips hand-authored per situation, generated from live game state, or both?
- **Does Skip scale?** Whether his scouting/tips improve over the course of a career, or he stays deliberately static as a baseline advisor.

## Code Alignment (2026-07)

- **The hub scene exists as a placeholder**: `Assets/Scenes/HubWorld.unity` is a button menu driven by `HubMenuController.cs` — to be replaced by the walkable hub. The movement/rendering stack built for the sport prototypes (`Movement/BaseMovementController` + `MovementProfile`, the `Rendering/` sprite tools, `World/SurfaceZone`) reuses directly for hub navigation.
- **`CoreGameManager` (Core/GameManagement) is the loop's anchor**: it already owns additive sport-scene loading, `ReturnToHub()`, result processing, and an `AdvanceDay()` stub. It predates the dodgeball conventions, so expect a refit rather than a rewrite.
- **Skip is data, not code**: an athlete on the shared player-profile thread (see `game_flow.md` — the `PlayerProfile` asset is the likely home) with a permanent mentor trait. Sports never special-case him — consistent with dodgeball reading only `Effective*` stats.
- **Hint hooks** ride the existing `ISportModule` seams (`GameContext` in, `GameResult`/significant events out).
