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

The SDK and its full dependency closure are **committed** under
`Assets/Packages/` (netstandard2.0 builds, reference-validation disabled in
their `.meta`s so Unity loads them). No NuGet restore is needed — a plain
`git pull` gives you everything. Switch it on in two staged steps:

1. **Pull the branch and reopen the project.** Unity imports the DLLs under
   `Assets/Packages/`. Watch the Console: it should compile clean with **no**
   "will not be loaded due to errors" messages. At this point the assistant
   is still on the REST path (the SDK code is dormant behind the define), so
   a clean compile here proves the DLLs resolve before any of our code
   depends on them. **If you see DLL errors, stop here and report them** —
   don't do step 2, because enabling the define while a DLL is broken takes
   Assembly-CSharp (the whole game) down with it.
2. **Edit ▸ Project Settings ▸ Player ▸ Scripting Define Symbols**, add
   `ANTHROPIC_SDK`, apply. The assistant recompiles onto the SDK path.

To go back, remove the define — the REST path takes over; the DLLs are inert
when unused.

### Why these specific DLLs

Unity 6's .NET Standard 2.1 profile already provides the low-level shims
(`System.Memory`, `System.Buffers`, `System.Runtime.CompilerServices.Unsafe`,
`System.Threading.Tasks.Extensions`, `System.Numerics.Vectors`), so those are
deliberately **omitted** — adding the NuGet copies would collide with Unity's
built-ins (duplicate types). Committed instead is only what Unity lacks:
`Anthropic`, `Microsoft.Extensions.AI.Abstractions`,
`Microsoft.Bcl.AsyncInterfaces`, `System.Collections.Immutable`,
`System.IO.Pipelines`, `System.Net.ServerSentEvents`,
`System.Text.Encodings.Web`, `System.Text.Json`. The project has no asmdefs,
so all of this shares one Assembly-CSharp — hence the staged verify above.
