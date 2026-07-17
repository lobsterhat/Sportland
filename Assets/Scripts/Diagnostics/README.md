# Physics Debug Assistant

A dev-only tool (editor/development builds; it destroys itself in release
builds). `PhysicsRecorder` keeps a rolling window of body state + game
events; `PhysicsDebugAssistant` answers plain-English questions about it via
Claude; `PhysicsDebugOverlay` is the backtick-toggled IMGUI panel.

`CourtSetup` spawns the whole kit behind its `physicsDebugAssistant` flag
(on for the lab, off during career fixtures). Set `ANTHROPIC_API_KEY` in the
environment before launching the editor — the assistant disables itself with
a console warning if it's missing.

## Two transports

`PhysicsDebugAssistant` has one public surface (`Ask` / `IsBusy` /
`LastAnswer` / `LastError`) and two implementations, chosen by the
`ANTHROPIC_SDK` scripting define:

- **default (define absent):** talks to the Messages API over
  `UnityWebRequest` + `JsonUtility`. Zero dependencies. This is what compiles
  and runs on a fresh clone.
- **`ANTHROPIC_SDK` defined:** uses the official Anthropic .NET SDK
  (streaming, retries, typed errors available for later growth).

## Switching to the official SDK

1. Open the project in Unity. NuGetForUnity (added to
   `Packages/manifest.json`) installs automatically.
2. **Window ▸ NuGet ▸ Restore Packages** (or let it auto-restore from
   `Assets/packages.config`). This pulls `Anthropic` and its transitive
   dependencies into `Assets/Packages`.
3. **Edit ▸ Project Settings ▸ Player ▸ Scripting Define Symbols**, add
   `ANTHROPIC_SDK`, apply. The assistant recompiles onto the SDK path.

To go back, remove the define — the REST path takes over again; the restored
DLLs are harmless when unused.

> Note: the SDK's netstandard2.0 build pulls in `System.Text.Json` and
> `Microsoft.Extensions.AI.Abstractions`. If the restore surfaces a
> duplicate-assembly conflict (Unity ships some of these), resolve it in the
> NuGetForUnity window — the project has no asmdefs, so everything shares one
> Assembly-CSharp.
