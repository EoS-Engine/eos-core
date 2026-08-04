namespace EOS.Planner;

/// <summary>
/// Planning-Execution-Engine-Specification-v1.0 §20's <c>GoalCreated</c> event (producer: Goal
/// Manager, §10.1a), per the Composition Root Adapter Pattern (ADR-015-001) — <c>EOS.Planner</c>
/// defines this small interface; <c>EOS.Runner</c>'s <c>Program.cs</c> supplies the concrete
/// adapter bridging to <c>EventEnvelope</c>/<c>EventMediator</c> (<c>EOS.Contracts</c>/
/// <c>EOS.Orchestrator</c>), which <c>EOS.Planner</c> has no legal dependency path to reach
/// directly.
/// </summary>
public interface IGoalCreatedEventPublisher
{
    void PublishGoalCreated(Guid goalId, Guid? parentGoalId, string statement);
}
