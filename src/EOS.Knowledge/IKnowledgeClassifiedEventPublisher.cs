using EOS.KnowledgeGraph;

namespace EOS.Knowledge;

/// <summary>
/// Knowledge-Management-Specification-v1.0 §19's <c>KnowledgeClassified</c> event
/// (payload: <c>node_id, taxonomy_type</c>), per the Composition Root Adapter Pattern
/// (ADR-015-001): <c>EOS.Knowledge</c> defines this small interface; <c>EOS.Runner</c>'s
/// <c>Program.cs</c> supplies the concrete adapter bridging to
/// <c>EventEnvelope</c>/<c>EventMediator</c>, which <c>EOS.Knowledge</c> has no legal
/// dependency path to reach directly.
/// </summary>
public interface IKnowledgeClassifiedEventPublisher
{
    void PublishKnowledgeClassified(Guid nodeId, TaxonomyClassification taxonomyType);
}
