namespace EOS.Gates;

/// <summary>
/// Resource Protection ceilings (Protection-Layer-Specification-v1.0 §16) — Resource-Management-
/// Specification-v1.0 §19.3's Limits concept, distinct from that specification's own §17
/// Capacity Planning tiers (Safe/Warning/Critical/Emergency, WP-021's <c>CapacityTier</c>).
/// Structurally ready: no ActionRequest carries a requested resource amount to compare against
/// these ceilings yet (WP-013 Architecture Challenge, G1/G4) — this remains true after WP-021,
/// which is limited to logging <see cref="EOS.Contracts.IResourceManagementClient"/>'s real,
/// live-measured budget alongside these configured ceilings (<c>ProtectionGate</c>), not adding
/// the enforcement comparison itself.
/// </summary>
public sealed record ResourceCeilings(
    int CpuCeilingPercent,
    int RamCeilingMegabytes,
    int DiskCeilingMegabytes,
    int ModelUsageCeilingTokens,
    int ContextSizeCeilingTokens,
    int BackgroundTasksCeilingCount);
