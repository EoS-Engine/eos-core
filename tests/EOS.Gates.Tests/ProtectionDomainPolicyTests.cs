using EOS.Contracts;
using EOS.Gates;

namespace EOS.Gates.Tests;

/// <summary>
/// Demonstrates that all eleven Protection Domains (Protection-Layer-Specification-v1.0 §11)
/// are governable through the existing ActionType-routing mechanism (PolicyEngine, WP-012),
/// per the WP-013 Architecture Challenge's resolution of G1: no domain requires payload beyond
/// ActionType, and no new production engine is required per domain. Each test uses a
/// representative ActionType convention the owning subsystem would supply.
/// </summary>
public class ProtectionDomainPolicyTests
{
    private static PolicyDecision EvaluateWithGlobalDenyPolicy(string actionType)
    {
        var engine = new PolicyEngine(
            globalPolicies: [new PolicyEntry(actionType, "Deny", $"{actionType} denied by domain policy.")],
            projectPolicies: [],
            userPolicies: [],
            runtimePolicies: []);

        return engine.Evaluate(actionType);
    }

    [Theory]
    [InlineData("Knowledge.Ingest")]
    [InlineData("Memory.Consolidate")]
    [InlineData("Learning.QuarantineEscalation")]
    [InlineData("Planning.Dispatch")]
    [InlineData("AIProvider.Registration")]
    [InlineData("Resources.CpuRequest")]
    [InlineData("LocalFiles.Write")]
    [InlineData("Projects.Scope")]
    [InlineData("SystemSettings.Change")]
    public void PolicyEngine_GovernsDomainSpecificActionType_ViaConfiguredPolicy(string domainActionType)
    {
        var decision = EvaluateWithGlobalDenyPolicy(domainActionType);

        Assert.False(decision.Allow);
        Assert.Equal($"{domainActionType} denied by domain policy.", decision.Reason);
    }

    [Fact]
    public void ReasoningDomain_IsGoverned_ByRiskEngineRiskBasedGating()
    {
        // Protection-Layer-Specification-v1.0 §11: "Risk-based gating of DecisionMade" — already
        // fully implemented by RiskEngine (WP-012); no new logic required for this domain.
        var engine = new RiskEngine();

        var assessment = engine.Assess("EOS.Reasoning", "Decision", riskScore: 85);

        Assert.Equal(RiskTier.High, assessment.Tier);
    }

    [Fact]
    public void ConfigurationDomain_IsGoverned_ByApprovalEngineDecisionMatrixRouting()
    {
        // Protection-Layer-Specification-v1.0 §11: "configuration changes are Decision-Matrix-
        // routed... actions like any other" — already fully covered by ApprovalEngine's existing
        // "Security-sensitive change" Human-Required row (WP-012); no new logic required.
        var engine = new ApprovalEngine();

        var decision = engine.Resolve("Security-sensitive change");

        Assert.Equal(ProtectionVerdict.Defer, decision.Verdict);
    }
}
