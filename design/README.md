# Sportland — Design Docs

Living design canvas for the multi-sport sports-RPG. Spitball here; these files are
version-controlled and grow with the game.

## How to use these docs

The loop:

- **Chat (with Claude)** to *think* — float a half-formed idea, pressure-test it,
  find edge cases, organize a messy list. The back-and-forth is where ideas get sharp.
  But chat is ephemeral.
- **These docs** to *persist* — land the keepers here. This is the bridge from idea
  to implementation; when an idea matures it becomes code, and this is its backlog.
- **Claude memory** (`<repo>/.claude/.../memory/`) to *lock* — once a decision is
  settled, it's distilled into a memory file for cross-session recall. Memory is for
  decided things, not raw ideation.

Raw ideas go in the **Idea backlog** / **Parking lot** sections. As an idea firms up,
shape it into the spec body; when it's decided, note the lock in Claude memory.

## The game (vision)

A multi-sport arcade sports-RPG with a large, persistent player pool. Sports share a
core (court/field, movement, possession, collisions), each adds its own rules and
stats, and players carry their attributes + Special Abilities across sports.

The career is the spine: **You** assemble teams across sports and chase the top-level
championship. Players overlap between sports and teams, and progress is uneven — You
might be A-League dodgeball but D-League ice hockey — so the game must hold up across
*wildly* different skill levels, including F-heavy rosters early in the story.

## Design tenet: skill floor & ceiling

**F is a playable floor, not a broken one.** The gap from F to S must be **huge in
reliability and magnitude** — an S team genuinely slaughters an F team — but it is
**never a gap in capability**. An F-grade player can always execute the *basic actions*
of the sport; they just do them weakly and unreliably:

- F dodgeball **thrower** → the ball still *reaches* an opponent (slow, loopy, easy to
  read — but it gets there; it never just dribbles short).
- F **catcher** → can still *attempt* a catch (mostly bobbles, occasionally holds).
- F **mover** → still moves, just slow.

Two F teams play a sloppy but *real, fun* match. S-vs-F is a blowout, but F is still
playing the sport, not standing helpless. The rule: **never fail to *act* — only fail
to act *well*.** When tuning a grade's low end, protect the basic action first, then
let everything above it scale hard toward S.

## Attribute model (3 layers)

Rated attributes use a hidden **0–20** scale shown to players as an **F–S** grade. Full
spec — the attribute set, code mappings, and built-vs-to-build status — in
[attributes.md](attributes.md).

1. **General attributes** — cross-sport: Stamina, Recovery, Speed, Agility,
   Damage Capacity, Defensive Anticipation.
2. **Sport-specific attributes** — e.g. dodgeball: Throw Power, Throw Technique,
   Catch Technique, Offensive Anticipation.
3. **Special Abilities** — conditional modifiers that stack on top. See
   [special_abilities.md](special_abilities.md).

Effective stat = base × ∏(active ability multipliers), clamped [0,1]. Sport code reads
the `Effective*` values only, so abilities are felt everywhere without per-system code.

## Docs

### Match layer
- [attributes.md](attributes.md) — the attribute set (0–20 / F–S), the cross-sport vs
  sport-specific split, each one's mapping to current code, and conversion status.
- [defense.md](defense.md) — the Catch / Evade / Brace model: a zoned, skill-timed catch
  window, ramped bracing, single evade dash, and the stats that size them.
- [special_abilities.md](special_abilities.md) — ability architecture, a template, the
  built abilities as worked examples, and an idea backlog.
- [game_flow.md](game_flow.md) — match structure, modes, win conditions, and the
  RPG / progression layer around matches.

### Career / hub layer
- [hub_world.md](hub_world.md) — the hub as home base: core loop, the Skip-guided
  introduction, and the mentor character's full spec.
- [character_creator.md](character_creator.md) — archetype-first character creation:
  six archetypes on the playing-vs-managing axis, perks, creator flow.
- [conflict_chemistry.md](conflict_chemistry.md) — the ego layer: expectation +
  disposition traits, the ego budget, refusals, exits, and team chemistry.
- [hub_actions.md](hub_actions.md) — the daily action economy: budgets, the building
  action catalog, guest-list events, and the post-game address.
- [calendar_league.md](calendar_league.md) — tiered divisions with promotion/relegation,
  season anatomy, and the overlapping multi-sport year.
- [athlete_development.md](athlete_development.md) — the athlete pool: ceilings,
  growth/decline, development, familiarity, and the club-pool-vs-rosters model.
- [character_management.md](character_management.md) — the **world** player pool:
  3 levels × 12 teams (36 rosters per sport), sport-overall → natural level,
  per-sport interest, and the rule that the pool **grows when a sport is added**
  (seasons stagger; most people will not play every sport).
- [rival_managers.md](rival_managers.md) — every AI club's manager-player: shared parts,
  personality-driven club AI, and drama-without-scripts.
- [scouting.md](scouting.md) — scouting as activity channels (matches, 1v1 invitationals,
  interviews) and the mini-sport pipeline.

## Status

Built in dodgeball (branch `dodgeball_ai`): the 3-layer attribute seam, the ability
engine, two abilities (Hot Head, Sole Survivor), roster wiring, and a HUD readout. The
code map and commit list live in Claude memory `project_special_abilities.md`. The
multi-sport / career layer is still all design (this folder).
