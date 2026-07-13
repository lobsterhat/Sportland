# Dodgeball

Sprite-based arcade dodgeball, 6v6, top-down court with side-view sprite players.
All code is under `Assets/Scripts/Sports/Dodgeball/` (namespace
`Sportland.Sports.Dodgeball`). Scene: `Assets/Scenes/Dodgeball.unity`.

> Scale: **1 Unity unit = 1 metre**. Court is volleyball-sized, 18 × 9, origin at
> centre.

## Running it

Open `Assets/Scenes/Dodgeball.unity` and press Play. `CourtSetup` (on the
`DodgeballField` object) builds the court, spawns 12 players, the ball, the
debug HUD, and the debug cannon at startup.

Build a standalone: menu **Sportland → Build → Windows** (`Assets/Editor/DodgeballBuilder.cs`)
→ `Builds/Windows/`.

## Controls (DualShock 4 / keyboard)

| Action | Gamepad | Keyboard |
|---|---|---|
| Move | Left stick / D-pad | WASD / arrows |
| Run | hold L2, **or** double-tap a D-pad direction (second tap while moving) | hold Left Shift |
| Jump | Cross (✕) | Space |
| Throw | Square (▢) | Q |
| Pass | Triangle (△) — tap = lob, hold = chest | F |
| Catch | Circle (◯) | E |
| Return ball to me (debug) | L1 | 1 |

The human controls one player (`A_In_2` by default). Control transfers to a
teammate when a pass you throw is caught.

## Teams & zones

3 infielders on each team's own half + 3 outfielders surrounding the opposing
half (Back / Top / Bottom strips). A player out of its assigned zone gets a
3-second return grace; 3 crossings in a rolling 30 s window raises a warning.
Outfielder strips are mutually exclusive. You can't catch while out of zone.

## Key systems

- **Movement** (`PlayerMovement`): walk/run speeds with an acceleration ramp;
  a jump arc; a duck (held crouch); and a **vertical body band**
  (`BodyBottom`/`BodyTop`) used for hit detection — jumping raises the bottom,
  ducking lowers the top.
- **Ball** (`Ball`): a state machine — Carried / Passing / Thrown / Bouncing /
  Loose. Lateral motion (court plane, with drag) is **decoupled** from a
  vertical `Height` (its own gravity sim) that drives the visual arc, catch
  height, and hit zones. Throws are gravity arcs that lead a moving target;
  flight-time math is drag-aware.
- **Throwing** is split into ratings that act *indirectly*: `throwSpeed` sets
  release velocity, `throwAccuracy` scatters the aim, `anticipation` leads a
  moving target (0 = aim where they are, 1 = where they'll be on arrival).
- **Catching** is a skill check (not automatic). Press Catch as the ball
  arrives; success chance =
  `clamp01( base(catching) − speedPenalty − throwPenalty + facing + timing + luck )`,
  then a roll. Watch every term live in the HUD's CATCH MATH panel.
- **Evasion** — four options the AI chooses between (and the human can do some
  of): **Catch**, **Duck** (under a high throw), **Jump** (over a low throw),
  **Sidestep** (off the ball's line). The choice keys off the ball's predicted
  arrival height.
- **CPU AI** (`DodgeballAI`, on every non-human player): idles toward its spawn
  spot, faces the carrier when an opponent holds the ball, and on an incoming
  opponent throw commits once to catch / duck / jump / sidestep.

## Player attributes

- `GeneralAttributes` (cross-sport): `luck`.
- `DodgeballAttributes` (sport-specific): `throwSpeed`, `throwAccuracy`,
  `anticipation`, `catching`. All 0–100, default 60 (luck 50).

Note: these and the AI/input/HUD/cannon components are added at runtime by
`CourtSetup`, so their tuning values come from script defaults — change defaults
in the script, or tweak live during Play.

## Debug tools (Play mode)

- **Cannon** (`DodgeballCannon`): fires the ball at the controlled player.
  C / R1 to fire; **right stick** moves it; top-right panel has speed / X / Y /
  anticipation sliders, a Fire button, and an Auto toggle. An orange marker
  shows its position.
- **HUD** (`DodgeballDiagnosticsHUD`, top-left): player speed/state/accel, ball
  speed + height, THROW telemetry (release & destination speed/height,
  distance), and the CATCH MATH breakdown. Speeds shown in u/s and mph.

Toggle both off via `showDiagnosticsHud` / `spawnDebugCannon` on `CourtSetup`.

## Open design questions

Unresolved since the start — hooks exist but behavior isn't decided:

1. What penalty fires after the out-of-zone warning?
2. What happens to a turned-over ball (drop / teleport / opposing inbound)?
3. What happens when a thrown ball hits a player in a restricted area?
4. Knockout behavior — eliminated player removed entirely, or becomes an
   outfielder?

There is **no elimination yet** — a thrown ball that isn't caught just caroms
(`Ball.OnHit(player, zone)` is the hook to build scoring/knockout on).

## Roadmap / not yet built

- AI offense: chasing loose balls, AI throwing & target selection.
- Elimination / scoring rules (the four questions above).
- A coupled-projectile model (if a future sport needs lob hang-time to affect
  lead).
- A human duck input (no face button free yet).
