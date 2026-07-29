namespace EOS.Knowledge;

public sealed record RankingWeights(double VectorSimilarity, double Recency, double DomainMatch, double AccessFrequency)
{
    public double VectorSimilarity { get; init; } = Validate(VectorSimilarity, nameof(VectorSimilarity));

    public double Recency { get; init; } = Validate(Recency, nameof(Recency));

    public double DomainMatch { get; init; } = Validate(DomainMatch, nameof(DomainMatch));

    public double AccessFrequency { get; init; } = Validate(AccessFrequency, nameof(AccessFrequency));

    private static double Validate(double value, string paramName)
    {
        if (!double.IsFinite(value) || value < 0.0 || value > 1.0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Ranking weights must be finite values within [0.0, 1.0].");
        }

        return value;
    }
}
