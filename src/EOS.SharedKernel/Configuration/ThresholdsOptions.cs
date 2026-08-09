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

    // Resource Protection ceilings (Protection-Layer-Specification-v1.0 §16) — Resource-
    // Management-Specification-v1.0 §19.3's Limits concept. WP-021's ResourceManagementClient
    // now provides the real, live-measured budget ProtectionGate logs alongside these
    // configured ceilings; no ActionRequest carries a requested resource amount to compare
    // against them yet (WP-013 Architecture Challenge, G1/G4), so enforcement itself remains
    // structural only, unchanged from WP-013.
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

    // Reasoning Engine Context Expansion cap (Reasoning-Engine-Specification-v1.0 §12.4): "max 1
    // expansion per request, configurable via Thresholds.json" — the cap value itself is the
    // configurable field (WP-019 Implementation Plan, Context Expansion Ambiguity — Resolved).
    // Upper-bounded to 1: §12.4's own text ("max 1 expansion per request... bounded to prevent
    // unbounded back-and-forth") caps expansions at exactly one; only the field's presence in
    // Thresholds.json is what "configurable" refers to, not permission to exceed that bound.
    [Range(1, 1)]
    public required int ReasoningContextExpansionCap { get; init; }

    // Reasoning Engine Low Confidence floor (Reasoning-Engine-Specification-v1.0 §21): "a
    // configurable floor (Thresholds.json, Constitution Part 10)" below which a Decision is
    // still returned but flagged via LowConfidenceDecisionFlagged.
    [Range(0.0, 1.0)]
    public required double ReasoningLowConfidenceFloor { get; init; }

    // Resource Management Capacity Planning (Resource-Management-Specification-v1.0 §17.1-§17.4):
    // Safe/Warning/Critical/Emergency tier boundaries, one triplet per §18.2 monitored dimension
    // — distinct from the existing *CeilingPercent/*CeilingMegabytes/*CeilingTokens/*CeilingCount
    // fields above, which are §19.3 Limits (Protection's per-action ceiling), not §17 Capacity
    // Planning tiers. §17.5: "All four tiers, per resource type, are defined in Thresholds.json."
    [Range(0, 100)]
    public required int CpuWarningThresholdPercent { get; init; }

    [Range(0, 100)]
    public required int CpuCriticalThresholdPercent { get; init; }

    [Range(0, 100)]
    public required int CpuEmergencyThresholdPercent { get; init; }

    [Range(1, int.MaxValue)]
    public required int RamWarningThresholdMegabytes { get; init; }

    [Range(1, int.MaxValue)]
    public required int RamCriticalThresholdMegabytes { get; init; }

    [Range(1, int.MaxValue)]
    public required int RamEmergencyThresholdMegabytes { get; init; }

    [Range(1, int.MaxValue)]
    public required int DiskWarningThresholdMegabytes { get; init; }

    [Range(1, int.MaxValue)]
    public required int DiskCriticalThresholdMegabytes { get; init; }

    [Range(1, int.MaxValue)]
    public required int DiskEmergencyThresholdMegabytes { get; init; }

    [Range(1, int.MaxValue)]
    public required int ModelUsageWarningThresholdTokens { get; init; }

    [Range(1, int.MaxValue)]
    public required int ModelUsageCriticalThresholdTokens { get; init; }

    [Range(1, int.MaxValue)]
    public required int ModelUsageEmergencyThresholdTokens { get; init; }

    [Range(0, int.MaxValue)]
    public required int QueueLengthWarningThresholdCount { get; init; }

    [Range(0, int.MaxValue)]
    public required int QueueLengthCriticalThresholdCount { get; init; }

    [Range(0, int.MaxValue)]
    public required int QueueLengthEmergencyThresholdCount { get; init; }

    [Range(0, int.MaxValue)]
    public required int BackgroundTasksWarningThresholdCount { get; init; }

    [Range(0, int.MaxValue)]
    public required int BackgroundTasksCriticalThresholdCount { get; init; }

    [Range(0, int.MaxValue)]
    public required int BackgroundTasksEmergencyThresholdCount { get; init; }

    [Range(0, 100)]
    public required int CacheUsageWarningThresholdPercent { get; init; }

    [Range(0, 100)]
    public required int CacheUsageCriticalThresholdPercent { get; init; }

    [Range(0, 100)]
    public required int CacheUsageEmergencyThresholdPercent { get; init; }

    // Resource Monitor Sampling Model (Resource-Management-Specification-v1.0 §18.1): "sampled
    // at a bounded cadence (configurable, Thresholds.json), never continuously instrumented" —
    // the elapsed-time throttle bound between real OS-level measurements per resource type
    // (WP-021 Implementation Plan, Sampling Strategy).
    [Range(1, int.MaxValue)]
    public required int ResourceSamplingIntervalSeconds { get; init; }

    // Resource Management Fair-Share Quotas (Resource-Management-Specification-v1.0 §10.4,
    // §19.1-§19.2): per-resource-class ceilings for the 3 named resource types (§19.2: "CPU/RAM/
    // Model-slot"), one field per §16 resource-class per type (WP-022 Implementation Plan
    // Decision D5). Validated at BootstrapRunner startup: rank 1 (UserRequests) >= rank 2
    // (InteractiveSessions) >= ... >= rank 5 (LearningActivities), per §19.1's "each resource-
    // class receives at least its configured minimum share."
    [Range(0, 100)]
    public required int CpuQuotaUserRequestsPercent { get; init; }

    [Range(0, 100)]
    public required int CpuQuotaInteractiveSessionsPercent { get; init; }

    [Range(0, 100)]
    public required int CpuQuotaAutonomousTasksPercent { get; init; }

    [Range(0, 100)]
    public required int CpuQuotaBackgroundMaintenancePercent { get; init; }

    [Range(0, 100)]
    public required int CpuQuotaLearningActivitiesPercent { get; init; }

    [Range(1, int.MaxValue)]
    public required int RamQuotaUserRequestsMegabytes { get; init; }

    [Range(1, int.MaxValue)]
    public required int RamQuotaInteractiveSessionsMegabytes { get; init; }

    [Range(1, int.MaxValue)]
    public required int RamQuotaAutonomousTasksMegabytes { get; init; }

    [Range(1, int.MaxValue)]
    public required int RamQuotaBackgroundMaintenanceMegabytes { get; init; }

    [Range(1, int.MaxValue)]
    public required int RamQuotaLearningActivitiesMegabytes { get; init; }

    [Range(0, int.MaxValue)]
    public required int ModelSlotQuotaUserRequestsCount { get; init; }

    [Range(0, int.MaxValue)]
    public required int ModelSlotQuotaInteractiveSessionsCount { get; init; }

    [Range(0, int.MaxValue)]
    public required int ModelSlotQuotaAutonomousTasksCount { get; init; }

    [Range(0, int.MaxValue)]
    public required int ModelSlotQuotaBackgroundMaintenanceCount { get; init; }

    [Range(0, int.MaxValue)]
    public required int ModelSlotQuotaLearningActivitiesCount { get; init; }

    // §19.4 Starvation Prevention: "denied allocation for more than a configured number of
    // consecutive Sprint cycles." No Sprint-cycle clock exists anywhere in this codebase
    // (pre-existing, disclosed condition — same as CompressionSweep.cs's cadence gap, WP-016);
    // this counts consecutive Background Task Controller evaluations that defer a class instead
    // (WP-022 Implementation Plan Decision D4).
    [Range(1, int.MaxValue)]
    public required int QuotaStarvationDenialCountThreshold { get; init; }

    // §14.2 Model Unloading: idle-residency timeout before a Resident model becomes eligible for
    // eviction.
    [Range(1, int.MaxValue)]
    public required int ModelIdleResidencyTimeoutSeconds { get; init; }

    // Resource-Management-Specification-v1.0 §19.2's Quota Manager rolling-window duration (WP-
    // 022 Implementation Plan Decision D9) — dedicated field, distinct from
    // ResourceSamplingIntervalSeconds (§18.1's unrelated OS-measurement cadence), per WP-022
    // Recovery Plan Slice R4/Finding F6.
    [Range(1, int.MaxValue)]
    public required int QuotaWindowSeconds { get; init; }

    // Constitution Part 7 §7.2's Scheduler structures this codebase had no config source for
    // until WP-024: Concurrency ("Max simultaneous Running tasks per role/domain") and Daily
    // Capacity ("Aggregate task-throughput ceiling per day"). No per-role/domain partitioning
    // data exists on DispatchedTask yet, so Concurrency is enforced as one global ceiling — a
    // disclosed simplification, not a fabricated per-role model.
    [Range(1, int.MaxValue)]
    public required int SchedulerConcurrencyCeilingCount { get; init; }

    [Range(1, int.MaxValue)]
    public required int SchedulerDailyCapacityCount { get; init; }
}
