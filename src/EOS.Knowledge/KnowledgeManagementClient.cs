using EOS.Contracts;
using EOS.KnowledgeGraph;

namespace EOS.Knowledge;

public sealed class KnowledgeManagementClient(
    KnowledgeGraphStore store,
    IKnowledgeClient knowledgeClient,
    OntologyValidator ontologyValidator,
    FreshnessCalculator freshnessCalculator,
    DuplicateDetector duplicateDetector,
    IProtectionClient protectionClient,
    KnowledgeRankingWeights rankingWeights,
    double freshnessExpirationThreshold,
    IKnowledgeClassifiedEventPublisher? knowledgeClassifiedEventPublisher = null,
    IKnowledgeRelationshipAddedEventPublisher? knowledgeRelationshipAddedEventPublisher = null,
    IKnowledgeQualityUpdatedEventPublisher? knowledgeQualityUpdatedEventPublisher = null,
    IKnowledgeGovernanceActionRequestedEventPublisher? knowledgeGovernanceActionRequestedEventPublisher = null,
    IKnowledgeGovernanceActionAppliedEventPublisher? knowledgeGovernanceActionAppliedEventPublisher = null,
    IKnowledgeFreshnessExpiredEventPublisher? knowledgeFreshnessExpiredEventPublisher = null,
    IKnowledgeDuplicateFlaggedEventPublisher? knowledgeDuplicateFlaggedEventPublisher = null) : IKnowledgeManagementClient
{
    // §16.4: "Every Update/Deprecation/Archiving/Retirement/Recovery... is a Governance action
    // requiring Protection validation" — mirrors WP-016's "MemoryCompression" ActionRequest
    // precedent (Program.cs), since no ActionRequest carries a real requested-resource amount
    // or actor-authority data yet (WP-012/WP-013 Implementation Plans).
    private const string GovernanceActor = "KnowledgeOwner";
    private const int GovernanceActionRiskScore = 10;
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

    public async Task<QualityProfile?> GetQualityAsync(Guid nodeId, CancellationToken cancellationToken = default)
    {
        var node = await store.GetByIdAsync(nodeId, cancellationToken);
        if (node is null)
        {
            return null;
        }

        var completeness = ComputeCompleteness(node.Metadata);
        var freshness = freshnessCalculator.Calculate(
            node.Metadata?.LastValidation, node.Metadata?.Taxonomy, DateTimeOffset.UtcNow);

        var quality = (node.Metadata?.Quality ?? new QualityProfile()) with
        {
            Completeness = completeness,
            Freshness = freshness,
        };

        var updatedMetadata = (node.Metadata ?? new KnowledgeMetadata()) with { Quality = quality };
        await knowledgeClient.UpdateAsync(
            node.NodeId, node.NodeType, node.Content, node.DomainTags, node.EvidenceRefs,
            updatedMetadata, cancellationToken);

        knowledgeQualityUpdatedEventPublisher?.PublishKnowledgeQualityUpdated(nodeId, quality);

        if (freshness < freshnessExpirationThreshold)
        {
            knowledgeFreshnessExpiredEventPublisher?.PublishKnowledgeFreshnessExpired(nodeId, freshness);
        }

        return quality;
    }

    public async Task<IReadOnlyList<KnowledgeSearchResult>> SearchAsync(
        SearchRequest request, CancellationToken cancellationToken = default)
    {
        var memoryResults = (await knowledgeClient.QueryAsync(
            request.Type, request.DomainTags, request.Range, cancellationToken)).ToList();

        return memoryResults
            .Select((node, index) => (Node: node, Score: ComputeKmScore(node, request), Index: index))
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Index)
            .Select(item => new KnowledgeSearchResult(item.Node, item.Score))
            .ToList();
    }

    public async Task<ValidationResult> RequestGovernanceActionAsync(
        Guid nodeId, GovernanceActionType action, string justification, CancellationToken cancellationToken = default)
    {
        var node = await store.GetByIdAsync(nodeId, cancellationToken)
            ?? throw new ArgumentException($"'{nodeId}' does not resolve to an existing node.", nameof(nodeId));

        knowledgeGovernanceActionRequestedEventPublisher?.PublishKnowledgeGovernanceActionRequested(
            nodeId, action, GovernanceActor);

        var validationResult = protectionClient.Validate(new ActionRequest(
            ActionId: Guid.NewGuid(),
            ActionType: $"KnowledgeGovernance{action}",
            Actor: GovernanceActor,
            RiskScore: GovernanceActionRiskScore));

        if (validationResult.Verdict != ProtectionVerdict.Allow)
        {
            return validationResult;
        }

        var existingMetadata = node.Metadata ?? new KnowledgeMetadata();
        var nextVersion = (existingMetadata.VersionHistory.Count == 0 ? 0 : existingMetadata.VersionHistory[^1].Version) + 1;
        var versionRecord = new VersionRecord
        {
            Version = nextVersion,
            Timestamp = DateTimeOffset.UtcNow,
            Reason = justification,
        };

        var updatedMetadata = existingMetadata with
        {
            LifecycleState = ToLifecycleState(action),
            VersionHistory = [.. existingMetadata.VersionHistory, versionRecord],
        };

        await knowledgeClient.UpdateAsync(
            node.NodeId, node.NodeType, node.Content, node.DomainTags, node.EvidenceRefs,
            updatedMetadata, cancellationToken);

        knowledgeGovernanceActionAppliedEventPublisher?.PublishKnowledgeGovernanceActionApplied(
            nodeId, action, nextVersion);

        return validationResult;
    }

    public async Task<IReadOnlyList<DuplicateCandidate>> FindDuplicatesAsync(
        Guid nodeId, CancellationToken cancellationToken = default)
    {
        var candidates = await duplicateDetector.FindDuplicatesAsync(nodeId, cancellationToken);

        foreach (var candidate in candidates)
        {
            knowledgeDuplicateFlaggedEventPublisher?.PublishKnowledgeDuplicateFlagged(
                nodeId, candidate.NodeId, candidate.SimilaritySource);
        }

        return candidates;
    }

    private double ComputeKmScore(KnowledgeNode item, SearchRequest request)
    {
        var quality = item.Metadata?.Quality;
        var relationshipRelevance = request.RelationshipContextNodeId is { } contextNodeId
            && (item.Metadata?.Relationships ?? []).Any(edge => edge.TargetNodeId == contextNodeId)
                ? 1.0
                : 0.0;
        var deprecationPenalty = item.Metadata?.LifecycleState == KnowledgeLifecycleState.Deprecation ? 1.0 : 0.0;

        return rankingWeights.Confidence * (quality?.Confidence ?? 0.0)
            + rankingWeights.Reliability * (quality?.Reliability ?? 0.0)
            + rankingWeights.RelationshipRelevance * relationshipRelevance
            - rankingWeights.DeprecationPenalty * deprecationPenalty;
    }

    private static double ComputeCompleteness(KnowledgeMetadata? metadata)
    {
        var populatedFields = new[]
        {
            metadata?.Owner is not null,
            metadata?.Source is not null,
            metadata?.Taxonomy is not null,
            metadata?.LifecycleState is not null,
            metadata?.LastValidation is not null,
        };

        return (double)populatedFields.Count(populated => populated) / populatedFields.Length;
    }

    private static KnowledgeLifecycleState ToLifecycleState(GovernanceActionType action) => action switch
    {
        GovernanceActionType.Update => KnowledgeLifecycleState.Update,
        GovernanceActionType.Deprecation => KnowledgeLifecycleState.Deprecation,
        GovernanceActionType.Archiving => KnowledgeLifecycleState.Archiving,
        GovernanceActionType.Retirement => KnowledgeLifecycleState.Retirement,
        GovernanceActionType.Recovery => KnowledgeLifecycleState.Recovery,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unrecognized GovernanceActionType."),
    };
}
