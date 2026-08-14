using EOS.Contracts;

namespace EOS.Orchestrator;

/// <summary>
/// Autonomous-Engineering-Loop-Specification-v1.0 §17/§22's <c>OperationalModeChanged</c> event —
/// payload frozen exactly as specified: "from_mode, to_mode, changed_by". Published only after a
/// mode change has already been Protection-validated (<see cref="ProtectionVerdict.Allow"/>) and
/// persisted — matching this class's siblings' persist-before-publish invariant.
/// </summary>
public interface IOperationalModeChangedEventPublisher
{
    void PublishOperationalModeChanged(OperationalMode fromMode, OperationalMode toMode, string changedBy);
}
