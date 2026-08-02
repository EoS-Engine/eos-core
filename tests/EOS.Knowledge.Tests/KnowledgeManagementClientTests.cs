using EOS.Contracts;
using EOS.KnowledgeGraph;
using EOS.VectorStore;

namespace EOS.Knowledge.Tests;

public class KnowledgeManagementClientTests
{
    private static readonly RankingWeights DefaultRankingWeights = new(
        VectorSimilarity: 0.4, Recency: 0.3, DomainMatch: 0.2, AccessFrequency: 0.1);

    private static readonly KnowledgeRankingWeights DefaultKnowledgeRankingWeights = new(
        Confidence: 0.4, Reliability: 0.3, RelationshipRelevance: 0.2, DeprecationPenalty: 0.5);

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("EOS_SQLSERVER_CONNECTION_STRING")
        ?? throw new InvalidOperationException("EOS_SQLSERVER_CONNECTION_STRING is not set.");

    private static string ChromaDbEndpoint =>
        Environment.GetEnvironmentVariable("EOS_CHROMADB_ENDPOINT")
        ?? throw new InvalidOperationException("EOS_CHROMADB_ENDPOINT is not set.");

    private static async Task<(KnowledgeGraphStore Store, KnowledgeManagementClient Client)> CreateAsync(
        CapturingKnowledgeClassifiedEventPublisher? classifiedPublisher = null,
        CapturingKnowledgeRelationshipAddedEventPublisher? relationshipPublisher = null,
        CapturingKnowledgeQualityUpdatedEventPublisher? qualityUpdatedPublisher = null,
        CapturingKnowledgeGovernanceActionRequestedEventPublisher? governanceRequestedPublisher = null,
        CapturingKnowledgeGovernanceActionAppliedEventPublisher? governanceAppliedPublisher = null,
        CapturingKnowledgeFreshnessExpiredEventPublisher? freshnessExpiredPublisher = null,
        CapturingKnowledgeDuplicateFlaggedEventPublisher? duplicateFlaggedPublisher = null,
        IProtectionClient? protectionClient = null,
        double freshnessExpirationThreshold = 0.25)
    {
        var store = new KnowledgeGraphStore(ConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        IKnowledgeClient knowledgeClient = new KnowledgeClient(
            store, DefaultRankingWeights, new ChromaVectorStore(ChromaDbEndpoint), NeverCalledMemorySourceStore.Instance);
        var ontologyValidator = new OntologyValidator(
            store,
            dependsOnDisallowedTargetTypes: [KnowledgeNodeType.Lesson],
            governanceApprovalRequiredRelationshipTypes: [RelationshipType.Replaces, RelationshipType.Supersedes]);
        var freshnessCalculator = new FreshnessCalculator(
            decayHalfLifeDays: 90, typeWeights: new Dictionary<TaxonomyClassification, double>());
        var duplicateDetector = new DuplicateDetector(store, new NeverSimilarCompareProviderStub());
        var client = new KnowledgeManagementClient(
            store,
            knowledgeClient,
            ontologyValidator,
            freshnessCalculator,
            duplicateDetector,
            protectionClient ?? new AlwaysAllowProtectionClient(),
            DefaultKnowledgeRankingWeights,
            freshnessExpirationThreshold,
            classifiedPublisher,
            relationshipPublisher,
            qualityUpdatedPublisher,
            governanceRequestedPublisher,
            governanceAppliedPublisher,
            freshnessExpiredPublisher,
            duplicateFlaggedPublisher);
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

    [Fact]
    public async Task GetQualityAsync_ComputesFreshnessAndCompleteness_AndPublishesTheEvent()
    {
        var qualityUpdatedPublisher = new CapturingKnowledgeQualityUpdatedEventPublisher();
        var (store, client) = await CreateAsync(qualityUpdatedPublisher: qualityUpdatedPublisher);
        var nodeId = Guid.NewGuid();
        await store.UpsertAsync(
            new KnowledgeNode(nodeId, KnowledgeNodeType.Fact, "a fact", [], [], DateTimeOffset.UtcNow)
            {
                Metadata = new KnowledgeMetadata { Owner = "role:qa", LastValidation = DateTimeOffset.UtcNow },
            },
            CancellationToken.None);

        var quality = await client.GetQualityAsync(nodeId, CancellationToken.None);

        Assert.NotNull(quality);
        Assert.NotNull(quality.Freshness);
        Assert.True(quality.Freshness > 0.9);
        Assert.NotNull(quality.Completeness);
        Assert.Equal(nodeId, qualityUpdatedPublisher.LastNodeId);
    }

    [Fact]
    public async Task GetQualityAsync_ReturnsNull_ForANeverCreatedNode()
    {
        var (_, client) = await CreateAsync();

        var quality = await client.GetQualityAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(quality);
    }

    [Fact]
    public async Task GetQualityAsync_PublishesFreshnessExpired_WhenFreshnessFallsBelowThreshold()
    {
        var freshnessExpiredPublisher = new CapturingKnowledgeFreshnessExpiredEventPublisher();
        var (store, client) = await CreateAsync(
            freshnessExpiredPublisher: freshnessExpiredPublisher, freshnessExpirationThreshold: 0.5);
        var nodeId = Guid.NewGuid();
        await store.UpsertAsync(
            new KnowledgeNode(nodeId, KnowledgeNodeType.Fact, "a fact", [], [], DateTimeOffset.UtcNow),
            CancellationToken.None);

        await client.GetQualityAsync(nodeId, CancellationToken.None);

        Assert.Equal(nodeId, freshnessExpiredPublisher.LastNodeId);
    }

    [Fact]
    public async Task GetQualityAsync_DoesNotRepublishFreshnessExpired_OnASecondCallWhileStillStale()
    {
        var freshnessExpiredPublisher = new CapturingKnowledgeFreshnessExpiredEventPublisher();
        var (store, client) = await CreateAsync(
            freshnessExpiredPublisher: freshnessExpiredPublisher, freshnessExpirationThreshold: 0.5);
        var nodeId = Guid.NewGuid();
        await store.UpsertAsync(
            new KnowledgeNode(nodeId, KnowledgeNodeType.Fact, "a fact", [], [], DateTimeOffset.UtcNow),
            CancellationToken.None);

        await client.GetQualityAsync(nodeId, CancellationToken.None);
        await client.GetQualityAsync(nodeId, CancellationToken.None);

        Assert.Equal(1, freshnessExpiredPublisher.CallCount);
    }

    [Fact]
    public async Task GetQualityAsync_RepublishesFreshnessExpired_OnASecondTransitionAfterRecovery()
    {
        var freshnessExpiredPublisher = new CapturingKnowledgeFreshnessExpiredEventPublisher();
        var (store, client) = await CreateAsync(
            freshnessExpiredPublisher: freshnessExpiredPublisher, freshnessExpirationThreshold: 0.5);
        var nodeId = Guid.NewGuid();
        await store.UpsertAsync(
            new KnowledgeNode(nodeId, KnowledgeNodeType.Fact, "a fact", [], [], DateTimeOffset.UtcNow),
            CancellationToken.None);

        await client.GetQualityAsync(nodeId, CancellationToken.None);

        var recoveredNode = await store.GetByIdAsync(nodeId, CancellationToken.None);
        await store.UpsertAsync(
            recoveredNode! with { Metadata = recoveredNode.Metadata! with { LastValidation = DateTimeOffset.UtcNow } },
            CancellationToken.None);
        await client.GetQualityAsync(nodeId, CancellationToken.None);

        var staleAgainNode = await store.GetByIdAsync(nodeId, CancellationToken.None);
        await store.UpsertAsync(
            staleAgainNode! with
            {
                Metadata = staleAgainNode.Metadata! with { LastValidation = DateTimeOffset.UtcNow.AddDays(-1000) },
            },
            CancellationToken.None);
        await client.GetQualityAsync(nodeId, CancellationToken.None);

        Assert.Equal(2, freshnessExpiredPublisher.CallCount);
    }

    [Fact]
    public async Task SearchAsync_RanksHigherConfidenceItemFirst_WhenWeightingIsIndependent()
    {
        var (store, client) = await CreateAsync();
        var highConfidenceId = Guid.NewGuid();
        var lowConfidenceId = Guid.NewGuid();
        await store.UpsertAsync(
            new KnowledgeNode(highConfidenceId, KnowledgeNodeType.Fact, "high", [], [], DateTimeOffset.UtcNow)
            {
                Metadata = new KnowledgeMetadata { Quality = new QualityProfile { Confidence = 0.9, Reliability = 0.0 } },
            },
            CancellationToken.None);
        await store.UpsertAsync(
            new KnowledgeNode(lowConfidenceId, KnowledgeNodeType.Fact, "low", [], [], DateTimeOffset.UtcNow)
            {
                Metadata = new KnowledgeMetadata { Quality = new QualityProfile { Confidence = 0.1, Reliability = 0.0 } },
            },
            CancellationToken.None);

        var results = await client.SearchAsync(new SearchRequest { Type = MemoryType.Semantic }, CancellationToken.None);

        var ordered = results.Where(r => r.Node.NodeId == highConfidenceId || r.Node.NodeId == lowConfidenceId).ToList();
        Assert.Equal(highConfidenceId, ordered[0].Node.NodeId);
        Assert.Equal(lowConfidenceId, ordered[1].Node.NodeId);
    }

    [Fact]
    public async Task SearchAsync_DownRanksDeprecatedItem_IndependentlyOfConfidence()
    {
        var (store, client) = await CreateAsync();
        var deprecatedId = Guid.NewGuid();
        var publishedId = Guid.NewGuid();
        await store.UpsertAsync(
            new KnowledgeNode(deprecatedId, KnowledgeNodeType.Fact, "deprecated", [], [], DateTimeOffset.UtcNow)
            {
                Metadata = new KnowledgeMetadata
                {
                    Quality = new QualityProfile { Confidence = 0.9 },
                    LifecycleState = KnowledgeLifecycleState.Deprecation,
                },
            },
            CancellationToken.None);
        await store.UpsertAsync(
            new KnowledgeNode(publishedId, KnowledgeNodeType.Fact, "published", [], [], DateTimeOffset.UtcNow)
            {
                Metadata = new KnowledgeMetadata { Quality = new QualityProfile { Confidence = 0.9 } },
            },
            CancellationToken.None);

        var results = await client.SearchAsync(new SearchRequest { Type = MemoryType.Semantic }, CancellationToken.None);

        var ordered = results.Where(r => r.Node.NodeId == deprecatedId || r.Node.NodeId == publishedId).ToList();
        Assert.Equal(publishedId, ordered[0].Node.NodeId);
        Assert.Equal(deprecatedId, ordered[1].Node.NodeId);
    }

    [Fact]
    public async Task RequestGovernanceActionAsync_AppliesLifecycleChangeAndNewVersion_WhenProtectionAllows()
    {
        var requestedPublisher = new CapturingKnowledgeGovernanceActionRequestedEventPublisher();
        var appliedPublisher = new CapturingKnowledgeGovernanceActionAppliedEventPublisher();
        var (store, client) = await CreateAsync(
            governanceRequestedPublisher: requestedPublisher, governanceAppliedPublisher: appliedPublisher);
        var nodeId = Guid.NewGuid();
        await store.UpsertAsync(
            new KnowledgeNode(nodeId, KnowledgeNodeType.Fact, "a fact", [], [], DateTimeOffset.UtcNow),
            CancellationToken.None);

        var result = await client.RequestGovernanceActionAsync(
            nodeId, GovernanceActionType.Deprecation, "no longer accurate", CancellationToken.None);

        Assert.Equal(ProtectionVerdict.Allow, result.Verdict);
        var persisted = await store.GetByIdAsync(nodeId, CancellationToken.None);
        Assert.Equal(KnowledgeLifecycleState.Deprecation, persisted!.Metadata!.LifecycleState);
        var version = Assert.Single(persisted.Metadata.VersionHistory);
        Assert.Equal(1, version.Version);
        Assert.Equal(nodeId, requestedPublisher.LastNodeId);
        Assert.Equal(1, appliedPublisher.LastNewVersion);
    }

    [Fact]
    public async Task RequestGovernanceActionAsync_DoesNotApplyTheChange_WhenProtectionDenies()
    {
        var appliedPublisher = new CapturingKnowledgeGovernanceActionAppliedEventPublisher();
        var (store, client) = await CreateAsync(
            governanceAppliedPublisher: appliedPublisher, protectionClient: new AlwaysDenyProtectionClient());
        var nodeId = Guid.NewGuid();
        await store.UpsertAsync(
            new KnowledgeNode(nodeId, KnowledgeNodeType.Fact, "a fact", [], [], DateTimeOffset.UtcNow),
            CancellationToken.None);

        var result = await client.RequestGovernanceActionAsync(
            nodeId, GovernanceActionType.Retirement, "mistaken content", CancellationToken.None);

        Assert.Equal(ProtectionVerdict.Deny, result.Verdict);
        var persisted = await store.GetByIdAsync(nodeId, CancellationToken.None);
        Assert.Null(persisted!.Metadata);
        Assert.Null(appliedPublisher.LastNodeId);
    }

    [Fact]
    public async Task RequestGovernanceActionAsync_Throws_WhenNodeDoesNotExist()
    {
        var (_, client) = await CreateAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => client.RequestGovernanceActionAsync(
            Guid.NewGuid(), GovernanceActionType.Update, "justification", CancellationToken.None));
    }

    [Fact]
    public async Task FindDuplicatesAsync_ReturnsStructuralMatch_AndPublishesTheEvent()
    {
        // The KnowledgeNode table is shared, uncleaned state across this whole test class (same
        // characteristic every other test here already relies on, e.g. random Guids to avoid
        // collisions) — a unique-per-run domain tag is used so this test's structural match is
        // never confused with unrelated Fact/Facts rows left behind by other tests.
        var uniqueTag = $"test-tag-{Guid.NewGuid()}";
        var duplicateFlaggedPublisher = new CapturingKnowledgeDuplicateFlaggedEventPublisher();
        var (store, client) = await CreateAsync(duplicateFlaggedPublisher: duplicateFlaggedPublisher);
        var nodeId = Guid.NewGuid();
        var duplicateId = Guid.NewGuid();
        await store.UpsertAsync(
            new KnowledgeNode(nodeId, KnowledgeNodeType.Fact, "original", [uniqueTag], [], DateTimeOffset.UtcNow)
            {
                Metadata = new KnowledgeMetadata { Taxonomy = TaxonomyClassification.Facts },
            },
            CancellationToken.None);
        await store.UpsertAsync(
            new KnowledgeNode(duplicateId, KnowledgeNodeType.Fact, "likely duplicate", [uniqueTag], [], DateTimeOffset.UtcNow)
            {
                Metadata = new KnowledgeMetadata { Taxonomy = TaxonomyClassification.Facts },
            },
            CancellationToken.None);

        var duplicates = await client.FindDuplicatesAsync(nodeId, CancellationToken.None);

        var candidate = Assert.Single(duplicates);
        Assert.Equal(duplicateId, candidate.NodeId);
        Assert.Equal("structural", candidate.SimilaritySource);
        Assert.Equal(1, duplicateFlaggedPublisher.CallCount);
    }

    [Fact]
    public async Task FindDuplicatesAsync_ExcludesCandidate_WhenNoStructuralSignalIsShared()
    {
        var uniqueTag = $"test-tag-{Guid.NewGuid()}";
        var duplicateFlaggedPublisher = new CapturingKnowledgeDuplicateFlaggedEventPublisher();
        var (store, client) = await CreateAsync(duplicateFlaggedPublisher: duplicateFlaggedPublisher);
        var nodeId = Guid.NewGuid();
        var unrelatedId = Guid.NewGuid();
        await store.UpsertAsync(
            new KnowledgeNode(nodeId, KnowledgeNodeType.Fact, "original", [uniqueTag], [], DateTimeOffset.UtcNow)
            {
                Metadata = new KnowledgeMetadata { Taxonomy = TaxonomyClassification.Facts },
            },
            CancellationToken.None);
        await store.UpsertAsync(
            new KnowledgeNode(unrelatedId, KnowledgeNodeType.Fact, "unrelated", ["mobile"], [], DateTimeOffset.UtcNow)
            {
                Metadata = new KnowledgeMetadata { Taxonomy = TaxonomyClassification.Patterns },
            },
            CancellationToken.None);

        var duplicates = await client.FindDuplicatesAsync(nodeId, CancellationToken.None);

        Assert.DoesNotContain(duplicates, candidate => candidate.NodeId == unrelatedId);
        Assert.Equal(0, duplicateFlaggedPublisher.CallCount);
    }

    [Fact]
    public async Task FindDuplicatesAsync_ExcludesCandidate_WhenOnlyTaxonomyIsShared_NotDomainTag()
    {
        var uniqueTag = $"test-tag-{Guid.NewGuid()}";
        var duplicateFlaggedPublisher = new CapturingKnowledgeDuplicateFlaggedEventPublisher();
        var (store, client) = await CreateAsync(duplicateFlaggedPublisher: duplicateFlaggedPublisher);
        var nodeId = Guid.NewGuid();
        var taxonomyOnlyId = Guid.NewGuid();
        await store.UpsertAsync(
            new KnowledgeNode(nodeId, KnowledgeNodeType.Fact, "original", [uniqueTag], [], DateTimeOffset.UtcNow)
            {
                Metadata = new KnowledgeMetadata { Taxonomy = TaxonomyClassification.Facts },
            },
            CancellationToken.None);
        await store.UpsertAsync(
            new KnowledgeNode(taxonomyOnlyId, KnowledgeNodeType.Fact, "shares taxonomy only", ["mobile"], [], DateTimeOffset.UtcNow)
            {
                Metadata = new KnowledgeMetadata { Taxonomy = TaxonomyClassification.Facts },
            },
            CancellationToken.None);

        var duplicates = await client.FindDuplicatesAsync(nodeId, CancellationToken.None);

        Assert.DoesNotContain(duplicates, candidate => candidate.NodeId == taxonomyOnlyId);
        Assert.Equal(0, duplicateFlaggedPublisher.CallCount);
    }

    [Fact]
    public async Task FindDuplicatesAsync_ExcludesCandidate_WhenOnlyDomainTagIsShared_NotTaxonomy()
    {
        var uniqueTag = $"test-tag-{Guid.NewGuid()}";
        var duplicateFlaggedPublisher = new CapturingKnowledgeDuplicateFlaggedEventPublisher();
        var (store, client) = await CreateAsync(duplicateFlaggedPublisher: duplicateFlaggedPublisher);
        var nodeId = Guid.NewGuid();
        var domainTagOnlyId = Guid.NewGuid();
        await store.UpsertAsync(
            new KnowledgeNode(nodeId, KnowledgeNodeType.Fact, "original", [uniqueTag], [], DateTimeOffset.UtcNow)
            {
                Metadata = new KnowledgeMetadata { Taxonomy = TaxonomyClassification.Facts },
            },
            CancellationToken.None);
        await store.UpsertAsync(
            new KnowledgeNode(domainTagOnlyId, KnowledgeNodeType.Fact, "shares domain tag only", [uniqueTag], [], DateTimeOffset.UtcNow)
            {
                Metadata = new KnowledgeMetadata { Taxonomy = TaxonomyClassification.Patterns },
            },
            CancellationToken.None);

        var duplicates = await client.FindDuplicatesAsync(nodeId, CancellationToken.None);

        Assert.DoesNotContain(duplicates, candidate => candidate.NodeId == domainTagOnlyId);
        Assert.Equal(0, duplicateFlaggedPublisher.CallCount);
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

    private sealed class CapturingKnowledgeQualityUpdatedEventPublisher : IKnowledgeQualityUpdatedEventPublisher
    {
        public Guid? LastNodeId { get; private set; }

        public QualityProfile? LastQualityProfile { get; private set; }

        public void PublishKnowledgeQualityUpdated(Guid nodeId, QualityProfile qualityProfile)
        {
            LastNodeId = nodeId;
            LastQualityProfile = qualityProfile;
        }
    }

    private sealed class CapturingKnowledgeGovernanceActionRequestedEventPublisher : IKnowledgeGovernanceActionRequestedEventPublisher
    {
        public Guid? LastNodeId { get; private set; }

        public GovernanceActionType? LastActionType { get; private set; }

        public void PublishKnowledgeGovernanceActionRequested(Guid nodeId, GovernanceActionType actionType, string requestedBy)
        {
            LastNodeId = nodeId;
            LastActionType = actionType;
        }
    }

    private sealed class CapturingKnowledgeGovernanceActionAppliedEventPublisher : IKnowledgeGovernanceActionAppliedEventPublisher
    {
        public Guid? LastNodeId { get; private set; }

        public int? LastNewVersion { get; private set; }

        public void PublishKnowledgeGovernanceActionApplied(Guid nodeId, GovernanceActionType actionType, int newVersion)
        {
            LastNodeId = nodeId;
            LastNewVersion = newVersion;
        }
    }

    private sealed class CapturingKnowledgeFreshnessExpiredEventPublisher : IKnowledgeFreshnessExpiredEventPublisher
    {
        public Guid? LastNodeId { get; private set; }

        public double? LastFreshnessScore { get; private set; }

        public int CallCount { get; private set; }

        public void PublishKnowledgeFreshnessExpired(Guid nodeId, double freshnessScore)
        {
            LastNodeId = nodeId;
            LastFreshnessScore = freshnessScore;
            CallCount++;
        }
    }

    private sealed class CapturingKnowledgeDuplicateFlaggedEventPublisher : IKnowledgeDuplicateFlaggedEventPublisher
    {
        public int CallCount { get; private set; }

        public void PublishKnowledgeDuplicateFlagged(Guid nodeIdA, Guid nodeIdB, string similaritySource) => CallCount++;
    }

    private sealed class NeverSimilarCompareProviderStub : ICompareProvider
    {
        public Task<bool> AreSimilarAsync(string contentA, string contentB, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class AlwaysAllowProtectionClient : IProtectionClient
    {
        public ValidationResult Validate(ActionRequest action) => new(ProtectionVerdict.Allow, RiskTier.Low, Reason: null);
    }

    private sealed class AlwaysDenyProtectionClient : IProtectionClient
    {
        public ValidationResult Validate(ActionRequest action) => new(ProtectionVerdict.Deny, RiskTier.High, "denied for test");
    }
}
