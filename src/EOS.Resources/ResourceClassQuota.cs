using EOS.Contracts;

namespace EOS.Resources;

/// <summary>
/// Resource-Management-Specification-v1.0 §19.2's per-resource-class, per-resource-type
/// ("CPU/RAM/Model-slot") numeric ceiling. Plain, <c>EOS.SharedKernel</c>-free record, mirroring
/// <see cref="ResourceTierBoundaries"/>'s precedent (WP-021) — <c>Program.cs</c> maps
/// <c>ThresholdsOptions</c>' fields into this type at the composition root.
///
/// <para><b>Known limitation (WP-022 Recovery Plan Slice R5/Finding F3, disclosed, not hidden):</b>
/// <see cref="CpuPercent"/> and <see cref="RamMegabytes"/> are configured here because §19.2
/// literally names all three resource types ("CPU/RAM/Model-slot"), but only
/// <see cref="ModelSlotCount"/> is currently enforced by <see cref="QuotaManager"/>.
/// <see cref="ResourceMonitor"/> (§18.2) provides only aggregate, system-wide CPU/RAM
/// measurements — no frozen document, and no mechanism anywhere in this repository, attributes
/// currently-consumed CPU/RAM to a specific <see cref="ResourceClass"/>. Enforcing a genuine
/// per-class CPU/RAM fair-share ceiling would require new instrumentation (e.g. per-job resource
/// accounting) that no frozen document defines — out of this WP's scope, since it would be new
/// architecture. This is an intentional, disclosed limitation, not silently missing
/// functionality.</para>
/// </summary>
public sealed record ResourceClassQuota(double CpuPercent, double RamMegabytes, int ModelSlotCount);

/// <summary>
/// One <see cref="ResourceClassQuota"/> per <see cref="ResourceClass"/> (§16).
/// </summary>
public sealed record ResourceClassQuotas(
    ResourceClassQuota UserRequests,
    ResourceClassQuota InteractiveSessions,
    ResourceClassQuota AutonomousTasks,
    ResourceClassQuota BackgroundMaintenance,
    ResourceClassQuota LearningActivities);
