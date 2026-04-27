# Codomon Bug Report

*Generated after reviewing the full codebase (April 2026)*

---

## BUG-001 — `AppLogger` is not thread-safe (crash risk)

**File:** `Models/AppLogger.cs`

`AppLogger.Append` writes to an `ObservableCollection<LogEntry>` which raises
`CollectionChanged` on the calling thread. The autosave timer in `MainViewModel`
uses `System.Timers.Timer`, whose `Elapsed` event fires on a thread-pool thread.
When the timer callback calls `AppLogger.Warn(…)` the collection mutation and its
notification happen off the UI thread. Avalonia's data-bound controls will throw a
cross-thread exception, or at minimum exhibit undefined behaviour.

**Minimal reproduction path:**
1. Open a workspace.
2. Wait 5 minutes for the autosave timer to fire.
3. `TryCreateAutosaveAsync` → `AppLogger.Warn` on the timer thread → crash.

**Fix:** Marshal `Append` to the UI thread (e.g. `Dispatcher.UIThread.Post`) or
replace `ObservableCollection` with a thread-safe alternative.

---

## BUG-002 — Double enumeration of `IEnumerable<ConnectionModel>` in `AddRoslynConnections`

**File:** `ViewModels/MainViewModel.cs`, method `AddRoslynConnections`

```csharp
foreach (var conn in connections)      // first iteration
    ...

AppLogger.Info($"… added {connections.Count()} connection(s).");   // second iteration
```

`connections` is declared as `IEnumerable<ConnectionModel>`. If the caller passes
a deferred LINQ query, the second evaluation re-enumerates the source. The log
message may therefore report a count that differs from the number of items actually
added. Calling `.Count()` on an already-exhausted `IEnumerator` also returns 0 for
some source types.

**Fix:** Materialise to a `List<>` at the top of the method, or store the count
before the loop.

---

## BUG-003 — Promoted Roslyn connections always have empty `FromId` / `ToId`

**File:** `ViewModels/RoslynScanViewModel.cs`, method `PromoteConnection`

```csharp
var connection = new ConnectionModel
{
    ...
    FromId = string.Empty,
    ToId   = string.Empty,
    ...
};
```

`FromId` and `ToId` are always set to `string.Empty` when a suggested connection is
promoted. `MainCanvasControl.DrawConnection` calls `ResolveConnectionPoint` for
both IDs; if both return `null` the line is silently skipped (no arrow is drawn on
the canvas). Promoted Roslyn connections therefore never appear visually and cannot
be manually edited to correct the endpoints through any existing UI.

**Fix:** Either map `FromClass` / `ToClass` to workspace IDs before promotion (if
such a mapping can be determined), or expose UI controls that let the user select
the source and target system/module before confirming the promotion.

---

## BUG-004 — `MainCanvasControl._decayTimer` leaks on every workspace reload

**File:** `Controls/MainCanvasControl.cs`

Each call to `SetupCanvas()` in `MainWindow` creates a brand-new
`MainCanvasControl`. The previous instance is detached from the visual tree, but
its `DispatcherTimer` (`_decayTimer`) is never stopped or disposed. The timer keeps
firing every 300 ms indefinitely, holds a strong reference to the old
`MainCanvasControl` (preventing GC), and keeps calling `InvalidateVisual()` on a
detached control.

**Fix:** Stop the timer when the control is detached from the visual tree (override
`OnDetachedFromVisualTree` and call `_decayTimer.Stop()`), or implement
`IDisposable` and dispose it when the canvas host is replaced.

---

## BUG-005 — `LogReplayViewModel._timer` leaks on workspace reload

**File:** `ViewModels/LogReplayViewModel.cs`

A `DispatcherTimer` is created on the first `Play()` call and is never disposed.
When a new workspace is loaded `MainViewModel` creates a new `LogReplayViewModel`;
the old VM's timer continues to hold a reference to the old `WorkspaceModel` and
keeps firing. Over multiple open/close cycles this accumulates leaked timers and
old workspace models.

**Fix:** Implement `IDisposable` on `LogReplayViewModel` and dispose the timer
there. Dispose the previous instance before replacing `_logReplay` in
`MainViewModel`.

---

## BUG-006 — `TimelineViewModel.Build` runs O(n × workspace-size) work on the UI thread

**File:** `ViewModels/TimelineViewModel.cs`, method `Build`

`Build` is called synchronously from the UI thread (via `RebuildTimeline` in
`MainWindow`) every time entries change or a profile is switched. For each log
entry `LogMatcher.Match` iterates over all workspace systems and modules. With
large log files (tens of thousands of lines) this freezes the UI.

**Fix:** Offload the bucket-building loop to a `Task.Run`, then marshal the result
back to the UI thread and update the observable properties.

---

## BUG-007 — `AutosaveService` and `LogImportService` use second-precision timestamps for unique names

**Files:** `Persistence/AutosaveService.cs`, `Services/LogImportService.cs`

Both services append a `DateTime.Now.ToString("yyyyMMdd_HHmmss")` suffix to
produce unique names. The precision is 1 second. If `CreateAutosaveAsync` or
`CopyToWorkspaceAsync` is called twice within the same second (e.g. by direct
calls, or rapid user interaction), the second call may silently overwrite the first
file / use the same directory.

**Fix:** Use millisecond precision (`"yyyyMMdd_HHmmssffff"`) or append a short
GUID suffix.

---

## BUG-008 — `LogParser.ParseDelimited` with an empty string delimiter splits into individual characters

**File:** `Services/LogParser.cs`, method `ParseDelimited`

When `options.DelimiterIsRegex` is `false` and `options.Delimiter` is `""`,
`line.Split(new[] { "" }, StringSplitOptions.None)` splits the string into an
array of individual characters (one per element). Each character is then processed
as a potential timestamp or log-level cell, producing completely wrong output.

This can happen if a user accidentally blanks the delimiter field in the import
wizard (the `EffectiveDelimiter` property returns `""` for the "custom" key when
`CustomDelimiter` is empty, and the Step 2 validator only guards against empty
values when the user tries to advance—not when the wizard is submitted directly).

**Fix:** Guard against an empty delimiter in `ParseDelimited` (fall back to tab, or
return the raw line unparsed). The existing wizard validator already prevents empty
custom delimiters on Next, but a defensive check in the service is still good
practice.

---

## BUG-009 — `PatternMatches` minimum-length guard uses a fixed threshold of 2

**File:** `Services/LogMatcher.cs`, method `PatternMatches`

```csharp
if (string.IsNullOrEmpty(pattern) || pattern.Length < 2) return false;
```

A single-character pattern is silently rejected without any warning or error, even
when the user has deliberately typed a one-character rule pattern in the Mapping
Rules dialog. The rule appears to be saved, and the rule editor shows no error, but
it silently never matches anything.

**Fix:** Either document this restriction clearly in the UI (rule editor), add an
inline validation error for patterns shorter than 2 characters, or remove the
length guard and let users create short patterns.

---

## BUG-010 — `RoslynScanService.BuildSuggestedConnections` over-counts calls when sites list is full

**File:** `Services/RoslynScanService.cs`, method `BuildSuggestedConnections`

```csharp
else if (entry.Sites.Count < 10 && !entry.Sites.Contains(callSite))
{
    entry.Sites.Add(callSite);
    callMap[key] = (entry.Count + 1, entry.Sites);
}
else
{
    callMap[key] = (entry.Count + 1, entry.Sites);   // count incremented even when site already in list
}
```

When the sites list already contains `callSite` (duplicate call-site string) or
when it is full (`Count >= 10`), `entry.Count` is still incremented. This means
`CallCount` diverges from the actual number of distinct call sites stored in
`CallSites`. The implication is that the sort order (`OrderByDescending(c =>
c.CallCount)`) is still directionally correct, but the displayed count shown to the
user in the "Suggested Connections" panel is inflated by duplicate/overflow calls.

**Fix:** Only increment `Count` when a new distinct call site is accepted, or
clearly document that `CallCount` represents raw call occurrences rather than
distinct sites.

---

## BUG-011 — `AppLogger.Entries` grows without bound

**File:** `Models/AppLogger.cs`

Every `Info`, `Debug`, `Warn`, and `Error` call appends to a static
`ObservableCollection<LogEntry>`. There is no upper limit. Over a long session (or
with a large imported log file being replayed) hundreds of thousands of entries
accumulate in memory, and the Dev Console's `ListBox` attempts to render them all.

**Fix:** Cap the collection at a reasonable maximum (e.g. 10 000 entries),
discarding the oldest entries when the cap is reached.

---

## BUG-012 — `RecentWorkspacesService` mixes UTC and local time

**File:** `Persistence/RecentWorkspacesService.cs`

`LastOpened` is stored as `DateTime.UtcNow` (UTC), but `LastModified` returns
`File.GetLastWriteTime(…)` which is local time. If these values were ever compared
or sorted together they would produce incorrect ordering. They are currently
displayed in separate UI contexts so the bug is latent, but a future refactor that
sorts by "most recently active" could trigger it.

**Fix:** Use a single convention throughout (prefer UTC), storing `LastModified`
via `File.GetLastWriteTimeUtc` and labelling displayed times consistently.

---

## BUG-013 — `SetupWizardViewModel` Step 2: existing workspace folder contents checked at validation time, not at creation time (TOCTOU)

**File:** `ViewModels/SetupWizardViewModel.cs`, method `ValidateCurrentStep`

The wizard checks that the chosen folder is empty at step-2 validation time. There
is a gap between that check and the actual workspace creation (`SaveAsync`). Another
process creating files in the folder during that window will not be detected, and
`WorkspaceSerializer.SaveAsync` will silently overwrite any existing files.

This is a minor TOCTOU (time-of-check/time-of-use) issue unlikely in practice but
worth noting.

**Fix:** Re-validate inside `WorkspaceSerializer.CreateNewAsync` and throw a
descriptive exception if the folder is no longer empty.

---

## BUG-014 — `TimelineControl` subscribes to `TimelineViewModel.PropertyChanged` but never unsubscribes

**File:** `Controls/TimelineControl.cs`

The constructor wires up `_vm.PropertyChanged += OnVmPropertyChanged`. When the
timeline is replaced (`SetupTimeline()` on workspace reload), the old
`TimelineControl` is detached from the visual tree but the event subscription
keeps the old control alive (the `TimelineViewModel` holds a reference to it).

**Fix:** Override `OnDetachedFromVisualTree` and unsubscribe from
`_vm.PropertyChanged` there.

---

## Summary Table

| ID       | Severity | Area                  | Summary                                              |
|----------|----------|-----------------------|------------------------------------------------------|
| BUG-001  | High     | Threading             | `AppLogger` writes ObservableCollection off UI thread |
| BUG-002  | Medium   | Logic                 | `AddRoslynConnections` double-enumerates IEnumerable |
| BUG-003  | High     | Feature gap           | Promoted Roslyn connections have no canvas endpoints  |
| BUG-004  | Medium   | Resource leak         | `_decayTimer` not stopped on canvas replacement      |
| BUG-005  | Medium   | Resource leak         | `LogReplayViewModel._timer` not disposed on reload   |
| BUG-006  | Medium   | Performance           | Timeline `Build` blocks UI thread on large logs      |
| BUG-007  | Low      | Race / correctness    | Second-precision timestamp names can collide         |
| BUG-008  | Medium   | Logic / crash         | Empty delimiter splits into individual characters    |
| BUG-009  | Low      | UX / silent failure   | 1-char patterns silently never match                 |
| BUG-010  | Low      | Data display          | `CallCount` inflated by duplicate/overflow calls     |
| BUG-011  | Low      | Memory                | `AppLogger.Entries` grows without bound              |
| BUG-012  | Low      | Data consistency      | Mixed UTC / local time in recent-workspaces metadata |
| BUG-013  | Low      | TOCTOU                | Workspace folder emptiness check is not atomic       |
| BUG-014  | Medium   | Resource leak         | `TimelineControl` never unsubscribes from VM events  |
