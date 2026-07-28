namespace EOS.Knowledge;

public sealed record RankingWeights(
    double VectorSimilarity, double Recency, double DomainMatch, double AccessFrequency);
