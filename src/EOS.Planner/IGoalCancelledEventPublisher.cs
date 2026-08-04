namespace EOS.Planner;

/// <summary>
/// Planning-Execution-Engine-Specification-v1.0 §20's <c>GoalCancelled</c> event (producer: Goal
/// Manager, §11.6), per the Composition Root Adapter Pattern (ADR-015-001).
/// </summary>
public interface IGoalCancelledEventPublisher
{
    void PublishGoalCancelled(Guid goalId, string reason);
}
