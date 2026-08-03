using EOS.Contracts;

namespace EOS.Resources;

/// <summary>
/// Resource-Management-Specification-v1.0 §17: Capacity Planning. Computes Safe/Warning/
/// Critical/Emergency (§17.1–§17.4) from a measured value against configured boundaries
/// (§17.5), and publishes <c>ResourceThresholdCrossed</c> (§20) only on a genuine tier
/// transition — the first-ever classification for a dimension establishes its initial resting
/// state, not a "crossing".
/// </summary>
public sealed class CapacityManager(CapacityThresholds thresholds, IResourceThresholdCrossedEventPublisher eventPublisher)
{
    private readonly Dictionary<ResourceType, CapacityTier> _lastTier = [];
    private readonly Lock _lock = new();

    public CapacityTier ComputeTier(ResourceType resourceType, double measuredValue)
    {
        var boundaries = GetBoundaries(resourceType);
        var tier = Classify(measuredValue, boundaries);

        // The lock protects only the _lastTier read/write (state); the event publish itself
        // happens after the lock is released, so a slow or re-entrant subscriber can never
        // block another thread's tier computation or deadlock against this same lock.
        bool shouldPublish;
        lock (_lock)
        {
            shouldPublish = _lastTier.TryGetValue(resourceType, out var previousTier) && previousTier != tier;
            _lastTier[resourceType] = tier;
        }

        if (shouldPublish)
        {
            eventPublisher.PublishResourceThresholdCrossed(resourceType, tier);
        }

        return tier;
    }

    // §17.4: "the reserved-headroom boundary... that must never be crossed in practice" — the
    // Emergency comparison is checked first, so a measured value at or above it always
    // classifies as Emergency, never silently absorbed into a lower tier.
    private static CapacityTier Classify(double measuredValue, ResourceTierBoundaries boundaries)
    {
        if (measuredValue >= boundaries.Emergency)
        {
            return CapacityTier.Emergency;
        }

        if (measuredValue >= boundaries.Critical)
        {
            return CapacityTier.Critical;
        }

        if (measuredValue >= boundaries.Warning)
        {
            return CapacityTier.Warning;
        }

        return CapacityTier.Safe;
    }

    private ResourceTierBoundaries GetBoundaries(ResourceType resourceType) => resourceType switch
    {
        ResourceType.Cpu => thresholds.Cpu,
        ResourceType.Ram => thresholds.Ram,
        ResourceType.Disk => thresholds.Disk,
        ResourceType.ModelUsage => thresholds.ModelUsage,
        ResourceType.QueueLength => thresholds.QueueLength,
        ResourceType.BackgroundTasks => thresholds.BackgroundTasks,
        ResourceType.CacheUsage => thresholds.CacheUsage,
        _ => throw new ArgumentOutOfRangeException(nameof(resourceType), resourceType, "Unsupported ResourceType."),
    };
}
