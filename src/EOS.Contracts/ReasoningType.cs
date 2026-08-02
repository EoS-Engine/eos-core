namespace EOS.Contracts;

/// <summary>
/// Reasoning-Engine-Specification-v1.0 §11's 13 Reasoning Types — each "a specific
/// configuration of the pipeline (§10)... only a different weighting/subset of stages and a
/// different evidence expectation," never a separate implementation.
/// </summary>
public enum ReasoningType
{
    DeterministicReasoning,
    AnalyticalReasoning,
    RuleBasedReasoning,
    GoalOrientedReasoning,
    ContextualReasoning,
    ArchitecturalReasoning,
    EngineeringReasoning,
    DiagnosticReasoning,
    RootCauseAnalysis,
    ComparativeReasoning,
    RiskReasoning,
    OptimizationReasoning,
    StrategicReasoning,
}
