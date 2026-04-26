using Codomon.Desktop.Models;
using Codomon.Desktop.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Codomon.Desktop.ViewModels;

/// <summary>
/// Builds and exposes an aggregated day-timeline from imported log entries.
/// One row per visible System; buckets aggregate event counts in 5-minute slices.
/// </summary>
public class TimelineViewModel : INotifyPropertyChanged
{
    /// <summary>Granularity: one bucket per 5 minutes → 288 buckets / day.</summary>
    public static readonly TimeSpan BucketSize = TimeSpan.FromMinutes(5);

    private const int BucketsPerDay = (int)(24 * 60 / 5); // 288

    // ── Public state ──────────────────────────────────────────────────────────

    /// <summary>Ordered list of Systems that have at least one timestamped entry and are visible.</summary>
    public IReadOnlyList<SystemBoxModel> ActiveSystems => _activeSystems;

    /// <summary>Buckets per System, keyed by SystemId. Only populated after <see cref="Build"/>.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<TimelineBucket>> BucketsBySystem => _bucketsBySystem;

    /// <summary>Maximum bucket count across all systems and buckets (used for bar-height normalisation).</summary>
    public int MaxBucketCount { get; private set; }

    /// <summary>
    /// Time of day for the replay cursor (derived from the timestamp of the currently replayed entry).
    /// Null when no entry is active.
    /// </summary>
    public TimeSpan? ReplayCursorTime
    {
        get => _replayCursorTime;
        set { _replayCursorTime = value; OnPropertyChanged(); }
    }

    /// <summary>True when the loaded entries contain at least one timestamped entry.</summary>
    public bool HasTimestamps { get; private set; }

    // ── Private backing fields ────────────────────────────────────────────────

    private TimeSpan? _replayCursorTime;
    private readonly List<SystemBoxModel> _activeSystems = new();
    private readonly Dictionary<string, IReadOnlyList<TimelineBucket>> _bucketsBySystem = new();

    // ── Build ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds all timeline buckets from <paramref name="entries"/> matched against
    /// <paramref name="workspace"/>. Only Systems with <c>IsVisible = true</c> produce rows.
    /// </summary>
    public void Build(IReadOnlyList<LogEntryModel> entries, WorkspaceModel workspace)
    {
        _activeSystems.Clear();
        _bucketsBySystem.Clear();
        MaxBucketCount = 0;
        HasTimestamps = false;

        if (entries.Count == 0 || workspace.Systems.Count == 0)
        {
            OnPropertyChanged(nameof(ActiveSystems));
            OnPropertyChanged(nameof(BucketsBySystem));
            OnPropertyChanged(nameof(MaxBucketCount));
            OnPropertyChanged(nameof(HasTimestamps));
            return;
        }

        // Temporary mutable buckets: SystemId → slot index → bucket.
        var slotsBySystem = new Dictionary<string, Dictionary<int, MutableBucket>>();

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.Timestamp == null) continue;

            HasTimestamps = true;
            var match = LogMatcher.Match(entry, workspace);
            if (match.Strength == MatchStrength.None || match.System == null) continue;
            if (!match.System.IsVisible) continue;

            var timeOfDay = entry.Timestamp.Value.TimeOfDay;
            // Clamp to [0, 24 hours) in case of exotic timezone offsets.
            if (timeOfDay < TimeSpan.Zero) timeOfDay = TimeSpan.Zero;
            if (timeOfDay >= TimeSpan.FromHours(24)) timeOfDay = TimeSpan.FromHours(24) - TimeSpan.FromSeconds(1);

            int slot = (int)(timeOfDay.TotalMinutes / BucketSize.TotalMinutes);
            slot = Math.Clamp(slot, 0, BucketsPerDay - 1);

            var sysId = match.System.Id;
            if (!slotsBySystem.TryGetValue(sysId, out var slots))
            {
                slots = new Dictionary<int, MutableBucket>();
                slotsBySystem[sysId] = slots;
            }

            if (!slots.TryGetValue(slot, out var bucket))
            {
                bucket = new MutableBucket(slot);
                slots[slot] = bucket;
            }

            bucket.Count++;
            bucket.EntryIds.Add(i);
        }

        // Convert to immutable model; build ActiveSystems list.
        foreach (var sys in workspace.Systems)
        {
            if (!sys.IsVisible) continue;
            if (!slotsBySystem.TryGetValue(sys.Id, out var slots)) continue;

            var buckets = new List<TimelineBucket>(slots.Count);
            foreach (var kv in slots.OrderBy(x => x.Key))
            {
                var s = kv.Key;
                var mb = kv.Value;
                var start = TimeSpan.FromMinutes(s * BucketSize.TotalMinutes);
                buckets.Add(new TimelineBucket
                {
                    SystemId = sys.Id,
                    StartTime = start,
                    EndTime = start + BucketSize,
                    Count = mb.Count,
                    MatchingLogEntryIds = mb.EntryIds
                });

                if (mb.Count > MaxBucketCount)
                    MaxBucketCount = mb.Count;
            }

            _bucketsBySystem[sys.Id] = buckets;
            _activeSystems.Add(sys);
        }

        OnPropertyChanged(nameof(ActiveSystems));
        OnPropertyChanged(nameof(BucketsBySystem));
        OnPropertyChanged(nameof(MaxBucketCount));
        OnPropertyChanged(nameof(HasTimestamps));
    }

    // ── INotifyPropertyChanged ────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // ── Helper ────────────────────────────────────────────────────────────────

    private sealed class MutableBucket
    {
        public int Slot { get; }
        public int Count { get; set; }
        public List<int> EntryIds { get; } = new();

        public MutableBucket(int slot) => Slot = slot;
    }
}
