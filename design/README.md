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

- [attributes.md](attributes.md) — the attribute set (0–20 / F–S), the cross-sport vs
  sport-specific split, each one's mapping to current code, and conversion status.
- [special_abilities.md](special_abilities.md) — ability architecture, a template, the
  built abilities as worked examples, and an idea backlog.
- [game_flow.md](game_flow.md) — match structure, modes, win conditions, and the
  RPG / progression layer around matches.

## Status

Built in dodgeball (branch `dodgeball_ai`): the 3-layer attribute seam, the ability
engine, two abilities (Hot Head, Sole Survivor), roster wiring, and a HUD readout. The
code map and commit list live in Claude memory `project_special_abilities.md`. The
multi-sport / career layer is still all design (this folder).
