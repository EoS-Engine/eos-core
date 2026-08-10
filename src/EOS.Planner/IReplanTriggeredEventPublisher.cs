namespace EOS.Planner;

/// <summary>
/// Planning-Execution-Engine-Specification-v1.0 §20's fully specified <c>ReplanTriggered</c>
/// event (producer: Planning Engine, §10.2/§16) — per the Composition Root Adapter Pattern
/// (ADR-015-001), mirroring <see cref="IPlannerGeneratedEventPublisher"/>'s exact precedent. Must
/// only be published after the new Plan artifact and the Goal's moved PlanId have both already
/// committed.
/// </summary>
public interface IReplanTriggeredEventPublisher
{
    void PublishReplanTriggered(Guid goalId, string triggerType);
}
