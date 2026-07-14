# Sportland — Player Character Creator Design

**Status:** Living design document
**Last updated:** 2026-07-14
**Scope:** Creating the player's character — identity, archetype selection, and the trade-offs that define how they'll play and manage.

> Part of the `design/` canvas. See `hub_world.md` for where character creation sits in the introduction flow (Skip guides it, step 2 of the intro); code mapping in **Code alignment** below.

---

## 1. Goals & Principles

- **Pick a fantasy, not a spreadsheet.** The player chooses an archetype — a readable identity with a clear promise — instead of allocating raw numbers cold. Numbers come after, as a small personal touch.
- **The trade-off is the choice.** Sportland's core tension is *playing* vs. *managing*. Every archetype is a position on that axis, and the creator makes the cost of each position obvious before committing.
- **Fast on the rails.** This happens inside the Skip-guided introduction; a first-timer should get through it in a couple of minutes without feeling railroaded, because Skip explains each archetype in plain terms.
- **Identity that lasts.** The archetype shapes an entire career — daily actions, training, morale, in-game presence — so it should feel like choosing who you are in this city, not a difficulty setting.

## 2. Creator Flow

1. **Identity.** Name and appearance (initial pass: sprite/palette selection; depth of customization is an open question).
2. **Archetype selection.** Card-based picker, one card per archetype: fantasy statement up top, play/management grades, signature perk, and honest weakness. Skip comments on each card as it's highlighted — plain-language "here's what your days will feel like."
3. **Personal touch.** A small pool of bonus stat points (proposed: enough to nudge, never enough to erase the archetype's shape) the player can drop onto physical or management stats.
4. **Summary & confirm.** Full character sheet preview — Skip gives a send-off line tailored to the chosen archetype — then into the hub.

## 3. The Two Meters

Every archetype is described against the same two meters, so cards are instantly comparable:

- **Playing** — the character's own on-field ability: athletic stats, sport-skill ceilings, and any in-game abilities. Governs how much you personally tip games you play in.
- **Management** — power over everything between games: how many daily actions you get, how effective training is, team morale, scouting accuracy, tactical options.

Grades use the game's shared rating scale (hidden 0–20 internal values surfaced as coarse **F–S** letter grades — see `attributes.md` and `Rating.cs`), so the player is learning to read a Sportland stat card while building their own.

## 4. Proposed Archetype Roster

Six archetypes spanning the axis — two extremes, one center, three management specialists with different flavors:

| Archetype | Playing | Management | Signature perk | The catch |
|---|---|---|---|---|
| **Superstar** | A (S ceiling) | D | *Take Over* — clutch-moment performance surge | Fewest daily management actions; the team leans on your play, not your leadership |
| **Player-Coach** | B | B | *Double Shift* — one bonus management action per day | Master of none: no elite edge anywhere |
| **Tactician** | C | A (tactics) | *Chalkboard* — expanded play-calling options and opponent-tendency reads in-game | Modest body: your genius is in the scheme, not your legs |
| **Motivator** | C | A (morale) | *Locker Room Aura* — passive team-wide morale/chemistry boost, slump protection | Weak on tactics and scouting; you inspire, you don't outscheme |
| **Mediator** | C | A (chemistry) | *Clear the Air* — greater chance to resolve conflicts between teammates; successful resolutions build team chemistry | Reactive power: a peaceful locker room leaves your gift idle, and you bring little tactics or training |
| **Developer** | C | A (training) | *Growth Eye* — training gains multiplier and the ability to spot hidden potential in athletes | Little direct game-day impact; your wins are built weeks earlier |

### Design notes per archetype

- **Superstar** is the "I want to win games myself" pick — the closest to a pure sports-action experience. Management scarcity is the real cost: fewer actions per day means practices, scouting, and events compete hard for your time.
- **Player-Coach** is the recommended default and the tutorial's implicit baseline: every system matters a medium amount, so the player sees the whole game.
- **Tactician** trades the body for the brain. In-game, this is the archetype that makes coaching *gameplay*: more calls, better information, visible opponent tendencies.
- **Motivator** is the people-person: the team plays above its numbers because of you. Deliberately low-maintenance mechanically (passive aura) so it suits players who want vibes over menus.
- **Mediator** is the locker-room diplomat. Where the Motivator *prevents* (a passive aura that keeps morale high), the Mediator *repairs*: when personality clashes, fights, or ego flare-ups happen between teammates, the Mediator has a much greater chance of resolving them — and a resolved conflict doesn't just return the team to neutral, it *builds* chemistry beyond where it started. Best on volatile, high-talent rosters; the archetype that makes signing difficult personalities a viable strategy.
- **Developer** is the long-game archetype for players who love progression systems — a farm-system fantasy. Strongest synergy with training and scouting screens.

### Relationship to Skip

Skip and the management archetypes deliberately don't overlap: **Skip gives information and suggestions; archetypes give mechanical power.** A Tactician with Skip on the bench gets his scouting intel *and* their own expanded play-calls — Skip never becomes redundant, and no archetype makes him mandatory.

## 5. System Dependencies

Stated as requirements, not implementation:

1. **Archetype definitions as data.** Name, fantasy text, stat template, meter grades, perk identifier, and modifier set — authorable without code changes so the roster can grow/rebalance. The same definitions power rival managers (`rival_managers.md`), so the creator and the rival generator share one source of truth.
2. **A persistent player-character record.** Identity + chosen archetype + bonus-point allocation, saved for the whole career.
3. **Modifier hooks.** Places where archetype effects land: daily action count, training-gain multiplier, team morale, in-game ability/play-call availability, scouting accuracy, conflict-resolution chance.
4. **A teammate conflict & chemistry system.** The Mediator requires conflicts to exist as real events with a resolution mechanic whose odds archetypes can modify, and a team-chemistry value that resolutions can raise. Full design in `conflict_chemistry.md`.
5. **Creator UI sequence.** Card picker + stat allocation + summary, embeddable inside the Skip-guided introduction (and skippable rails for repeat players are worth considering).

## 6. Open Questions

- **Roster final cut.** Are these the right six? A *scout-flavored* archetype (find hidden gems, read opponents) is possible but overlaps most with Skip — deliberately left out for now.
- **Motivator/Mediator balance.** Both are people-specialists; the prevent-vs-repair split must stay sharp in tuning. If chemistry systems end up shallow, consider whether they merge into one archetype.
- **Point pool.** Does the "personal touch" point allocation exist at all, and how big is it? (Proposed: small — flavor, not build-crafting.)
- **Appearance depth.** Palette swaps on a shared sprite vs. a fuller editor.
- **Respec & growth.** Is the archetype locked for a career? Can it evolve (Superstar aging into Player-Coach), or hybridize via unlocks?
- **Archetype-flavored dialogue.** Do Skip and other NPCs react differently to your archetype (Superstar gets fan attention, Motivator gets teammate confessions)?
- **Perk tuning.** Numbers for *Take Over*, aura strength, training multiplier, etc. are all placeholder concepts pending playtesting.

## Code Alignment (2026-07)

- **Archetype perks are Special Abilities.** The built engine (`special_abilities.md`, `SpecialAbility.cs` — ScriptableObject definitions, conditional multipliers, trigger rules) is exactly the shape perks need: *Take Over* is an event-latched ability à la Hot Head; *Clear the Air*/*Growth Eye*/*Double Shift* are the same data shape with hub-side triggers instead of match triggers.
- **The two meters ride the Rating scale** (`Rating.cs`, 0–20 → F–S); management stats become rated attributes alongside the general set in `attributes.md`.
- **`Core/Player/PlayerCharacter.cs` is a legacy placeholder** (pre-dates this design and the rating scale) — supersede it rather than extend it; the persistent character record belongs on the `PlayerProfile` thread.
- Archetype definitions as assets mirror the established authoring pattern (abilities, game modes are already ScriptableObjects).
