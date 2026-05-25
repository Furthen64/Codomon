# Codomon

A desktop "code telescope" for understanding and monitoring complex codebases. Codomon combines a visual architecture workspace, log import/replay/live monitoring, and static code analysis powered by Roslyn into a single cross-platform tool.

## Features

- **Visual workspace** — drag-and-drop canvas of Systems, Modules, and their connections
- **Log ingestion and replay** — import log files, replay them against the canvas, or tail live log files
- **Roslyn static analysis** — scan C# source code to automatically discover systems and modules
- **System Map** — a structured architecture model built from code analysis and manual overrides
- **LLM integration** — optional AI-powered architecture hypothesis and summary generation
- **Persistent workspaces** — workspace layout, rules, and analysis artifacts saved as JSON on disk

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8) or later

Run the requirements check script to verify your environment:

```bash
./checkreq.sh
```

## Build

**Linux / macOS**

```bash
./build.sh
```

**Windows (PowerShell)**

```powershell
.\winbuild.ps1
```

Both scripts build in `Release` configuration and stamp the binary with the current version (`0.1.0`) and build date.

## Run

**Linux / macOS**

```bash
./launch.sh
```

**Windows (PowerShell)**

```powershell
.\winlaunch.ps1
```

Alternatively, run directly with the .NET CLI from the repo root:

```bash
dotnet run --project Codomon.Desktop/Codomon.Desktop.csproj
```

## Project Structure

```
Codomon/
├── Codomon.Desktop/          # Single-project desktop application
│   ├── Controls/             # Reusable Avalonia controls (canvas, timeline)
│   ├── Models/               # Data structures (workspace, systems, logs, scan results)
│   ├── Persistence/          # Workspace serialization and autosave
│   ├── Services/             # Feature logic (scanning, parsing, matching, LLM, etc.)
│   ├── ViewModels/           # MVVM view models
│   └── Views/                # Avalonia windows and dialogs
├── build.sh / winbuild.ps1   # Build scripts
├── launch.sh / winlaunch.ps1 # Launch scripts
├── checkreq.sh               # Requirements check
├── OVERVIEW.md               # Developer architecture overview
└── TERMINOLOGY.md            # Hierarchy terminology reference
```

## Libraries

| Library | Version | Purpose |
|---|---|---|
| [Avalonia](https://avaloniaui.net/) | 11.2.3 | Cross-platform UI framework |
| [Avalonia.Themes.Fluent](https://avaloniaui.net/) | 11.2.3 | Fluent design theme for Avalonia |
| [Avalonia.Fonts.Inter](https://avaloniaui.net/) | 11.2.3 | Inter font for Avalonia |
| [NodifyAvalonia](https://github.com/BAndysc/nodify-avalonia) | 6.6.0 | Node-based graph editor controls (port of Nodify for Avalonia) |
| [Microsoft.CodeAnalysis.CSharp](https://github.com/dotnet/roslyn) | 4.9.2 | Roslyn C# compiler and analysis APIs |

## Developer Documentation

- [OVERVIEW.md](OVERVIEW.md) — architecture overview, layer responsibilities, startup sequence, and recommended reading order for new developers
- [TERMINOLOGY.md](TERMINOLOGY.md) — definitions for Codebase, System Map, System, Module, and Code Node

## Example Workflow: Analyzing AnimalHaus with Codomon

If you want a concrete repo to practice on, [`Furthen64/AnimalHaus`](https://github.com/Furthen64/AnimalHaus) is a good fit: it is small enough to understand in one sitting, but structured enough to exercise Codomon's system, module, docs, and runtime views.

### 1. Start with repo shape and runnable units

Open `README.md` and `AnimalHaus.sln` in the target repo first.

This quickly establishes the top-level buckets Codomon should help you model:

- 4 runnable systems: `Pigpen`, `Barn`, `Tractor`, `MarketPlace`
- 3 shared libraries: `AnimalHaus.Shared.Core`, `AnimalHaus.Shared.Utils`, `AnimalHaus.Shared.Messaging`
- 1 contracts project: `AnimalHaus.Contracts`
- 1 tool: `AdministrationApp`
- 2 test projects: `AnimalHaus.Shared.Tests`, `AnimalHaus.Integration.Tests`

In Codomon terms, this is the first pass where the human decides what belongs to domain behavior, shared infrastructure, contracts, tooling, and validation.

### 2. Read architecture docs before implementation files

Next, read `docs/architecture.md` and `docs/message-contracts.md`.

These files provide the mental model that makes the rest of the repo readable:

- each process owns a PUB socket and a REP socket
- systems communicate over ZeroMQ
- the default happy-path is `Pigpen -> Barn -> Tractor -> Pigpen`
- `MarketPlace` broadcasts price events to every other system

In Codomon, this is the point where the user should begin sketching systems and high-level connections instead of reading code file-by-file without context.

### 3. Understand the contracts and messaging layer

After the docs, inspect the shared interaction layer:

- `src/contracts/AnimalHaus.Contracts/Commands.cs`
- `src/contracts/AnimalHaus.Contracts/Events.cs`
- `src/shared/AnimalHaus.Shared.Messaging/*`

This is the narrow waist of the sample. It defines:

- which commands and events exist
- how messages are wrapped
- what metadata is attached
- how topics are named
- where ZeroMQ request/reply and pub/sub plumbing lives

Once these files are clear, Codomon's system and module views become much easier to interpret because transport concerns can be separated from actual business behavior.

### 4. Trace one end-to-end scenario through the host files

Use the feed-dispatch scenario as the first walkthrough:

1. start in `src/systems/AnimalHaus.Pigpen/PigpenHost.cs`
2. follow `RequestDispatch` into `BarnHost.cs`
3. follow `AssignTask` into `TractorHost.cs`
4. then return to Pigpen's handling of `DispatchCompleted`, `TaskCompleted`, and `MarketPriceChanged`

Only after the host-level orchestration is clear should you drop into each system's `Modules/` folder.

That order matters: the host files explain system coordination, while the modules explain local state changes such as feeding, inventory, hauling, fuel, and health updates.

### 5. Validate understanding with tests and runtime configuration

Finish by checking how the scenario is exercised and configured:

- `tests/AnimalHaus.Integration.Tests/DistributedSimulationTests.cs`
- `tests/AnimalHaus.Shared.Tests/MessagingTests.cs`
- system `appsettings.json` files
- `build.sh`
- `launch.sh`
- `src/tools/AdministrationApp`

These files answer the practical questions a human usually has at the end of a first pass:

- what behavior is considered important enough to test
- how the systems are launched together
- how ports, peers, timing, startup delay, and seeds are configured
- how the administration tool edits configuration without hand-editing JSON

This is the final Codomon step: verify that the system map you built from docs and code matches the repo's actual runnable and testable behavior.
