namespace EOS.Contracts;

/// <summary>
/// Planning-Execution-Engine-Specification-v1.0 §11 — the first-class, decomposable, hierarchical
/// entity Constitution §0.4.1 refers to only as "Backlog items." <see cref="ParentGoalId"/>
/// realizes §11.2's Goal Hierarchy (a DAG, not merely a tree); <see cref="PlanId"/> is set only
/// once the Goal reaches <see cref="GoalLifecycleState.Planned"/> (§11.1: "the state in which a
/// Goal has a corresponding Plan artifact") — never populated for non-leaf Goals, since §11.2
/// states "parent Goals never have their own Tasks directly, only via their children's Task
/// Graphs."
/// </summary>
public sealed record Goal(
    Guid GoalId,
    string Statement,
    Guid? ParentGoalId,
    string[] DomainTags,
    string SubmittedByActor,
    GoalLifecycleState State,
    Guid? PlanId);
