# Sportland

A multi-sport arcade sports-RPG built in **Unity 6000.0.29f1** (built-in 2D sprite
pipeline, new Input System). Sprite-based players on top-down courts; a career layer
assembles rosters across sports and chases a championship.

The vision, the design tenets, and every unbuilt system live in [`design/`](design/README.md).
Read that first for *why*; read this file for *how the repo works*.

## Repo map

| Path | What's in it |
|---|---|
| `Assets/Scripts/` | All game code, namespace `Sportland.*` (111 files) |
| `Assets/Editor/` | Editor-only tooling (`DodgeballBuilder.cs` — the build menu) |
| `Assets/Scenes/` | `Dodgeball`, `Demoball`, `Basketball`, `HubWorld`, `TagPrototype` |
| `Assets/Prefabs/`, `Sprites/`, `BoldPixels/` | Art and prefabs |
| `Assets/Packages/` | Anthropic .NET SDK committed as managed plugins (see below) |
| `design/` | The living design canvas — specs for systems mostly not yet built |
| `docs/` | Implementation docs for what *is* built (`Dodgeball.md`) |
| `ProjectSettings/`, `Packages/` | Unity project config; edit through the Editor |

### Script layout

Namespaces mirror folder paths: `Assets/Scripts/Sports/Dodgeball/` →
`namespace Sportland.Sports.Dodgeball`.

```
Assets/Scripts/
├── Core/            Rating.cs (the 0–20 scale), GameManagement, Athlete,
│                    Calendar + Flags + Player (placeholders)
├── Career/          CareerManager, League, Club, CareerAthlete, AthleteGenerator, Traits
├── Sports/
│   ├── Dodgeball/   The deep one (34 files): match, AI, ball, abilities, HUD
│   ├── Demoball/    Shove/scoring-ring prototype (15 files)
│   ├── Basketball/  Gameplay/ + Stats/ — the original sport, now dormant
│   ├── Tag/         Tag prototype
│   └── _Shared/     BaseSportModule
├── Hub/             HubBootstrap, HubInteractor (the route into a match), HubBuilding,
│                    HubScreens, HubPlayerController, HubCameraFollow, HubHud
├── InputHandling/   IInputSource / InputBroker / Player + Ai sources
├── Movement/        BaseMovementController, MovementProfile
├── Rendering/       Pixel-art renderer, sprite facing/sorting, HUD bars, status icons
├── Diagnostics/     Physics recorder + Claude-backed debug assistant (dev builds only)
├── World/           Surface types and zones
└── UI/Hub/          HubMenuController
```

[`Assets/Scripts/README.md`](Assets/Scripts/README.md) goes deeper: which folders are
live vs. legacy, how a match actually boots, and how to add a sport.

> **Legacy worth knowing about:** `Core/GameManagement/` (`CoreGameManager` +
> `ISportModule`) was the original architecture. Only the dormant Basketball path and
> `UI/Hub/HubMenuController` still use it. The live route into a match is
> `Hub/HubInteractor` → `SceneManager.LoadScene("Dodgeball")` → `CourtSetup`.

## Ground rules for agents

**1. You cannot compile this project.** Unity is not installed in cloud
environments and there is no headless build. Nothing here type-checks your C#. So:

- Keep edits tight and locally reasoned; don't refactor broadly on speculation.
- Verify the symbols you call actually exist — grep for them rather than assuming
  a Unity or Sportland API shape from memory.
- End any C# change with an explicit hand-off: which scene to open, what to press,
  and what should happen. The human's Unity Console is the compiler.

**2. Every asset needs a `.meta` file.** Unity tracks assets by the GUID in the
sibling `.meta`, and the repo commits them. If you add `Foo.cs` without `Foo.cs.meta`,
a teammate pulling the change gets a *different* GUID than yours and prefab/scene
references silently break. After adding or deleting files under `Assets/`:

```bash
node tools/unity-meta.mjs --check   # list missing/orphaned .meta files
node tools/unity-meta.mjs --fix     # generate the missing ones
```

**3. Don't hand-edit scenes, prefabs, or `ProjectSettings/`.** They're large
Unity-serialized YAML with GUID cross-references. Describe the change for the human
to make in the Editor instead. The one exception is a narrow, obviously-scoped value
tweak you can point at precisely.

**4. Much of the wiring is runtime, not scene-authored.** `CourtSetup` (on
`DodgeballField`) builds the court, spawns 12 players, the ball, the HUD, and the
debug cannon on `Start()`. Components it adds get their tuning from **script
defaults**, so "change the default in the script" is usually the right move, not
"set it in the Inspector".

**5. Ratings are 0–20, shown as F–S.** `Sportland.Core.Rating` is the one source of
truth: `To01(v) = v/20` feeds gameplay math, `Grade(v)` returns the letter. Never
invent a parallel scale. Gameplay code should read `Effective*` values (base ×
ability × stamina multipliers), never raw bases — that's what makes abilities felt
system-wide with no per-system code. See [`design/attributes.md`](design/attributes.md).

**6. F is a playable floor.** The project's loudest design tenet: an F-grade player
always *executes* the basic action of the sport, just weakly and unreliably. When
tuning the low end of a range, protect the basic action first.
See [`design/README.md`](design/README.md#design-tenet-skill-floor--ceiling).

## Running and verifying

- **Dodgeball** (the live sport): open `Assets/Scenes/Dodgeball.unity`, press Play.
  Controls, teams/zones, and every debug tool are documented in
  [`docs/Dodgeball.md`](docs/Dodgeball.md). The in-game HUD's CATCH MATH panel shows
  every term of the catch roll live — it is the fastest way to check a tuning change.
- **Standalone build:** menu **Sportland → Build → Windows** → `Builds/Windows/`.
- **Scale:** 1 Unity unit = 1 metre. The dodgeball court is 18 × 9, origin centred.
- There is no automated test suite. `com.unity.test-framework` is present but unused.

## The Anthropic SDK plugins

`Assets/Packages/` holds the Anthropic .NET SDK and its full dependency closure as
committed netstandard2.0 DLLs — no NuGet, no restore step. It powers the dev-only
physics debug assistant and is **dormant** unless the `ANTHROPIC_SDK` scripting define
is set; without it the assistant uses a zero-dependency `UnityWebRequest` path.
The project has no asmdefs, so a broken DLL takes all of Assembly-CSharp down.
Read [`Assets/Scripts/Diagnostics/README.md`](Assets/Scripts/Diagnostics/README.md)
before touching any of it.

## Working in Cursor

Project rules live in [`.cursor/rules/`](.cursor/rules) and attach automatically by
file type:

| Rule | Attaches to |
|---|---|
| `sportland-project.mdc` | always — the core constraints |
| `unity-csharp.mdc` | `Assets/Scripts/**`, `Assets/Editor/**` |
| `unity-assets.mdc` | scenes, prefabs, `.meta`, `ProjectSettings/` |
| `design-docs.mdc` | `design/**`, `docs/**` |

Cloud agents for this repo run against a **dashboard-managed personal environment**,
not a committed config. That's deliberate: a repo-level `.cursor/environment.json`
takes precedence over the dashboard environment, so adding one here would silently
discard whatever base snapshot the dashboard environment provides. Change the
environment in the Cursor dashboard unless you want it versioned per branch — and if
you do commit one, port the existing snapshot settings over in the same change.

A cloud agent can read, write, and reason about the whole repo, but it has no Unity,
no .NET SDK, and no way to compile. Treat its C# output as a reviewed patch, not a
verified one.

### Coming from Claude Code

This repo was developed with Claude Code before Cursor. Two things carry over:

- **Memory → rules.** Decisions were "locked" in local memory files under
  `claude/.claude/.../memory/` (e.g. `project_dodgeball_status.md`,
  `project_special_abilities.md`). Those are *not* in the repo — they only exist on
  the machine that wrote them. Port their contents into `.cursor/rules/*.mdc` so the
  decisions become versioned and shared instead of living on one disk.
- **`claude/.claude/settings.local.json`** is a Claude Code permission allowlist full
  of hardcoded `D:/Unity/Sportland` paths. Cursor doesn't read it; it's inert. Keep it
  only if you still run Claude Code against this checkout.

> **Security:** an earlier commit added `.claude/mcp_settings.json` containing a live
> PixelLab API bearer token. The file is gone from the working tree but the token is
> still in git history and should be **rotated**.
