namespace EOS.Gates;

/// <summary>
/// Resource Protection ceilings (Protection-Layer-Specification-v1.0 §16). Structurally ready,
/// stub values only — no enforcement beyond structural readiness; no ActionRequest carries a
/// requested resource amount to compare against these ceilings yet (WP-013 Architecture
/// Challenge, G1/G4). Real enforcement awaits Resource Management (WP-021).
/// </summary>
public sealed record ResourceCeilings(
    int CpuCeilingPercent,
    int RamCeilingMegabytes,
    int DiskCeilingMegabytes,
    int ModelUsageCeilingTokens,
    int ContextSizeCeilingTokens,
    int BackgroundTasksCeilingCount);
