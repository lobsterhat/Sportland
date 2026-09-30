# Sportland — Code Organization

How `Assets/Scripts/` is laid out, and which parts of it are actually live.
For project-wide conventions and the agent contract, see [AGENTS.md](../../AGENTS.md).

## Namespace convention

Namespaces mirror folder paths: `Sportland.<FolderPath>`.

- `Assets/Scripts/Core/GameManagement/` → `namespace Sportland.Core.GameManagement`
- `Assets/Scripts/Sports/Dodgeball/` → `namespace Sportland.Sports.Dodgeball`

Two established exceptions: `Core/Athlete/` uses `Sportland.Core.Athletes` and
`Sports/_Shared/` uses `Sportland.Sports.Shared`.

There are no asmdefs, so every script compiles into a single `Assembly-CSharp`. Any
type is reachable from any other, and one compile error takes the whole game down.

## Folder structure

```
Assets/Scripts/
├── Core/
│   ├── Rating.cs              The 0–20 rating scale + F–S grade mapping
│   ├── GameManagement/        CoreGameManager, ISportModule  (legacy — see below)
│   ├── Athlete/               Athlete ScriptableObject
│   ├── Calendar/              CalendarSystem      (placeholder)
│   ├── Flags/                 FlagManager         (placeholder)
│   └── Player/                PlayerCharacter     (placeholder)
│
├── Career/                    The career layer: CareerManager, League, Club,
│                              CareerAthlete, AthleteGenerator, Archetype, Traits,
│                              CareerMatchContext
│
├── Sports/
│   ├── Dodgeball/             The live sport. Match, AI, ball, movement, abilities,
│   │                          court build-out, diagnostics HUD, career bridge.
│   ├── Demoball/              Shove / scoring-ring prototype
│   ├── Basketball/            The original sport, now dormant
│   │   ├── Gameplay/          BasketballPlayer, Ball, Hoop, Backboard, controller
│   │   └── Stats/             ShotOutcomeCalculator, ShotMissCalculator
│   ├── Tag/                   Tag prototype (safe zones, reset, "it" assignment)
│   └── _Shared/               BaseSportModule
│
├── Hub/                       The live hub: HubBootstrap, HubBuilding, HubInteractor,
│                              HubScreens, HubPlayerController, HubCameraFollow, HubHud
├── UI/Hub/                    HubMenuController  (legacy — see below)
│
├── InputHandling/             IInputSource, InputBroker, Player/Ai sources
├── Movement/                  BaseMovementController, MovementProfile
├── Rendering/                 Pixel-art renderer, sprite facing/sorting/offsets,
│                              stamina bar, player info bar, status icons
├── Diagnostics/               Physics recorder/overlay + Claude-backed debug assistant
│                              (dev builds only — see Diagnostics/README.md)
└── World/                     SurfaceDefinition, SurfaceType, SurfaceZone
```

## How a match actually starts

The live path is scene-based, not module-based:

`Hub/HubInteractor` → `SceneManager.LoadScene("Dodgeball")` → `CourtSetup` (on the
`DodgeballField` object) builds the court, spawns 12 players, the ball, the HUD, and
the debug cannon in `Start()`. For a league fixture, `CareerMatchDirector` carries the
career context in and returns to `HubWorld` at the end.

**Consequence:** most components are added at runtime and never pass through the
Inspector, so their tuning comes from **script field defaults**. To change a tuned
value, change the default in the script. New runtime systems get added to `CourtSetup`
behind a bool flag, the way `showDiagnosticsHud`, `spawnDebugCannon`, and
`physicsDebugAssistant` do.

## Legacy: the sport module pattern

`Core/GameManagement/` defines `ISportModule` + a `CoreGameManager` singleton
(`DontDestroyOnLoad`, additive scene loading, `SportType` enum), and
`Sports/_Shared/BaseSportModule` is its abstract base. This was the original
architecture. Today **only the dormant Basketball path and `UI/Hub/HubMenuController`
use it** — dodgeball, demoball, tag, and the live `Hub/` do not.

Don't build new work on it without deciding first whether it's being revived or
retired. `Hub/HubInteractor` is the pattern that's actually in use.

## Key patterns in live code

- **Ratings.** `Sportland.Core.Rating` is the single source of truth: `To01(v) = v/20`
  for gameplay math, `Grade(v)` for the player-facing F–S letter. Gameplay code reads
  `Effective*` values (base × ability × stamina), never raw bases — that seam is what
  makes Special Abilities apply everywhere with no per-system code.
- **Input indirection.** Gameplay reads `IInputSource` through `InputBroker`, with
  interchangeable player and AI sources, so the same movement code drives both.
- **Decoupled ball physics.** Lateral motion (court plane, with drag) is separate from
  a vertical `Height` with its own gravity sim. Don't collapse them into a 3D vector.
- **ScriptableObject data.** Athletes are data assets that persist independently of
  scenes.

## Adding a new sport

Follow dodgeball, not the module pattern:

1. `Sports/<Sport>/` with `namespace Sportland.Sports.<Sport>`.
2. A `<Sport>Setup` component that builds the field and spawns participants in
   `Start()`, plus a scene in `Assets/Scenes/` containing just that one object.
3. Reuse `Core/Rating`, `InputHandling/`, `Movement/`, and `Rendering/` rather than
   re-deriving them; add only sport-specific attributes alongside `GeneralAttributes`.
4. Point a hub building at the new scene in `Hub/HubInteractor`.
5. Add a doc to `docs/` describing what shipped, and run
   `node tools/unity-meta.mjs --check` before committing.
