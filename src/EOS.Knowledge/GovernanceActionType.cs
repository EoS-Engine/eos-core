namespace EOS.Knowledge;

/// <summary>
/// Knowledge-Management-Specification-v1.0 §20.1's <c>request_governance_action()</c> parameter
/// — the exact five state-changing Lifecycle actions §16.4 enumerates as requiring Protection
/// validation and a new Version: "Every Update/Deprecation/Archiving/Retirement/Recovery
/// (§12.7–§12.11) is a Governance action... no exceptions." Names match
/// <see cref="EOS.KnowledgeGraph.KnowledgeLifecycleState"/>'s corresponding values directly.
/// </summary>
public enum GovernanceActionType
{
    Update,
    Deprecation,
    Archiving,
    Retirement,
    Recovery,
}
