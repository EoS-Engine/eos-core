using EOS.Contracts;

namespace EOS.Gates;

public sealed record RuleDecision(bool Allow, string? Reason);

/// <summary>
/// Rule Engine (Protection §10.3: the Rule Engine executes the Universal Gates).
/// <see cref="Evaluate(string)"/> remains a pass-through: no real caller sends an ActionType corresponding to a
/// Task-Lifecycle-stage-advancing artifact (Constitution Part 0 §0.8/§2.3), and Architecture
/// Fitness Rules have no production-callable form to reuse without a hidden dependency
/// (see WP-012 Architecture Freeze).
/// <see cref="EvaluateUniversalGates"/> (ADR-009) is the first real rule: it turns the measured
/// outcome of Universal Gates 1–2 (Constitution §0.8.1) into the pass/fail decision that governs
/// whether a task may leave <c>Running</c> for <c>Review</c> (§0.8.3, §6.2).
/// </summary>
public sealed class RuleEngine
{
    public RuleDecision Evaluate(string actionType) => new(Allow: true, Reason: null);

    /// <summary>
    /// Gate 1 (build / static analysis) must have passed. Gate 2 (unit tests) must have passed or be
    /// not applicable (no test project covers the changed paths). Anything else — including a step
    /// that could not run — fails closed (§0.8.3).
    /// </summary>
    public RuleDecision EvaluateUniversalGates(UniversalGateResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.BuildGate.Status != GateStepStatus.Passed)
        {
            return new RuleDecision(Allow: false, Reason: $"Universal Gate 1 (build/static analysis) {Describe(result.BuildGate)}");
        }

        if (result.TestGate.Status == GateStepStatus.Failed)
        {
            return new RuleDecision(Allow: false, Reason: $"Universal Gate 2 (unit tests) {Describe(result.TestGate)}");
        }

        return new RuleDecision(Allow: true, Reason: null);
    }

    private static string Describe(GateStepResult step) =>
        step.Status switch
        {
            GateStepStatus.Failed => $"failed: {step.Detail}",
            GateStepStatus.NotApplicable => $"did not run: {step.Detail}",
            _ => step.Detail ?? string.Empty,
        };
}
