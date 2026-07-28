using System.ComponentModel.DataAnnotations;

namespace EOS.SharedKernel.Configuration;

public sealed record ThresholdsOptions
{
    [Range(0, 100)]
    public required int ResourceWarningPercent { get; init; }

    [Range(0, 100)]
    public required int ResourceCriticalPercent { get; init; }

    [Range(1, int.MaxValue)]
    public required int ProviderFailureThreshold { get; init; }

    [Range(1, int.MaxValue)]
    public required int ProviderRecoveryProbeIntervalSeconds { get; init; }

    [Range(1, 2_147_483)]
    public required int InferenceTimeoutSeconds { get; init; }

    // Resource Protection ceilings (Protection-Layer-Specification-v1.0 §16). Structurally
    // ready, stub values only — real enforcement awaits Resource Management (WP-021).
    [Range(0, 100)]
    public required int CpuCeilingPercent { get; init; }

    [Range(1, int.MaxValue)]
    public required int RamCeilingMegabytes { get; init; }

    [Range(1, int.MaxValue)]
    public required int DiskCeilingMegabytes { get; init; }

    [Range(1, int.MaxValue)]
    public required int ModelUsageCeilingTokens { get; init; }

    [Range(1, int.MaxValue)]
    public required int ContextSizeCeilingTokens { get; init; }

    [Range(1, int.MaxValue)]
    public required int BackgroundTasksCeilingCount { get; init; }
}
