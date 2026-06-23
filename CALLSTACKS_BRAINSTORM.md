# Callstacks Brainstorm

## Rough idea

Codomon could help users study an app by recording what happens during focused Start → Middle → End (SME) test runs. The user performs one user story at a time, signals when the story starts and ends, and Codomon stores the captured runtime evidence for that run.

The key evidence for this brainstorm is callstack data, paired with errors and timestamps, so Codomon can later help connect user actions, runtime behavior, and failures.

## Assumptions

- The user can run individual SME tests through the app they want to study.
- The user can signal when each SME starts and ends.
- Each SME run is stored separately, for example in a folder like `sme0001/`.
- Runtime errors can be captured with timestamps.
- Runtime callstacks can be sampled during the run.
- Callstack sampling frequency should be tweakable, because capturing every callstack may be too noisy or expensive.

## Possible capture paths

### Codomon attaches to the app

Codomon could connect directly to the target app while the user performs an SME. During the run, Codomon samples runtime callstacks and writes them into the SME folder.

Questions:

- Which runtimes can Codomon attach to directly?
- Does this require debug symbols, special launch settings, or elevated permissions?
- Can Codomon attach to an already-running process, or does it need to launch the app?
- How much performance overhead is acceptable during a test run?

### Visual Studio or VS Code integration

Visual Studio, VS Code, or an extension could provide callstack data while the target app is being debugged. Codomon might consume exported debugger data instead of attaching directly.

Questions:

- Can IDE debugger APIs expose callstacks continuously or on demand?
- Can the IDE mark SME start and end events?
- Would this work only for developer workflows, or could it also work for non-developer users?
- How portable is this across languages, runtimes, and platforms?

### External profiler, tracer, or diagnostic tool

Another tool could capture stack samples, traces, exceptions, or profiling data. Codomon could import or normalize that output.

Questions:

- Which existing tools already sample callstacks well?
- Can they export simple CSV or structured trace data?
- Can they be automated around SME start/end boundaries?
- Is their output stable enough for Codomon to rely on?

## Suggested SME folder shape

```text
sme0001/
  errors.csv
  callstacks.csv
  metadata.json
```

### `errors.csv`

Possible columns:

- `timestamp`
- `severity`
- `source`
- `message`
- `exception_type`
- `stack_id`

### `callstacks.csv`

Possible columns:

- `timestamp`
- `thread_id`
- `thread_name`
- `sample_id`
- `frame_index`
- `function`
- `file`
- `line`
- `module`

This row-per-frame shape is verbose, but easy to inspect and filter. A later version could split stack samples and stack frames into separate files to reduce duplication.

### `metadata.json`

Possible fields:

- SME id
- Start timestamp
- End timestamp
- Target app name
- Target process id
- Runtime or language
- Capture method
- Sampling interval
- User-provided story name or notes

## Things Codomon could learn from the data

- Which code paths appear during a specific user story.
- Which callstacks are common across many SMEs.
- Which callstacks are unique to one SME.
- Which errors happen near specific stack samples.
- Which user stories touch the same functions, modules, or layers.
- Whether a suspected feature area is actually exercised by the SME.

## Open questions

- Should Codomon optimize for debugger-style exact callstacks or profiler-style sampled callstacks?
- Should errors and callstacks share a common event timeline format?
- How should Codomon handle missing symbols, native frames, async frames, and generated code?
- Should the first version support only one runtime or be import-first and tool-agnostic?
- How should noisy framework/library frames be filtered?
- Can users label important moments inside an SME, not just start and end?
- Should Codomon store raw tool output alongside normalized CSV files?

## Possible first experiment

Start with an import-first prototype:

1. Let the user create an SME folder.
2. Let the user provide `errors.csv` and `callstacks.csv`.
3. Teach Codomon to load the files and display a timeline for one SME.
4. Add filtering by timestamp, thread, function, module, and error proximity.
5. Later, add direct capture through Codomon, IDE integration, or external tooling.

This keeps the first version flexible while the capture mechanism is still uncertain.
