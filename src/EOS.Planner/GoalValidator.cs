using EOS.Contracts;

namespace EOS.Planner;

/// <summary>
/// Planning-Execution-Engine-Specification-v1.0 §11.5: "Before decomposition, a Goal is checked
/// for: a resolvable, non-ambiguous statement of intent... feasibility against known
/// competencies (Competency Graph, Constitution §0.3), and Protection Layer policy compliance
/// (Protection-Layer-Specification-v1.0 §11, Planning domain) before any Task Graph is built."
///
/// §10.1a's prose ("The Goal Manager is where a stakeholder-level intent is validated, §11.5")
/// and §20's event table (producer: "Goal Manager, §11.5") describe validation as conceptually
/// belonging to Goal Management, not as a mandate that <c>GoalManager</c> itself must contain the
/// validation logic — the same specification already treats decomposition identically
/// ("[Goal Manager] does not itself decompose a Goal into tasks — that is the Task Graph
/// Builder's job, §10.3," §10.1a), i.e. a distinct class within Goal Management's domain. This
/// class is that same pattern applied to validation: a dedicated, single-responsibility component
/// <see cref="PlanningEngine"/> (§10.2, which alone orchestrates Goal Management's components)
/// invokes immediately after Goal creation, publishing the exact <c>GoalValidated</c>
/// (goal_id, feasibility_result) payload §20 specifies.
/// </summary>
public sealed class GoalValidator(IProtectionClient protectionClient, IGoalValidatedEventPublisher goalValidatedEventPublisher)
{
    public GoalValidationResult Validate(Goal goal)
    {
        if (!HasResolvableIntent(goal))
        {
            var result = new GoalValidationResult(Feasible: false, Reason: "The Goal statement is empty or whitespace-only and cannot be resolved.");
            goalValidatedEventPublisher.PublishGoalValidated(goal.GoalId, result.Feasible);
            return result;
        }

        if (!IsCompetencyFeasible(goal))
        {
            var result = new GoalValidationResult(Feasible: false, Reason: "The Goal is not feasible against known competencies.");
            goalValidatedEventPublisher.PublishGoalValidated(goal.GoalId, result.Feasible);
            return result;
        }

        var protectionResult = protectionClient.Validate(new ActionRequest(
            ActionId: Guid.NewGuid(),
            ActionType: "GoalValidation",
            Actor: goal.SubmittedByActor,
            RiskScore: 10));

        if (protectionResult.Verdict != ProtectionVerdict.Allow)
        {
            var result = new GoalValidationResult(Feasible: false, Reason: protectionResult.Reason ?? "Protection Layer denied Goal validation.");
            goalValidatedEventPublisher.PublishGoalValidated(goal.GoalId, result.Feasible);
            return result;
        }

        var feasible = new GoalValidationResult(Feasible: true, Reason: null);
        goalValidatedEventPublisher.PublishGoalValidated(goal.GoalId, feasible.Feasible);
        return feasible;
    }

    // §11.5: "delegating this specific judgment to Reasoning Engine's Goal Understanding/Intent
    // Analysis stages where genuinely ambiguous" — no frozen document defines a deterministic
    // trigger condition for "genuinely ambiguous" (FR-PE3 requires determinism outside the
    // bounded Reasoning case), so this check stays at the one objective, non-invented bar a
    // statement of intent must clear: it must actually state something. The Task Graph Builder's
    // own bounded Reasoning delegation (§10.3/§10.11, ADR-PE003) is where this WP exercises the
    // Reasoning Engine delegation the roadmap's acceptance criteria target.
    private static bool HasResolvableIntent(Goal goal) => !string.IsNullOrWhiteSpace(goal.Statement);

    // Constitution §0.3's Competency Graph has no concrete implementation anywhere in this
    // codebase yet — no store, no query surface, nothing to consult. Honestly permissive
    // default (never blocks a Goal on data nothing in this codebase can currently supply),
    // matching the established precedent for the same class of gap (e.g.
    // BackgroundTaskController.WithinMaintenanceWindow, WP-022).
    private static bool IsCompetencyFeasible(Goal goal) => true;
}
