# Call Stacks Brainstorm

## Rough idea

Codomon could help users study an app by recording what happens during focused Start → Middle → End (SME) test runs. In this note, SME specifically means Start → Middle → End, not subject matter expert. The user performs one user story at a time, signals when the story starts and ends, and Codomon stores the captured runtime evidence for that run.

The key evidence for this brainstorm is call stack data, paired with errors and timestamps, so Codomon can later help connect user actions, runtime behavior, and failures.

## Assumptions

- The user can run individual SME tests through the app they want to study.
- The user can signal when each SME starts and ends.
- Each SME run is stored separately, for example in a folder like `sme0001/`.
- Runtime errors can be captured with timestamps.
- Runtime call stacks can be sampled during the run.
- Call stack sampling frequency should be tweakable, because capturing every call stack may be too noisy or expensive.

## Possible capture paths

### Codomon attaches to the app

Codomon could connect directly to the target app while the user performs an SME. During the run, Codomon samples runtime call stacks and writes them into the SME folder.

Questions:

- Which runtimes can Codomon attach to directly?
- Does this require debug symbols, special launch settings, or elevated permissions?
- Can Codomon attach to an already-running process, or does it need to launch the app?
- How much performance overhead is acceptable during a test run?

### Visual Studio or VS Code integration

Visual Studio, VS Code, or an extension could provide call stack data while the target app is being debugged. Codomon might consume exported debugger data instead of attaching directly.

Questions:

- Can IDE debugger APIs expose call stacks continuously or on demand?
- Can the IDE mark SME start and end events?
- Would this work only for developer workflows, or could it also work for non-developer users?
- How portable is this across languages, runtimes, and platforms?

### External profiler, tracer, or diagnostic tool

Another tool could capture stack samples, traces, exceptions, or profiling data. Codomon could import or normalize that output.

Questions:

- Which existing tools already sample call stacks well?
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
- Which call stacks are common across many SMEs.
- Which call stacks are unique to one SME.
- Which errors happen near specific stack samples.
- Which user stories touch the same functions, modules, or layers.
- Whether a suspected feature area is actually exercised by the SME.

## Open questions

- Should Codomon optimize for debugger-style exact call stacks or profiler-style sampled call stacks?
- Should errors and call stacks share a common event timeline format?
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


---

Feedback from GPT 5.5:


Feedback on CALLSTACKS_BRAINSTORM.md

Overall, I like the structure: it separates the goal, assumptions, capture mechanisms, storage shape, possible insights, and first experiment. The strongest part is the “import-first prototype” direction: it avoids overcommitting to a debugger/profiler integration before Codomon knows what data shape is actually useful.

The main design choice I’d challenge is this: the brainstorm currently treats call stacks as the central evidence, but for understanding user stories, call stacks alone may be too low-level unless they are paired with higher-level timeline events, labels, errors, screenshots, logs, or user annotations.
1. Define the “SME run” as a timeline first, not as separate files first

The proposed folder shape is simple and good:

sme0001/
  errors.csv
  callstacks.csv
  metadata.json

But conceptually, Codomon may benefit from treating an SME as a timeline of events, where call stack samples, errors, labels, logs, screenshots, and user notes are different event types on the same clock.

Right now, errors.csv and callstacks.csv are parallel artifacts connected mostly by timestamp and optional stack_id. That may work initially, but timeline correlation could get awkward as soon as you add:

    user marks: “clicked Save”

    screenshots

    logs

    network calls

    database queries

    process start/stop

    thread changes

    async continuation hops

    profiler/import metadata

Suggested taskModel SME data around a shared event timeline
2. Sampling call stacks may miss the exact moment users care about

Profiler-style sampling is practical, but it has an important pitfall: if the user action is brief, the relevant stack may never appear in the sample window.

For example:

    user clicks a button

    handler runs for 15 ms

    sampling interval is 100 ms

    Codomon records nothing useful from that handler

This means sampled call stacks are better for answering “what code was active during this SME?” than “what exact code handled this user action?”

The note already asks whether Codomon should optimize for debugger-style exact call stacks or profiler-style sampled call stacks. I would make this distinction more explicit:

    sampled stacks: lower overhead, approximate, good for long-running behavior

    instrumented events/tracing: more exact, more setup, better for user action boundaries

    exception stacks: exact for failures, but only when failures happen

    debugger pause/tracepoints: powerful but developer-oriented

Suggested taskDocument the limits of sampled call stacks
3. “Common” and “unique” stacks need normalization rules

The “Things Codomon could learn” section mentions:

    common stacks across many SMEs

    stacks unique to one SME

    stories touching same functions/modules/layers

That is promising, but raw stack comparison is tricky. Two stacks can represent the same logical behavior while differing because of:

    line numbers

    generated code

    async machinery

    framework wrappers

    lambdas/anonymous functions

    JIT/native frames

    different thread entry points

    dynamic dispatch

    version changes

Codomon probably needs a concept of a normalized frame identity and maybe a stack fingerprint.

For example, a normalized frame key might be:

module + namespace/class + function

rather than:

file + line + exact symbol string

And Codomon may need multiple matching levels:

    exact stack match

    top N app frames match

    any shared frame

    shared module/package/layer

    shared error stack

Suggested taskDefine stack normalization and matching levels
4. The row-per-frame CSV is easy to inspect but may be hard to query later

The proposed callstacks.csv shape is good for a first prototype:

timestamp
thread_id
thread_name
sample_id
frame_index
function
file
line
module

However, as the note already says, it is verbose. More importantly, it may make stack-level operations harder:

    grouping all frames from one sample

    deduplicating identical stacks

    computing “top stack patterns”

    linking errors to nearest samples

    storing import provenance

    displaying collapsed stack trees/flamegraph-like views

I’d consider an early split into:

samples.csv
frames.csv
stacks.csv

or a JSONL event format for import flexibility.

A pragmatic compromise:

callstack_samples.csv
callstack_frames.csv

Where callstack_samples.csv has one row per sample and callstack_frames.csv has one row per frame.
Suggested taskConsider splitting call stack samples from frames
5. Start/Middle/End may not be enough; user marks are likely important

The SME framing is strong because it gives Codomon a bounded run. But the actual “Middle” may contain several meaningful moments:

    opened screen

    clicked button

    submitted form

    error appeared

    retried

    navigated back

    background sync completed

The open question already asks whether users can label important moments inside an SME. I’d promote this to a core part of the feature.

A timeline with user labels would make call stacks far more useful. Instead of asking:

    What happened in this 3-minute run?

Codomon could ask:

    What stacks/errors/logs happened within 2 seconds of “clicked Save”?

Suggested taskPromote in-run user labels to a first-class SME concept
6. The capture strategy should probably be “import-first, then adapters”

The “Possible first experiment” section is the best direction in the document. I would lean hard into that.

Direct attachment, IDE integration, and external profilers are all valuable but very different implementation paths. If Codomon defines a stable import format first, every capture method can become an adapter:

Visual Studio debugger export -> Codomon SME format
VS Code extension export       -> Codomon SME format
perf/dotnet-trace/etc.         -> Codomon SME format
manual CSV                     -> Codomon SME format

This also lets Codomon work with partial data. For example, an SME might have only errors and notes, no stacks. Another might have stacks but no symbols.
Suggested taskFrame capture methods as adapters into a stable SME import format
7. Symbol quality and privacy/security deserve more emphasis

Call stacks can expose sensitive details:

    local filesystem paths

    usernames

    proprietary module names

    customer identifiers in generated method names or logs

    source paths

    exception messages containing secrets

    arguments, if any tooling captures them

The current brainstorm mentions missing symbols and native frames, but not data sensitivity. If Codomon stores these SME folders, it should consider:

    redaction

    local-only storage

    opt-in export

    path normalization

    project-relative paths

    “do not collect arguments/locals” default

    warning users before sharing SME folders

Suggested taskAdd privacy and symbol-quality considerations
8. Codomon should distinguish app frames from framework/tooling frames

The brainstorm asks how noisy framework/library frames should be filtered. This is central enough that I’d make it part of the data model.

Users probably care most about:

    app code

    dependencies they own

    third-party library boundaries

    framework entry points only when relevant

Each frame could eventually have a classification:

app
dependency
framework
runtime
generated
native
unknown

This would make the UI much better. For example:

    show “user story touched these app functions”

    collapse framework frames by default

    keep raw stack available for debugging

    compute fingerprints using app frames first

Suggested taskAdd frame classification to the call stack model
9. The feature needs confidence language

One subtle pitfall: users may overtrust the output.

If Codomon says “this SME touched function X,” that is useful. But if Codomon says “this SME did not touch function Y,” that may be false if sampling missed it or symbols were incomplete.

I’d suggest making the UI and data model use confidence language:

    “observed during this run”

    “not observed”

    “near this error”

    “possibly related”

    “high confidence: exception stack”

    “medium confidence: repeated sampled stacks”

    “low confidence: single sample near user mark”

Suggested taskAdd confidence semantics for runtime evidence
10. Suggested next shape for the idea

If I were evolving the brainstorm, I’d restructure it around this hierarchy:

SME Run
  metadata
  timeline events
    user marks
    errors
    call stack samples
    logs
    screenshots, optional later
  imported raw artifacts
  normalized views
    functions touched
    modules touched
    errors near user marks
    common/unique stacks

This keeps the current idea intact, but makes call stacks one kind of evidence inside a broader study session.

My recommended first prototype would be:

    Create/load an SME folder.

    Load metadata.json.

    Load a simple event timeline.

    Import call stack samples from CSV.

    Import errors from CSV.

    Show a timeline with filters.

    Let the user add labels/marks.

    Correlate errors and stack samples around labels.

That gives Codomon a useful workflow without requiring a debugger integration yet.
Testing

⚠️ pwd; rg --files -g 'CALLSTACKS_BRAINSTORM.md' -g 'AGENTS.md'; sed -n '1,240p' CALLSTACKS_BRAINSTORM.md — Static inspection only; no tests or code execution were run per read-only QA constraints.