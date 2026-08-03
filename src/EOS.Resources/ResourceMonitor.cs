using EOS.Contracts;

namespace EOS.Resources;

/// <summary>
/// Resource-Management-Specification-v1.0 §18: Monitoring. Samples CPU/RAM/Disk/Cache Usage
/// directly from the OS (self-contained, no cross-subsystem dependency). Queue Length,
/// Background Tasks, and Model Usage have no direct data source reachable under Constitution
/// Part 1 §1.2's dependency table (<c>EOS.Resources</c>: "EOS.Contracts, EOS.SDK" only) — per
/// §20.1's own Consumed Events text and §21.2, these three are observed via the Event Catalog
/// instead (<see cref="RecordTaskStarted"/>/<see cref="RecordTaskCompleted"/>/
/// <see cref="RecordTaskBlocked"/>/<see cref="RecordInferenceRouted"/>/
/// <see cref="RecordInferenceCompleted"/>, called by <c>Program.cs</c>'s event subscriptions —
/// WP-021 Implementation Plan Decision D6, mirroring <c>AutomaticConsolidationTriggerHandlers</c>).
///
/// §18.1 Sampling Model: "sampled at a bounded cadence (configurable, Thresholds.json), never
/// continuously instrumented in a way that would itself compete for the CPU it measures." No
/// background timer/hosted service is used (none exists anywhere in this codebase) — sampling
/// is on-demand (pull-based), throttled per <see cref="ResourceType"/> by
/// <paramref name="samplingIntervalSeconds"/>: a fresh OS-level read happens only if the elapsed
/// time since that dimension's last sample is at or beyond the configured interval; otherwise
/// the cached value is returned.
/// </summary>
public sealed class ResourceMonitor(int samplingIntervalSeconds, int modelIdleResidencyTimeoutSeconds, IModelLoadedEventPublisher modelLoadedEventPublisher, IModelUnloadedEventPublisher modelUnloadedEventPublisher)
{
    private readonly Dictionary<ResourceType, (DateTimeOffset SampledAt, double Value)> _cache = [];
    private readonly Lock _lock = new();

    private int _activeTaskCount;
    private int _activeInferenceCount;

    private readonly Dictionary<string, (ModelResidencyState State, double? RamFootprintMegabytes, DateTimeOffset LastUsedAt)> _modelResidency = [];

    public double Sample(ResourceType resourceType)
    {
        lock (_lock)
        {
            // §20.1: Queue Length/Background Tasks/Model Usage are observed via the Event
            // Catalog, not OS-level sampled (Decision D6) — the elapsed-time throttle exists
            // only to bound the cost of real OS-level reads (§18.1); an in-memory counter has
            // no such cost, so these three dimensions always return the current live count,
            // never a value cached from before the most recent Record* call.
            if (IsEventDriven(resourceType))
            {
                return MeasureNow(resourceType);
            }

            var now = DateTimeOffset.UtcNow;
            if (_cache.TryGetValue(resourceType, out var cached)
                && (now - cached.SampledAt).TotalSeconds < samplingIntervalSeconds)
            {
                return cached.Value;
            }

            var value = MeasureNow(resourceType);
            _cache[resourceType] = (now, value);
            return value;
        }
    }

    private static bool IsEventDriven(ResourceType resourceType) =>
        resourceType is ResourceType.QueueLength or ResourceType.BackgroundTasks or ResourceType.ModelUsage;

    private double MeasureNow(ResourceType resourceType) => resourceType switch
    {
        ResourceType.Cpu => MeasureCpuUtilizationPercent(),
        ResourceType.Ram => MeasureRamUsedMegabytes(),
        ResourceType.Disk => MeasureDiskUsedMegabytes(),
        ResourceType.CacheUsage => MeasureCacheUsagePercent(),
        ResourceType.QueueLength or ResourceType.BackgroundTasks => _activeTaskCount,
        ResourceType.ModelUsage => _activeInferenceCount,
        _ => throw new ArgumentOutOfRangeException(nameof(resourceType), resourceType, "Unsupported ResourceType."),
    };

    // §18.2: "CPU | Utilization %, per-core and aggregate". Linux /proc/stat: aggregate CPU
    // utilization computed as the delta between this read and the previous cached raw counters
    // (avoids an artificial blocking sleep to observe a delta within a single call). No
    // RuntimeIdentifier restricts this repository to Linux, so on any other OS this honestly
    // reports 0% rather than throwing from a missing /proc filesystem, matching the existing
    // "no real producer yet" pattern used by MeasureCacheUsagePercent.
    private double MeasureCpuUtilizationPercent()
    {
        if (!OperatingSystem.IsLinux())
        {
            return 0.0;
        }

        var (idle, total) = ReadProcStatCpuTotals();

        if (_previousCpuTotals is { } previous && total > previous.Total)
        {
            var idleDelta = idle - previous.Idle;
            var totalDelta = total - previous.Total;
            _previousCpuTotals = (idle, total);
            return totalDelta <= 0 ? 0.0 : Math.Clamp(100.0 * (1.0 - (double)idleDelta / totalDelta), 0.0, 100.0);
        }

        _previousCpuTotals = (idle, total);
        return 0.0;
    }

    private (long Idle, long Total)? _previousCpuTotals;

    private static (long Idle, long Total) ReadProcStatCpuTotals()
    {
        var line = File.ReadLines("/proc/stat").First(l => l.StartsWith("cpu ", StringComparison.Ordinal));
        var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1)
            .Select(long.Parse).ToArray();

        // user, nice, system, idle, iowait, irq, softirq, steal (guest/guest_nice excluded from total, per convention)
        var idle = fields[3] + fields[4];
        var total = fields.Take(8).Sum();
        return (idle, total);
    }

    // §18.2: "RAM | Utilization, available headroom". Linux /proc/meminfo: MemTotal - MemAvailable.
    // Same non-Linux fallback rationale as MeasureCpuUtilizationPercent above.
    private static double MeasureRamUsedMegabytes()
    {
        if (!OperatingSystem.IsLinux())
        {
            return 0.0;
        }

        var lines = File.ReadAllLines("/proc/meminfo");
        var totalKb = ParseMeminfoValueKb(lines, "MemTotal:");
        var availableKb = ParseMeminfoValueKb(lines, "MemAvailable:");
        return (totalKb - availableKb) / 1024.0;
    }

    private static long ParseMeminfoValueKb(string[] lines, string key)
    {
        var line = lines.First(l => l.StartsWith(key, StringComparison.Ordinal));
        var value = line[key.Length..].Trim().Split(' ')[0];
        return long.Parse(value);
    }

    // §18.2: "Disk | Free space, I/O contention". Used space on the root filesystem, matching
    // the existing DiskCeilingMegabytes' own used-space semantics (Constitution Part 10). Same
    // non-Linux fallback rationale as MeasureCpuUtilizationPercent above — "/" is not a valid
    // drive identifier on every OS.
    private static double MeasureDiskUsedMegabytes()
    {
        if (!OperatingSystem.IsLinux())
        {
            return 0.0;
        }

        var root = new DriveInfo("/");
        return (root.TotalSize - root.AvailableFreeSpace) / (1024.0 * 1024.0);
    }

    // §18.2: "Cache Usage | RAM/Disk cache-tier occupancy vs. configured ceiling (§12.2, §13.4)".
    // No cache-tier store exists in this repository yet (Memory-Management-Specification-v1.0's
    // cache tiers are not implemented as a distinct, separately-measurable store) — honestly
    // reports 0% occupancy rather than fabricating a value, consistent with the "structurally
    // ready, no real producer yet" pattern already established for other WPs' stubs.
    private static double MeasureCacheUsagePercent() => 0.0;

    // §20.1 Consumed Events — Decision D6. Called by Program.cs's EventMediator subscriptions.
    public void RecordTaskStarted(Guid taskId)
    {
        lock (_lock)
        {
            _activeTaskCount++;
        }
    }

    public void RecordTaskCompleted(Guid taskId)
    {
        lock (_lock)
        {
            _activeTaskCount = Math.Max(0, _activeTaskCount - 1);
        }
    }

    public void RecordTaskBlocked(Guid taskId)
    {
        lock (_lock)
        {
            _activeTaskCount = Math.Max(0, _activeTaskCount - 1);
        }
    }

    public void RecordInferenceRouted(string model)
    {
        bool firstObservation;

        lock (_lock)
        {
            _activeInferenceCount++;

            var now = DateTimeOffset.UtcNow;
            firstObservation = !_modelResidency.TryGetValue(model, out var existing) || existing.State == ModelResidencyState.Unloaded;

            if (firstObservation)
            {
                // §14.1: "reactively (on first infer()/embed() request requiring it)". No
                // separate load-started/load-finished signal exists anywhere in this repository
                // or any frozen document (AI-Provider-Layer-Specification-v1.0's own
                // InferenceRouted/InferenceCompleted, §19, carry no such marker, and the model is
                // already resident by the time InferenceRouted fires) — there is no legal instant
                // to bookend a real "before loading"/"after loading" RAM delta. Reporting
                // <see langword="null"/> is the honest signal (WP-022 Recovery Plan Slice
                // R3/Finding F4), matching the same "no real producer yet" precedent already
                // established by <see cref="MeasureCacheUsagePercent"/>, rather than fabricating
                // a delta that would always be ~0 regardless of the model's real footprint.
                _modelResidency[model] = (ModelResidencyState.Resident, null, now);
            }
            else
            {
                _modelResidency[model] = (ModelResidencyState.Resident, existing.RamFootprintMegabytes, now);
            }
        }

        if (firstObservation)
        {
            // §20's ModelLoaded payload (model_id, ram_footprint) requires a non-nullable value;
            // 0.0 is the disclosed "unmeasurable" sentinel (WP-022 Recovery Plan Slice
            // R3/Finding F4) — the model becoming Resident is itself real and worth publishing,
            // even though its footprint cannot be legally measured.
            modelLoadedEventPublisher.PublishModelLoaded(model, 0.0);
        }
    }

    public void RecordInferenceCompleted(string model)
    {
        lock (_lock)
        {
            _activeInferenceCount = Math.Max(0, _activeInferenceCount - 1);
        }
    }

    /// <summary>
    /// §21.1/§14.3: read-only residency signal. Idle-residency eviction (§14.2) is checked here,
    /// pull-based per call — matching this same class's existing Sampling Model (§18.1) rather
    /// than a background timer.
    /// </summary>
    public ModelResidencyStatus GetModelResidency(string modelId)
    {
        bool evicted;
        double? footprintAtEviction = null;

        lock (_lock)
        {
            if (!_modelResidency.TryGetValue(modelId, out var current))
            {
                return new ModelResidencyStatus(modelId, ModelResidencyState.Unloaded, null);
            }

            evicted = current.State == ModelResidencyState.Resident
                && (DateTimeOffset.UtcNow - current.LastUsedAt).TotalSeconds >= modelIdleResidencyTimeoutSeconds;

            if (evicted)
            {
                footprintAtEviction = current.RamFootprintMegabytes;
                _modelResidency[modelId] = (ModelResidencyState.Unloaded, current.RamFootprintMegabytes, current.LastUsedAt);
            }
            else
            {
                current = _modelResidency[modelId];
                return new ModelResidencyStatus(modelId, current.State, current.RamFootprintMegabytes);
            }
        }

        modelUnloadedEventPublisher.PublishModelUnloaded(modelId, footprintAtEviction ?? 0.0);
        return new ModelResidencyStatus(modelId, ModelResidencyState.Unloaded, footprintAtEviction);
    }
}
