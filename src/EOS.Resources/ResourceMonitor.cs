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
public sealed class ResourceMonitor(int samplingIntervalSeconds)
{
    private readonly Dictionary<ResourceType, (DateTimeOffset SampledAt, double Value)> _cache = [];
    private readonly Lock _lock = new();

    private int _activeTaskCount;
    private int _activeInferenceCount;

    public double Sample(ResourceType resourceType)
    {
        lock (_lock)
        {
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

    private double MeasureNow(ResourceType resourceType) => resourceType switch
    {
        ResourceType.Cpu => MeasureCpuUtilizationPercent(),
        ResourceType.Ram => MeasureRamUsedMegabytes(),
        ResourceType.Disk => MeasureDiskUsedMegabytes(),
        ResourceType.CacheUsage => MeasureCacheUsagePercent(),
        // §20.1: observed via the Event Catalog, not sampled directly (Decision D6) — the
        // real-time recorded count is returned as-is; sampling/throttling does not apply to it.
        ResourceType.QueueLength or ResourceType.BackgroundTasks => _activeTaskCount,
        ResourceType.ModelUsage => _activeInferenceCount,
        _ => throw new ArgumentOutOfRangeException(nameof(resourceType), resourceType, "Unsupported ResourceType."),
    };

    // §18.2: "CPU | Utilization %, per-core and aggregate". Linux /proc/stat: aggregate CPU
    // utilization computed as the delta between this read and the previous cached raw counters
    // (avoids an artificial blocking sleep to observe a delta within a single call).
    private double MeasureCpuUtilizationPercent()
    {
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
    private static double MeasureRamUsedMegabytes()
    {
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
    // the existing DiskCeilingMegabytes' own used-space semantics (Constitution Part 10).
    private static double MeasureDiskUsedMegabytes()
    {
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
        lock (_lock)
        {
            _activeInferenceCount++;
        }
    }

    public void RecordInferenceCompleted(string model)
    {
        lock (_lock)
        {
            _activeInferenceCount = Math.Max(0, _activeInferenceCount - 1);
        }
    }
}
