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
