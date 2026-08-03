namespace EOS.Resources;

/// <summary>
/// One <see cref="ResourceTierBoundaries"/> per §18.2 monitored dimension (§17.5: "All four
/// tiers, per resource type, are defined in Thresholds.json").
/// </summary>
public sealed record CapacityThresholds(
    ResourceTierBoundaries Cpu,
    ResourceTierBoundaries Ram,
    ResourceTierBoundaries Disk,
    ResourceTierBoundaries ModelUsage,
    ResourceTierBoundaries QueueLength,
    ResourceTierBoundaries BackgroundTasks,
    ResourceTierBoundaries CacheUsage);
