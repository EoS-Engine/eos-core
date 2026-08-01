using EOS.KnowledgeGraph;

namespace EOS.Knowledge;

/// <summary>
/// Knowledge-Management-Specification-v1.0 §17.1: "FreshnessScore = decay_function(now() -
/// knowledge_metadata.last_validation) * type_weight(taxonomy classification, §11)... externally
/// configurable (<c>Knowledge.json</c>)." <paramref name="decayHalfLifeDays"/> parameterizes
/// <c>decay_function</c> as exponential half-life decay (§17.5's "passive accumulation of time...
/// a pure input" is satisfied by any monotonically-decreasing function of age; half-life decay
/// is the minimal, standard concrete form). <paramref name="typeWeights"/> parameterizes
/// <c>type_weight</c>; a taxonomy absent from the map uses a neutral 1.0 weight.
/// </summary>
public sealed class FreshnessCalculator(double decayHalfLifeDays, IReadOnlyDictionary<TaxonomyClassification, double> typeWeights)
{
    public double Calculate(DateTimeOffset? lastValidation, TaxonomyClassification? taxonomy, DateTimeOffset now)
    {
        if (lastValidation is null)
        {
            return 0.0;
        }

        var ageDays = Math.Max(0.0, (now - lastValidation.Value).TotalDays);
        var decay = Math.Pow(0.5, ageDays / decayHalfLifeDays);
        var weight = taxonomy is not null && typeWeights.TryGetValue(taxonomy.Value, out var configuredWeight)
            ? configuredWeight
            : 1.0;

        return decay * weight;
    }
}
