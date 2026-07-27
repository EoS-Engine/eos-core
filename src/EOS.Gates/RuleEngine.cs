namespace EOS.Gates;

public sealed record RuleDecision(bool Allow, string? Reason);

/// <summary>
/// Pass-through this WP: no real caller sends an ActionType corresponding to a
/// Task-Lifecycle-stage-advancing artifact (Constitution Part 0 §0.8/§2.3), and Architecture
/// Fitness Rules have no production-callable form to reuse without a hidden dependency
/// (see WP-012 Architecture Freeze). Structurally present and ready for real rule definitions.
/// </summary>
public sealed class RuleEngine
{
    public RuleDecision Evaluate(string actionType) => new(Allow: true, Reason: null);
}
