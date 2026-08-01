using EOS.KnowledgeGraph;

namespace EOS.Knowledge.Tests;

public class FreshnessCalculatorTests
{
    [Fact]
    public void Calculate_ReturnsZero_WhenNeverValidated()
    {
        var calculator = new FreshnessCalculator(90, new Dictionary<TaxonomyClassification, double>());

        var score = calculator.Calculate(null, TaxonomyClassification.Facts, DateTimeOffset.UtcNow);

        Assert.Equal(0.0, score);
    }

    [Fact]
    public void Calculate_ReturnsHalf_AtExactlyOneHalfLife()
    {
        var calculator = new FreshnessCalculator(90, new Dictionary<TaxonomyClassification, double>());
        var now = DateTimeOffset.UtcNow;

        var score = calculator.Calculate(now.AddDays(-90), null, now);

        Assert.Equal(0.5, score, precision: 6);
    }

    [Fact]
    public void Calculate_AppliesTheConfiguredTypeWeight()
    {
        var calculator = new FreshnessCalculator(
            90, new Dictionary<TaxonomyClassification, double> { [TaxonomyClassification.EngineeringStandards] = 2.0 });
        var now = DateTimeOffset.UtcNow;

        var weighted = calculator.Calculate(now, TaxonomyClassification.EngineeringStandards, now);
        var unweighted = calculator.Calculate(now, TaxonomyClassification.Facts, now);

        Assert.Equal(2.0, weighted);
        Assert.Equal(1.0, unweighted);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Constructor_Throws_ForNonFiniteOrNonPositiveHalfLife(double invalidHalfLifeDays)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new FreshnessCalculator(invalidHalfLifeDays, new Dictionary<TaxonomyClassification, double>()));
    }
}
