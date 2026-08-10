namespace EOS.Learning.Tests;

public class ConfidenceGuardTests
{
    [Theory]
    [InlineData(1.0, 1.0, 1.0)]
    [InlineData(1.0, 0.5, 0.5)]
    [InlineData(0.8, 0.5, 0.4)]
    [InlineData(0.0, 1.0, 0.0)]
    public void Assess_MultipliesComparisonConfidenceByTrustScore(double comparisonConfidence, double trustScore, double expected)
    {
        var guard = new ConfidenceGuard();

        var result = guard.Assess(comparisonConfidence, trustScore);

        Assert.Equal(expected, result, precision: 10);
    }
}
