namespace EOS.Planner;

/// <summary>
/// Planning-Execution-Engine-Specification-v1.0 §20's <c>GoalValidated</c> event (producer: Goal
/// Manager, §11.5), per the Composition Root Adapter Pattern (ADR-015-001). Published from
/// <see cref="GoalValidator"/> — see that class's own doc comment for why §10.1a/§20's "Goal
/// Manager" ownership is realized as a dedicated Goal Management component rather than inside
/// <see cref="GoalManager"/> itself, mirroring how the same specification already separates
/// decomposition (Task Graph Builder) from <c>GoalManager</c>.
/// </summary>
public interface IGoalValidatedEventPublisher
{
    void PublishGoalValidated(Guid goalId, bool feasibilityResult);
}
