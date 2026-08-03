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

        lock (_lock)
        {
            if (_lastTier.TryGetValue(resourceType, out var previousTier) && previousTier != tier)
            {
                eventPublisher.PublishResourceThresholdCrossed(resourceType, tier);
            }

            _lastTier[resourceType] = tier;
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
