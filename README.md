# Codomon

Codomon is a cross-platform desktop application for understanding and monitoring complex codebases. It combines a visual architecture workspace, log import and replay, live log tailing, and Roslyn-based C# analysis into a single Avalonia app.

## What The App Does

- maps a codebase into Systems, Modules, and Code Nodes
- stores that model as a persistent workspace on disk
- imports log files and replays them against the workspace
- watches live log files and streams runtime events into the UI
- scans C# source with Roslyn to discover structure and suggest relationships
- supports manual overrides and optional LLM-assisted summaries and architecture hypotheses

In the repo's terminology, the hierarchy is:

```text
Codebase
└── System Map
    └── System
        └── Module
            └── Code Node
```

## Tech Stack

- .NET 8
- Avalonia 11 for the desktop UI
- MVVM-style ViewModels + Services + Persistence layers
- NodifyAvalonia for graph editing and canvas interaction
- Microsoft.CodeAnalysis (Roslyn) for static analysis
- xUnit for tests

## Solution Layout

```text
Codomon.Desktop/   Main application
Codomon.Tests/     Unit and regression tests
Vision/            Logos and design assets
OVERVIEW.md        Developer architecture overview
TERMINOLOGY.md     Domain terminology and hierarchy
build.sh           Linux/macOS build script
launch.sh          Linux/macOS launch script
checkreq.sh        Ubuntu-oriented .NET SDK check/install helper
winbuild.ps1       Windows build script
winlaunch.ps1      Windows launch script
```

## Inside `Codomon.Desktop`

- `Views/` and `Controls/`: Avalonia windows, dialogs, canvas, timeline
- `ViewModels/`: application state and workflow orchestration
- `Services/`: Roslyn scanning, log parsing/matching, system detection, LLM helpers, graph adapters
- `Persistence/`: workspace save/load, autosave, recent workspace tracking, user config
- `Models/`: workspace, graph, log, profile, and System Map data models

The main composition root is the desktop app plus main window and `MainViewModel`, which wires together:

- workspace lifecycle
- graph and system map views
- log replay and live monitoring
- profile switching
- scan status and persisted artifacts

## Main Workflows

### Workspace persistence

Workspaces are stored as JSON plus supporting folders for scans, logs, autosaves, and prompts. The app captures both diagram layout and analysis/runtime artifacts.

### Log ingestion

Imported logs are parsed into entries, loaded into the replay model, and matched against workspace nodes for highlighting and timeline updates. Live monitoring uses a file watcher path and feeds entries into the same general matching flow.

### Static analysis

`RoslynScanService` scans C# files under a chosen source path, discovers projects, files, classes, methods, and logging call sites, then produces suggested relationships. Those results can be merged into the System Map, where manual overrides remain authoritative.

## Requirements

- .NET 8 SDK or later

Optional environment check:

```bash
./checkreq.sh
```

Note: `checkreq.sh` is geared toward Ubuntu setup paths and can attempt SDK installation there.

## Build

Linux / macOS:

```bash
./build.sh
```

Windows PowerShell:

```powershell
.\winbuild.ps1
```

The build scripts compile `Codomon.Desktop` in `Release` and stamp version metadata into the app.

## Run

Linux / macOS:

```bash
./launch.sh
```

Windows PowerShell:

```powershell
.\winlaunch.ps1
```

Directly with the .NET CLI:

```bash
dotnet run --project Codomon.Desktop/Codomon.Desktop.csproj
```

## Test

```bash
dotnet test
```

Current tests cover focused behavior such as log matching, log entry handling, LLM settings resolution, system map identity, and regression cases.

## Developer Reading Order

For orientation, start here:

1. `Codomon.Desktop/Program.cs`
2. `Codomon.Desktop/App.axaml.cs`
3. `Codomon.Desktop/Views/MainWindow.axaml.cs`
4. `Codomon.Desktop/ViewModels/MainViewModel.cs`
5. `Codomon.Desktop/Persistence/WorkspaceSerializer.cs`
6. `Codomon.Desktop/Services/RoslynScanService.cs`
7. `Codomon.Desktop/Services/SystemDetector.cs`
8. `Codomon.Desktop/Services/SystemMapUpsertService.cs`

Additional repo docs:

- [OVERVIEW.md](OVERVIEW.md)
- [TERMINOLOGY.md](TERMINOLOGY.md)
