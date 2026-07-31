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

    // Memory Retrieval Ranking weights (Memory-Management-Specification-v1.0 §19.1). The
    // specification names Thresholds.json explicitly as their configuration home.
    [Range(0.0, 1.0)]
    public required double RankingVectorSimilarityWeight { get; init; }

    [Range(0.0, 1.0)]
    public required double RankingRecencyWeight { get; init; }

    [Range(0.0, 1.0)]
    public required double RankingDomainMatchWeight { get; init; }

    [Range(0.0, 1.0)]
    public required double RankingAccessFrequencyWeight { get; init; }

    // Memory Expiration (Memory-Management-Specification-v1.0 §18). The specification names
    // Thresholds.json explicitly as Session's "idle-timeout policy" configuration home; applied
    // identically to Short-term's terminal-Task-Lifecycle-state trigger as a TTL safety net.
    [Range(1, int.MaxValue)]
    public required int ShortTermMemoryExpirationSeconds { get; init; }

    [Range(1, int.MaxValue)]
    public required int SessionMemoryIdleTimeoutSeconds { get; init; }

    // WP-016's summarization stub (ISummarizer, truncation-only, real implementation at
    // WP-020) has no length defined by any specification — this value has no basis beyond
    // being an arbitrary, externally-configurable stub parameter, never a business rule.
    [Range(1, int.MaxValue)]
    public required int SummarizationStubTruncationLength { get; init; }
}
