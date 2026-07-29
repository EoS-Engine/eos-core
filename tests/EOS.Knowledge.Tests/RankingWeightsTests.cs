namespace EOS.Knowledge.Tests;

public class RankingWeightsTests
{
    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Constructor_Throws_WhenVectorSimilarityIsOutOfRange(double invalidValue)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RankingWeights(invalidValue, 0.3, 0.2, 0.1));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void Constructor_Throws_WhenAnyWeightIsOutOfRange(double invalidValue)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RankingWeights(invalidValue, 0.3, 0.2, 0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RankingWeights(0.4, invalidValue, 0.2, 0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RankingWeights(0.4, 0.3, invalidValue, 0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RankingWeights(0.4, 0.3, 0.2, invalidValue));
    }

    [Fact]
    public void Constructor_Accepts_BoundaryValues()
    {
        var weights = new RankingWeights(0.0, 1.0, 0.0, 1.0);

        Assert.Equal(0.0, weights.VectorSimilarity);
        Assert.Equal(1.0, weights.Recency);
        Assert.Equal(0.0, weights.DomainMatch);
        Assert.Equal(1.0, weights.AccessFrequency);
    }
}
