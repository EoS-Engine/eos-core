using EOS.KnowledgeGraph;
using EOS.VectorStore;

namespace EOS.Knowledge.Tests;

public class KnowledgeManagementClientTests
{
    private static readonly RankingWeights DefaultRankingWeights = new(
        VectorSimilarity: 0.4, Recency: 0.3, DomainMatch: 0.2, AccessFrequency: 0.1);

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("EOS_SQLSERVER_CONNECTION_STRING")
        ?? throw new InvalidOperationException("EOS_SQLSERVER_CONNECTION_STRING is not set.");

    private static string ChromaDbEndpoint =>
        Environment.GetEnvironmentVariable("EOS_CHROMADB_ENDPOINT")
        ?? throw new InvalidOperationException("EOS_CHROMADB_ENDPOINT is not set.");

    private static async Task<(KnowledgeGraphStore Store, KnowledgeManagementClient Client)> CreateAsync(
        CapturingKnowledgeClassifiedEventPublisher? classifiedPublisher = null,
        CapturingKnowledgeRelationshipAddedEventPublisher? relationshipPublisher = null)
    {
        var store = new KnowledgeGraphStore(ConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        IKnowledgeClient knowledgeClient = new KnowledgeClient(
            store, DefaultRankingWeights, new ChromaVectorStore(ChromaDbEndpoint), NeverCalledMemorySourceStore.Instance);
        var ontologyValidator = new OntologyValidator(
            store,
            dependsOnDisallowedTargetTypes: [KnowledgeNodeType.Lesson],
            governanceApprovalRequiredRelationshipTypes: [RelationshipType.Replaces, RelationshipType.Supersedes]);
        var client = new KnowledgeManagementClient(
            store, knowledgeClient, ontologyValidator, classifiedPublisher, relationshipPublisher);
        return (store, client);
    }

    [Fact]
    public async Task ClassifyAsync_ThenGetClassificationAsync_RoundTripsTheTaxonomy_AndPublishesTheEvent()
    {
        var eventPublisher = new CapturingKnowledgeClassifiedEventPublisher();
        var (store, client) = await CreateAsync(classifiedPublisher: eventPublisher);
        var nodeId = Guid.NewGuid();
        await store.UpsertAsync(
            new KnowledgeNode(nodeId, KnowledgeNodeType.Fact, "a fact", [], [], DateTimeOffset.UtcNow),
            CancellationToken.None);

        await client.ClassifyAsync(nodeId, TaxonomyClassification.Facts, CancellationToken.None);
        var classification = await client.GetClassificationAsync(nodeId, CancellationToken.None);

        Assert.Equal(TaxonomyClassification.Facts, classification);
        Assert.Equal(nodeId, eventPublisher.LastNodeId);
        Assert.Equal(TaxonomyClassification.Facts, eventPublisher.LastTaxonomyType);
    }

    [Fact]
    public async Task GetClassificationAsync_ReturnsNull_ForANeverClassifiedNode()
    {
        var (store, client) = await CreateAsync();
        var nodeId = Guid.NewGuid();
        await store.UpsertAsync(
            new KnowledgeNode(nodeId, KnowledgeNodeType.Fact, "a fact", [], [], DateTimeOffset.UtcNow),
            CancellationToken.None);

        var classification = await client.GetClassificationAsync(nodeId, CancellationToken.None);

        Assert.Null(classification);
    }

    [Fact]
    public async Task ClassifyAsync_Throws_WhenNodeDoesNotExist()
    {
        var (_, client) = await CreateAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.ClassifyAsync(Guid.NewGuid(), TaxonomyClassification.Facts, CancellationToken.None));
    }

    [Fact]
    public async Task RoadmapDemo_FactAndPatternConnectedViaSupports_AreClassifiedAndNavigable()
    {
        var classifiedPublisher = new CapturingKnowledgeClassifiedEventPublisher();
        var relationshipPublisher = new CapturingKnowledgeRelationshipAddedEventPublisher();
        var (store, client) = await CreateAsync(classifiedPublisher, relationshipPublisher);
        var factId = Guid.NewGuid();
        var patternId = Guid.NewGuid();
        await store.UpsertAsync(
            new KnowledgeNode(factId, KnowledgeNodeType.Fact, "a supporting fact", [], [], DateTimeOffset.UtcNow),
            CancellationToken.None);
        await store.UpsertAsync(
            new KnowledgeNode(patternId, KnowledgeNodeType.Pattern, "a pattern", [], [], DateTimeOffset.UtcNow),
            CancellationToken.None);

        await client.ClassifyAsync(factId, TaxonomyClassification.Facts, CancellationToken.None);
        await client.ClassifyAsync(patternId, TaxonomyClassification.Patterns, CancellationToken.None);
        await client.AddRelationshipAsync(
            factId,
            new RelationshipEdge { TargetNodeId = patternId, RelationshipType = RelationshipType.Supports },
            CancellationToken.None);

        var factClassification = await client.GetClassificationAsync(factId, CancellationToken.None);
        var patternClassification = await client.GetClassificationAsync(patternId, CancellationToken.None);
        var relationships = await client.NavigateRelationshipsAsync(factId, cancellationToken: CancellationToken.None);

        Assert.Equal(TaxonomyClassification.Facts, factClassification);
        Assert.Equal(TaxonomyClassification.Patterns, patternClassification);
        var edge = Assert.Single(relationships);
        Assert.Equal(patternId, edge.TargetNodeId);
        Assert.Equal(RelationshipType.Supports, edge.RelationshipType);
        Assert.Equal(factId, relationshipPublisher.LastSourceNodeId);
        Assert.Equal(patternId, relationshipPublisher.LastTargetNodeId);
        Assert.Equal(RelationshipType.Supports, relationshipPublisher.LastRelationshipType);
    }

    [Fact]
    public async Task NavigateRelationshipsAsync_FiltersByType_WhenTypeIsSpecified()
    {
        var (store, client) = await CreateAsync();
        var sourceId = Guid.NewGuid();
        var supportsTargetId = Guid.NewGuid();
        var relatedToTargetId = Guid.NewGuid();
        await store.UpsertAsync(
            new KnowledgeNode(sourceId, KnowledgeNodeType.Fact, "source", [], [], DateTimeOffset.UtcNow),
            CancellationToken.None);
        await store.UpsertAsync(
            new KnowledgeNode(supportsTargetId, KnowledgeNodeType.Pattern, "supports target", [], [], DateTimeOffset.UtcNow),
            CancellationToken.None);
        await store.UpsertAsync(
            new KnowledgeNode(relatedToTargetId, KnowledgeNodeType.Fact, "related target", [], [], DateTimeOffset.UtcNow),
            CancellationToken.None);
        await client.AddRelationshipAsync(
            sourceId,
            new RelationshipEdge { TargetNodeId = supportsTargetId, RelationshipType = RelationshipType.Supports },
            CancellationToken.None);
        await client.AddRelationshipAsync(
            sourceId,
            new RelationshipEdge { TargetNodeId = relatedToTargetId, RelationshipType = RelationshipType.RelatedTo },
            CancellationToken.None);

        var supportsOnly = await client.NavigateRelationshipsAsync(sourceId, RelationshipType.Supports, CancellationToken.None);

        var edge = Assert.Single(supportsOnly);
        Assert.Equal(supportsTargetId, edge.TargetNodeId);
    }

    [Fact]
    public async Task AddRelationshipAsync_Throws_WhenTheOntologyConstraintIsViolated()
    {
        var (store, client) = await CreateAsync();
        var sourceId = Guid.NewGuid();
        var lessonTargetId = Guid.NewGuid();
        await store.UpsertAsync(
            new KnowledgeNode(sourceId, KnowledgeNodeType.Fact, "source", [], [], DateTimeOffset.UtcNow),
            CancellationToken.None);
        await store.UpsertAsync(
            new KnowledgeNode(lessonTargetId, KnowledgeNodeType.Lesson, "a lesson", [], [], DateTimeOffset.UtcNow),
            CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() => client.AddRelationshipAsync(
            sourceId,
            new RelationshipEdge { TargetNodeId = lessonTargetId, RelationshipType = RelationshipType.DependsOn },
            CancellationToken.None));
    }

    private sealed class CapturingKnowledgeClassifiedEventPublisher : IKnowledgeClassifiedEventPublisher
    {
        public Guid? LastNodeId { get; private set; }

        public TaxonomyClassification? LastTaxonomyType { get; private set; }

        public void PublishKnowledgeClassified(Guid nodeId, TaxonomyClassification taxonomyType)
        {
            LastNodeId = nodeId;
            LastTaxonomyType = taxonomyType;
        }
    }

    private sealed class CapturingKnowledgeRelationshipAddedEventPublisher : IKnowledgeRelationshipAddedEventPublisher
    {
        public Guid? LastSourceNodeId { get; private set; }

        public Guid? LastTargetNodeId { get; private set; }

        public RelationshipType? LastRelationshipType { get; private set; }

        public void PublishKnowledgeRelationshipAdded(Guid sourceNodeId, Guid targetNodeId, RelationshipType relationshipType)
        {
            LastSourceNodeId = sourceNodeId;
            LastTargetNodeId = targetNodeId;
            LastRelationshipType = relationshipType;
        }
    }
}
