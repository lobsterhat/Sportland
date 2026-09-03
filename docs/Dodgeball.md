# Dodgeball

Sprite-based arcade dodgeball, 6v6, played on an angled court with side-view
sprite players — the Technos Super Dodge Ball look. All code is under
`Assets/Scripts/Sports/Dodgeball/` (namespace `Sportland.Sports.Dodgeball`).
Scene: `Assets/Scenes/Dodgeball.unity`.

> Scale: **1 Unity unit = 1 metre**. Court is volleyball-sized, 18 × 9, origin at
> centre. The sim is a **flat plane** — the angle is drawn, not simulated.

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
| Jump | Cross (✕) — always a hop; with the ball you can throw across a line before landing | Space |
| Throw | Square (▢) | Q |
| Pass | Triangle (△) — tap = lob, hold = chest | F |
| Catch | Circle (◯) | E |
| Stance | R2 — toggle: face the ball, move slower, full catch/evade | Left Ctrl |
| Return ball to me (debug) | L1 | 1 |
| Flatten the court (debug) | — | V |

The human controls one player (`A_In_2` by default). Control transfers to a
teammate when a pass you throw is caught.

## Teams & zones

3 infielders on each team's own half + 3 outfielders surrounding the opposing
half (Back / Top / Bottom strips). A player out of its assigned zone gets a
3-second return grace; 3 crossings in a rolling 30 s window raises a warning.
Outfielder strips are mutually exclusive. You can't catch while out of zone.

## The court on screen

The court is drawn at an angle, after the Technos Super Dodge Ball courts: a
floor that recedes and narrows slightly toward the back, banded across its depth
like mown turf, with the stands rising behind the far sideline.

**None of it is simulated.** The sim stays the flat 18 × 9 plane it always was —
X along the court, Y as depth — and `CourtProjection` is the single place that
says where a point on that plane lands on screen. Everything that draws consults
it, which is what keeps the floor, the players, the ball, the shadows and the
world-anchored labels agreeing with each other.

Three knobs make the look, and they are independent:

| Knob | Default | What it does |
|---|---|---|
| `depthSquash` | 0.5 | Screen units per metre of depth, against 1 per metre of width. Tilts the floor away from the camera — the effect doing most of the work. |
| `farScale` | 0.82 | Width of the far edge against the near edge. 1 is a plain rectangle (the NES look); lower narrows the back into a trapezoid (the arcade look). |
| `spriteDepthScale` | 0 | How much sprites shrink with depth. **0 = constant size at every depth**, which is the Technos cheat and what keeps pixel art off fractional scales. |

Plus `depthBunch` (extra curve in the depth axis; keep near 1, since heavy
bunching makes movement speed visibly change with depth) and `heightLift`
(screen units per metre of jump or ball height — at the default 1, height reads
about twice as strongly as depth, which is what stops a jump looking like a step
toward the camera).

All five are live on `CourtSetup`, and **V** flattens the whole thing back to the
old top-down view for an A/B look. Flat is the identity projection, not a second
code path.

Two consequences worth knowing. Constant-size sprites over a narrowing floor mean
a far-court player slightly overhangs the sideline — invisible while `farScale`
stays mild, and it is what the arcade game does. And because depth is squashed,
pushing the stick up covers less screen than pushing it sideways; the shadows on
players and the ball are what keep a hop distinguishable from a step.

Who does what:

- `CourtProjection` — the mapping and its inverse. Anything crossing between sim
  metres and the screen goes through here, including mouse picking.
- `DodgeballCourtRenderer` — the static floor, lines and backdrop, subdivided
  across depth because the depth curve isn't linear. Drop a sprite into its
  `floorSprite` / `backdropSprite` slots to replace the procedural stand-ins; the
  floor sprite is UV-mapped across the trapezoid, so author it as a plain
  rectangle.
- `DodgeballCourtView` — places the moving parts each frame. It only ever *adds*
  to what the owning components wrote, so the duck squash and the shadows'
  jump-shrink survive.
- `DodgeballCameraRig` — framing. `FitPlayArea` holds the whole court on screen;
  a working `Follow` mode is the seam for the arcade's scrolling camera, left off
  because scrolling also wants backdrop parallax and off-screen player markers.

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
- **Flat view** (**V**): drops the court back to top-down. Handy for judging a
  projection tweak against the thing it replaced, and for reading positions
  without the depth squash in the way.

Toggle the first two off via `showDiagnosticsHud` / `spawnDebugCannon` on
`CourtSetup`.

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
