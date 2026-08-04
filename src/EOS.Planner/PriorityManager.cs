namespace EOS.Planner;

/// <summary>
/// Planning-Execution-Engine-Specification-v1.0 §10.5: "Computes the priority score Constitution
/// Part 7 §7.2's existing Priority Queue already consumes... formalizes that score's inputs
/// (§11.3): stated business priority, deadline proximity, dependency criticality, resource
/// cost — without altering the Priority Queue structure itself" (never built or modified here —
/// Constitution Part 7 §7.2's Priority Queue is Scheduler's own structure, WP-024).
///
/// §11.3 also names "stated business priority (from Product Owner)" and "deadline proximity" as
/// inputs — neither has a field anywhere in this WP's <c>Goal</c> record or any frozen document
/// defining where such a value would originate for a Goal submitted via <c>IPlanningClient</c>;
/// this is a disclosed limitation, not a fabricated computation. This method uses only the two
/// inputs §11.3/§11.4 gives this WP a real, non-invented source for: dependency criticality
/// (§11.4's "aggregate priority of dependent Goals," realized here as the count of other Goals
/// depending on this one) and estimated resource cost (Constitution §0.4.2, from the Goal's own
/// <c>Plan</c>).
/// </summary>
public sealed class PriorityManager
{
    public int ComputePriority(int dependentGoalCount, double estimatedResourceCost)
    {
        // Higher dependency criticality (more Goals blocked on this one) raises priority;
        // higher resource cost lowers it (a cheaper, equally-blocking Goal should dispatch
        // first) — a simple, deterministic, disclosed formula (FR-PE3), not a frozen-document
        // specified one, since none exists.
        var priority = (dependentGoalCount * 10) - (int)Math.Round(estimatedResourceCost);
        return Math.Max(priority, 0);
    }
}
