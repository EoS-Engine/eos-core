namespace EOS.Planner;

/// <summary>
/// Constitution Part 3's existing <c>PlannerGenerated</c> event, reused verbatim
/// (Planning-Execution-Engine-Specification-v1.0 §20; producer: Planning Engine, §10.2), per the
/// Composition Root Adapter Pattern (ADR-015-001).
/// </summary>
public interface IPlannerGeneratedEventPublisher
{
    void PublishPlannerGenerated(Guid planId, Guid taskGraphRef);
}
