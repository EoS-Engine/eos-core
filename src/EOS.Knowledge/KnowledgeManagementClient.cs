using EOS.KnowledgeGraph;

namespace EOS.Knowledge;

public sealed class KnowledgeManagementClient(
    KnowledgeGraphStore store,
    IKnowledgeClient knowledgeClient,
    OntologyValidator ontologyValidator,
    IKnowledgeClassifiedEventPublisher? knowledgeClassifiedEventPublisher = null,
    IKnowledgeRelationshipAddedEventPublisher? knowledgeRelationshipAddedEventPublisher = null) : IKnowledgeManagementClient
{
    public async Task ClassifyAsync(
        Guid nodeId, TaxonomyClassification taxonomy, CancellationToken cancellationToken = default)
    {
        var node = await store.GetByIdAsync(nodeId, cancellationToken)
            ?? throw new ArgumentException($"'{nodeId}' does not resolve to an existing node.", nameof(nodeId));

        var updatedMetadata = (node.Metadata ?? new KnowledgeMetadata()) with { Taxonomy = taxonomy };

        await knowledgeClient.UpdateAsync(
            node.NodeId, node.NodeType, node.Content, node.DomainTags, node.EvidenceRefs,
            updatedMetadata, cancellationToken);

        knowledgeClassifiedEventPublisher?.PublishKnowledgeClassified(nodeId, taxonomy);
    }

    public async Task<TaxonomyClassification?> GetClassificationAsync(
        Guid nodeId, CancellationToken cancellationToken = default)
    {
        var node = await store.GetByIdAsync(nodeId, cancellationToken);
        return node?.Metadata?.Taxonomy;
    }

    public async Task AddRelationshipAsync(
        Guid sourceNodeId, RelationshipEdge edge, CancellationToken cancellationToken = default)
    {
        var validation = await ontologyValidator.ValidateAsync(edge, cancellationToken);
        if (!validation.IsValid)
        {
            throw new ArgumentException(validation.Reason, nameof(edge));
        }

        var node = await store.GetByIdAsync(sourceNodeId, cancellationToken)
            ?? throw new ArgumentException($"'{sourceNodeId}' does not resolve to an existing node.", nameof(sourceNodeId));

        var existingRelationships = node.Metadata?.Relationships ?? [];
        var updatedMetadata = (node.Metadata ?? new KnowledgeMetadata()) with
        {
            Relationships = [.. existingRelationships, edge],
        };

        await knowledgeClient.UpdateAsync(
            node.NodeId, node.NodeType, node.Content, node.DomainTags, node.EvidenceRefs,
            updatedMetadata, cancellationToken);

        knowledgeRelationshipAddedEventPublisher?.PublishKnowledgeRelationshipAdded(
            sourceNodeId, edge.TargetNodeId, edge.RelationshipType);
    }

    public async Task<IReadOnlyList<RelationshipEdge>> NavigateRelationshipsAsync(
        Guid nodeId, RelationshipType? type = null, CancellationToken cancellationToken = default)
    {
        var node = await store.GetByIdAsync(nodeId, cancellationToken);
        var relationships = node?.Metadata?.Relationships ?? [];

        return type is null
            ? relationships
            : relationships.Where(edge => edge.RelationshipType == type).ToList();
    }
}
