using EOS.Gates;

namespace EOS.Gates.Tests;

public class PolicyEngineTests
{
    [Fact]
    public void Evaluate_ReturnsAllow_WhenNoPolicyIsConfigured()
    {
        var engine = new PolicyEngine([], [], [], []);

        var decision = engine.Evaluate("AnyAction");

        Assert.True(decision.Allow);
        Assert.Null(decision.Reason);
    }

    [Fact]
    public void Evaluate_ReturnsDeny_WhenAGlobalPolicyDeniesTheActionType()
    {
        var engine = new PolicyEngine(
            globalPolicies: [new PolicyEntry("Deploy", "Deny", "Deploys are globally restricted.")],
            projectPolicies: [],
            userPolicies: [],
            runtimePolicies: []);

        var decision = engine.Evaluate("Deploy");

        Assert.False(decision.Allow);
        Assert.Equal("Deploys are globally restricted.", decision.Reason);
    }

    [Fact]
    public void Evaluate_GlobalPolicyTakesPrecedenceOverProjectPolicy()
    {
        var engine = new PolicyEngine(
            globalPolicies: [new PolicyEntry("Deploy", "Deny", "Global deny.")],
            projectPolicies: [new PolicyEntry("Deploy", "Allow", "Project allow.")],
            userPolicies: [],
            runtimePolicies: []);

        var decision = engine.Evaluate("Deploy");

        Assert.False(decision.Allow);
        Assert.Equal("Global deny.", decision.Reason);
    }

    [Fact]
    public void Evaluate_FallsBackToRuntimeTier_WhenNoHigherTierMatches()
    {
        var engine = new PolicyEngine(
            globalPolicies: [],
            projectPolicies: [],
            userPolicies: [],
            runtimePolicies: [new PolicyEntry("Deploy", "Deny", "Runtime deny.")]);

        var decision = engine.Evaluate("Deploy");

        Assert.False(decision.Allow);
        Assert.Equal("Runtime deny.", decision.Reason);
    }

    [Fact]
    public void Evaluate_WildcardPolicyMatchesAnyActionType()
    {
        var engine = new PolicyEngine(
            globalPolicies: [new PolicyEntry("*", "Deny", "Everything denied by wildcard.")],
            projectPolicies: [],
            userPolicies: [],
            runtimePolicies: []);

        var decision = engine.Evaluate("AnyAction");

        Assert.False(decision.Allow);
        Assert.Equal("Everything denied by wildcard.", decision.Reason);
    }
}
