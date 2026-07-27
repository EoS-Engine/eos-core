using EOS.Contracts;
using EOS.Gates;

namespace EOS.Gates.Tests;

public class RiskEngineTests
{
    [Theory]
    [InlineData(0, RiskTier.Low)]
    [InlineData(30, RiskTier.Low)]
    [InlineData(31, RiskTier.Medium)]
    [InlineData(70, RiskTier.Medium)]
    [InlineData(71, RiskTier.High)]
    [InlineData(100, RiskTier.High)]
    public void Assess_ClassifiesTier_PerConstitution0_6_1Boundaries(int riskScore, RiskTier expectedTier)
    {
        var engine = new RiskEngine();

        var assessment = engine.Assess("actor", "actionType", riskScore);

        Assert.Equal(expectedTier, assessment.Tier);
        Assert.False(assessment.Escalated);
    }

    [Fact]
    public void Assess_NeverRecomputesTheScore_OnlyClassifiesWhatWasSupplied()
    {
        var engine = new RiskEngine();

        var first = engine.Assess("actor", "actionType", 50);
        var second = engine.Assess("actor", "actionType", 50);

        Assert.Equal(first.Tier, second.Tier);
    }

    [Fact]
    public void Assess_EscalatesToHighTier_AfterTwoRecordedConsecutiveMediumTierDenials()
    {
        var engine = new RiskEngine();
        engine.RecordMediumTierDenial("actor", "actionType");
        engine.RecordMediumTierDenial("actor", "actionType");

        var assessment = engine.Assess("actor", "actionType", 50);

        Assert.Equal(RiskTier.High, assessment.Tier);
        Assert.True(assessment.Escalated);
    }

    [Fact]
    public void RecordAllow_ResetsTheConsecutiveDenialCount()
    {
        var engine = new RiskEngine();
        engine.RecordMediumTierDenial("actor", "actionType");
        engine.RecordMediumTierDenial("actor", "actionType");
        engine.RecordAllow("actor", "actionType");

        var assessment = engine.Assess("actor", "actionType", 50);

        Assert.Equal(RiskTier.Medium, assessment.Tier);
        Assert.False(assessment.Escalated);
    }

    [Fact]
    public void Assess_DoesNotEscalate_ForADifferentActorOrActionType()
    {
        var engine = new RiskEngine();
        engine.RecordMediumTierDenial("actor-a", "actionType");
        engine.RecordMediumTierDenial("actor-a", "actionType");

        var assessment = engine.Assess("actor-b", "actionType", 50);

        Assert.Equal(RiskTier.Medium, assessment.Tier);
        Assert.False(assessment.Escalated);
    }
}
