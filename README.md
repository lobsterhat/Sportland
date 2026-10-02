# Sportland

A multi-sport arcade sports-RPG in Unity. Sprite-based players on top-down courts,
wrapped in a career where you assemble rosters across sports and chase a championship.
Players carry their attributes and Special Abilities between sports, and progress is
uneven on purpose — you might be A-League dodgeball and D-League ice hockey.

**Unity 6000.0.29f1**, 2D sprite pipeline, new Input System.

## Getting started

1. Open the project in Unity 6000.0.29f1 and let it import.
2. Open `Assets/Scenes/Dodgeball.unity` and press Play — that's the deepest sport, and
   it builds its whole court, both teams, and its debug tooling at runtime.
3. Read [`docs/Dodgeball.md`](docs/Dodgeball.md) for controls, rules, and the debug
   HUD. The CATCH MATH panel shows every term of the catch roll live.

Standalone build: menu **Sportland → Build → Windows**.

## Where things are

| | |
|---|---|
| [`AGENTS.md`](AGENTS.md) | How the repo works — conventions, constraints, gotchas. Start here to contribute. |
| [`design/`](design/README.md) | The living design canvas. Vision, tenets, and specs for systems not yet built. |
| [`docs/`](docs/) | Documentation for what *is* built. |
| [`Assets/Scripts/README.md`](Assets/Scripts/README.md) | Code layout, live vs. legacy, how to add a sport. |
| [`.cursor/rules/`](.cursor/rules) | Project rules that auto-attach in Cursor by file type. |

## Status

Dodgeball is playable: 6v6, zoned court, skill-checked catching, four evasion options,
CPU AI, the 3-layer attribute model, and two Special Abilities. A first career slice
(clubs, leagues, athlete generation, traits) feeds real fixtures onto that court, and
the hub world has buildings and screens wired to it.

Demoball, Basketball, and Tag are prototypes at varying stages. The calendar,
chemistry/conflict, hub action economy, scouting, and rival managers are designed but
unbuilt — see [`design/`](design/README.md).

## Conventions worth knowing before your first commit

- **Ratings are 0–20 internally, shown as F–S grades.** `Sportland.Core.Rating` is the
  only source of truth.
- **F is a playable floor, not a broken one.** An F player always executes the basic
  action of the sport, just weakly and unreliably. Never fail to act — only fail to
  act *well*.
- **Commit `.meta` files.** Unity resolves references by the GUID inside them. Run
  `node tools/unity-meta.mjs --check` before committing changes under `Assets/`.
