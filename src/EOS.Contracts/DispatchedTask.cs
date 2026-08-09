namespace EOS.Contracts;

/// <summary>
/// EOS-Specification.md Part 6's Task entity, first materialized by WP-024 — planning-time-only
/// until now (see <see cref="PlanTask"/>'s own doc comment). One row per <see cref="PlanTask"/>
/// once its owning <see cref="Plan"/> is dispatched-eligible (Planning-Execution-Engine-
/// Specification-v1.0 §10.6/§10.7). <see cref="Priority"/> is Planner's own computed value
/// (Constitution Part 7 §7.2's Priority Queue: "priority score from Planner"), carried verbatim
/// from the <c>TaskCreated</c> event (§20) rather than recomputed. <see cref="NotBefore"/> gates
/// §14's Delayed/Scheduled/Periodic modes (all three are, at bottom, the same "ineligible until a
/// timestamp passes" mechanism §14 itself describes); <see cref="EventObserved"/> gates §14's
/// Event-Driven mode ("eligible only upon a specific Event Catalog event" — no generic,
/// string-keyed Event Catalog subscription mechanism exists anywhere in this codebase today, so
/// this is the minimal honest representation: ineligible until something explicitly observes the
/// required event via <c>Scheduler.MarkEventObserved</c>, never silently treated as eligible).
/// <see cref="RunningAt"/> is Constitution Part 7 §7.2's Daily Capacity check input. None of these
/// are a history/audit trail (ADR-PE002) — only current dispatch state.
/// </summary>
public sealed record DispatchedTask(
    Guid TaskId,
    Guid PlanId,
    Guid GoalId,
    string Description,
    string[] CompetencyRequirements,
    Guid[] DependsOnTaskIds,
    int Priority,
    TaskLifecycleState State,
    SchedulingMode SchedulingMode,
    DateTimeOffset? NotBefore,
    DateTimeOffset? RunningAt,
    bool EventObserved);
