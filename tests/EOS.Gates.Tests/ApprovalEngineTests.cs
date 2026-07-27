using EOS.Contracts;
using EOS.Gates;

namespace EOS.Gates.Tests;

public class ApprovalEngineTests
{
    [Theory]
    [InlineData("Constitutional amendment")]
    [InlineData("Security-sensitive change")]
    [InlineData("Disaster recovery invocation")]
    public void Resolve_Defers_ForDecisionMatrixHumanRequiredRows(string actionType)
    {
        var engine = new ApprovalEngine();

        var decision = engine.Resolve(actionType);

        Assert.Equal(ProtectionVerdict.Defer, decision.Verdict);
        Assert.NotNull(decision.Reason);
    }

    [Theory]
    [InlineData("Code-level implementation choice")]
    [InlineData("Decision")]
    [InlineData("AnyUnrecognizedActionType")]
    public void Resolve_Allows_ForEverythingElse(string actionType)
    {
        var engine = new ApprovalEngine();

        var decision = engine.Resolve(actionType);

        Assert.Equal(ProtectionVerdict.Allow, decision.Verdict);
        Assert.Null(decision.Reason);
    }

    [Fact]
    public void Resolve_IsCaseInsensitive()
    {
        var engine = new ApprovalEngine();

        var decision = engine.Resolve("security-sensitive change");

        Assert.Equal(ProtectionVerdict.Defer, decision.Verdict);
    }
}
