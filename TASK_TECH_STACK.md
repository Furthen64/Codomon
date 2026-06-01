# TASK: Scan Tech Stack

## Goal

Add a new standalone feature called `Scan Tech Stack` to Codomon.

This feature should be clearly separate from the existing Roslyn/source scan workflow and should appear earlier than architecture analysis in the learning pipeline.

The intended pipeline becomes:

1. `Scan Source`
2. `Scan Tech Stack`
3. `Generate File Summaries`
4. `Architecture Synthesis`

## Why This Exists

Codomon currently scans source structure and later performs higher-level architecture synthesis. A dedicated tech stack scan adds a cheaper, more deterministic intermediate step that gives users immediate insight into what frameworks and tools a codebase uses.

This is useful because:

- it provides fast, concrete value before any LLM-driven analysis
- it helps users orient themselves in unfamiliar repos
- it can later improve architecture analysis prompts with grounded context
- it avoids overloading the source scan with unrelated responsibilities

## Product Shape

This should be an explicit, standalone feature:

- UI action: `Scan Tech Stack`
- separate dialog/window flow from Roslyn scan
- separate persisted scan artifact
- separate view model and service

It should not be hidden inside `Scan Source`, and it should not be treated as part of system-map generation in phase 1.

## Architectural Fit

Codomon already has a good pattern for workspace-scoped analysis artifacts:

- source path stored in `WorkspaceModel`
- scan results stored under `scans/`
- dedicated view model + dialog workflows
- JSON persistence for recoverable workspace artifacts

Tech stack scanning should follow the same pattern.

Recommended new pieces:

- `TechStackScanService`
- `TechStackScanViewModel`
- `TechStackScanDialog`
- `TechStackScanResult`
- `DetectedTechnology`
- `TechnologyEvidence`

## Scope For Phase 1

Phase 1 should answer:

`What technologies does this workspace use, and why do we think so?`

It should not attempt to fully resolve dependency graphs, transitive packages, or deep build semantics.

### Detection inputs for MVP

Use these in order of confidence:

1. `.csproj` `PackageReference` entries
2. `.csproj` SDK and project properties
3. known config files
4. infrastructure files in the repo/project folders
5. optional later: code-level hints from Roslyn scan

This keeps the feature deterministic and explainable.

## What We Detect

The MVP should detect common technologies by category.

### Suggested categories

- `Runtime`
- `UI`
- `Web`
- `Data`
- `Database`
- `Messaging`
- `Scheduling`
- `Logging`
- `Observability`
- `Testing`
- `Infrastructure`

### Suggested MVP technologies

- `.NET`
- `ASP.NET Core`
- `Avalonia`
- `Generic Host`
- `Worker Service`
- `Entity Framework Core`
- `Dapper`
- `Npgsql`
- `Microsoft.Data.SqlClient`
- `MongoDB`
- `MassTransit`
- `RabbitMQ`
- `Azure Service Bus`
- `Quartz`
- `Hangfire`
- `Serilog`
- `NLog`
- `log4net`
- `OpenTelemetry`
- `xUnit`
- `NUnit`
- `Moq`
- `Docker`
- `docker-compose`

This list can expand later, but phase 1 should stay intentionally small and high-signal.

## Detection Strategy

### 1. Parse `.csproj`

This is the highest-value source for the MVP.

From project files, detect:

- `PackageReference Include="..." Version="..."`
- SDK values such as `Microsoft.NET.Sdk.Web`
- output/project properties that imply executable/web/worker behavior

Examples:

- `Microsoft.NET.Sdk.Web` => `ASP.NET Core`
- `Avalonia` packages => `Avalonia`
- `Microsoft.EntityFrameworkCore` => `Entity Framework Core`
- `Serilog.AspNetCore` or `Serilog` => `Serilog`
- `OpenTelemetry.*` => `OpenTelemetry`
- `Quartz` => `Quartz`
- `Hangfire.*` => `Hangfire`

### 2. Scan known config files

Look for files such as:

- `appsettings.json`
- `appsettings.*.json`
- `serilog.json`
- `nlog.config`
- `log4net.config`

These should not usually be the only evidence for a technology, but they are useful supporting signals.

### 3. Scan infrastructure files

Look for:

- `Dockerfile`
- `docker-compose.yml`
- `docker-compose.yaml`

Later phases may add:

- `*.k8s.yaml`
- `helm` charts
- GitHub Actions deployment markers

### 4. Later: Roslyn/code-level hints

Not required for phase 1, but good for phase 2.

Examples:

- `services.AddDbContext` => EF Core
- `UseSerilog` => Serilog
- `AddOpenTelemetry` => OpenTelemetry
- `AddMassTransit` => MassTransit

These should be additive evidence, not the core detection source.

## Result Model

Tech stack scanning should produce a separate persisted result model, not be mixed into `RoslynScanResult`.

### Suggested `TechStackScanResult`

Fields:

- `Schema`
- `ScanTime`
- `SourcePath`
- `Projects`
- `Technologies`

### Suggested `DetectedTechnology`

Fields:

- `Name`
- `Category`
- `Confidence`
- `Version`
- `ProjectName`
- `ProjectFilePath`
- `Evidence`

### Suggested `TechnologyEvidence`

Fields:

- `Source`
- `Description`
- `SourceRef`

This mirrors the explainability style already used elsewhere in Codomon.

## Persistence

Persist results under the workspace `scans/` folder.

Suggested naming:

- `yyyyMMdd_HHmmss_techstack.json`

Suggested service methods, analogous to `RoslynScanService`:

- `ScanAsync(...)`
- `SaveAsync(...)`
- `LoadAsync(...)`
- `ListSavedScans(...)`

This allows:

- repeatable scans
- restoring latest results
- stale detection
- future history browsing

## UI Plan

This feature should be visually and conceptually distinct from Roslyn/source scan.

### New action

Add a main action/button/menu item:

- `Scan Tech Stack`

### New dialog

Create a dedicated dialog similar in structure to the Roslyn scan dialog, but focused on detected technologies.

Suggested sections:

- summary header
- grouped technologies by category
- optional per-project grouping/filter
- evidence/details panel

### Example summary text

- `14 technologies detected across 3 projects`
- `Top stack: ASP.NET Core, Entity Framework Core, Serilog, Docker`

### Evidence UX

When a technology is selected, show why it was detected:

- package reference found
- config file found
- infrastructure file found
- project SDK matched

This matters because the feature should be explainable, not magical.

## Overview Integration

Add Tech Stack as its own step in the Overview/learning pipeline.

Desired order:

1. Source scan
2. Tech stack
3. File summaries
4. Architecture synthesis
5. Code graph exploration

Suggested states:

- not scanned
- scanned
- stale
- blocked

Example detail text:

- `Detected 12 technologies across 4 projects`
- `Last scan: 2 hours ago`
- `Stale: source changed since tech stack scan`

## Relationship To Architecture Analysis

Tech stack scanning should come before architecture analysis.

Reasons:

- it is cheaper and faster
- results are deterministic
- it gives users immediate orientation
- the artifact can later improve prompt quality for architecture synthesis

### Important boundary

Phase 1 should not feed tech stack directly into `SystemModel` or the System Map.

Keep it as a workspace-level analysis artifact first.

Only in a later phase should architecture/hypothesis features optionally consume the most recent tech stack scan as additional context.

## Recommended Phase Breakdown

### Phase 1: detection + persistence + browsing

Build:

- tech stack models
- detection service
- JSON save/load/list methods
- view model
- dialog
- `Scan Tech Stack` entry point
- overview step

This phase should already be valuable.

### Phase 2: richer evidence

Add:

- Roslyn/code-level evidence
- smarter confidence scoring
- better per-project grouping
- more technology patterns

### Phase 3: downstream consumers

Allow:

- architecture synthesis prompts to include tech stack context
- maybe system detection to consume tech-stack signals carefully

But do this only after the standalone feature proves useful.

## Suggested Implementation Files

Likely additions:

- `Codomon.Desktop/Models/TechStackScanModels.cs`
- `Codomon.Desktop/Services/TechStackScanService.cs`
- `Codomon.Desktop/ViewModels/TechStackScanViewModel.cs`
- `Codomon.Desktop/Views/TechStackScanDialog.axaml`
- `Codomon.Desktop/Views/TechStackScanDialog.axaml.cs`

Likely updates:

- `Codomon.Desktop/Views/MainWindow.axaml.cs`
- `Codomon.Desktop/Views/MainWindow.axaml`
- overview/status helpers in the main window
- possibly workspace persistence helpers only if shared utilities are needed

## Design Constraints

- keep the feature explicit and standalone
- keep phase 1 deterministic and explainable
- prefer package/project/config evidence over inference
- persist results like other Codomon scan artifacts
- do not tightly couple this to System Map in the first implementation

## Non-Goals For Phase 1

Do not attempt:

- full NuGet dependency graph resolution
- transitive dependency analysis
- solution parsing
- language-agnostic stack detection
- automatic architecture conclusions from stack alone

Those can come later if real use cases justify the complexity.

## Definition Of Done For MVP

The feature is successful when a user can click `Scan Tech Stack` and Codomon can:

- inspect the workspace source tree
- detect a useful set of technologies from `.csproj` and known files
- show the results grouped by category and/or project
- explain why each technology was detected
- save and later restore the latest tech stack scan artifact
- show scan state in the Overview flow before architecture analysis
