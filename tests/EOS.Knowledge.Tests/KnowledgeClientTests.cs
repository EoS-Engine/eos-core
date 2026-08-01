using EOS.KnowledgeGraph;
using EOS.VectorStore;

namespace EOS.Knowledge.Tests;

public class KnowledgeClientTests
{
    private static readonly RankingWeights DefaultRankingWeights = new(
        VectorSimilarity: 0.4, Recency: 0.3, DomainMatch: 0.2, AccessFrequency: 0.1);

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("EOS_SQLSERVER_CONNECTION_STRING")
        ?? throw new InvalidOperationException("EOS_SQLSERVER_CONNECTION_STRING is not set.");

    private static string ChromaDbEndpoint =>
        Environment.GetEnvironmentVariable("EOS_CHROMADB_ENDPOINT")
        ?? throw new InvalidOperationException("EOS_CHROMADB_ENDPOINT is not set.");

    [Fact]
    public async Task UpdateAsync_PersistsASchemaValidKnowledgeNode_VerifiedThroughTheStoreDirectly()
    {
        var store = new KnowledgeGraphStore(ConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        IKnowledgeClient client = new KnowledgeClient(
            store, DefaultRankingWeights, new ChromaVectorStore(ChromaDbEndpoint), NeverCalledMemorySourceStore.Instance);
        var nodeId = Guid.NewGuid();

        await client.UpdateAsync(
            nodeId,
            KnowledgeNodeType.Fact,
            "The vertical slice's interaction",
            ["backend"],
            ["artifact://evidence/1"],
            cancellationToken: CancellationToken.None);

        var persisted = await store.GetByIdAsync(nodeId, CancellationToken.None);

        Assert.NotNull(persisted);
        Assert.Equal(nodeId, persisted.NodeId);
        Assert.Equal(KnowledgeNodeType.Fact, persisted.NodeType);
        Assert.Equal("The vertical slice's interaction", persisted.Content);
        Assert.Equal(["backend"], persisted.DomainTags);
        Assert.Equal(["artifact://evidence/1"], persisted.EvidenceRefs);
    }

    [Fact]
    public async Task UpdateAsync_UpsertsThroughTheInterface_WhenCalledTwiceForTheSameNodeId()
    {
        var store = new KnowledgeGraphStore(ConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        IKnowledgeClient client = new KnowledgeClient(
            store, DefaultRankingWeights, new ChromaVectorStore(ChromaDbEndpoint), NeverCalledMemorySourceStore.Instance);
        var nodeId = Guid.NewGuid();

        await client.UpdateAsync(nodeId, KnowledgeNodeType.Fact, "first", [], [], cancellationToken: CancellationToken.None);
        await client.UpdateAsync(nodeId, KnowledgeNodeType.Fact, "second", [], [], cancellationToken: CancellationToken.None);
        var persisted = await store.GetByIdAsync(nodeId, CancellationToken.None);

        Assert.NotNull(persisted);
        Assert.Equal("second", persisted.Content);
    }

    [Fact]
    public async Task UpdateAsync_PreservesExistingMetadata_WhenCalledAgainWithoutSpecifyingIt()
    {
        var store = new KnowledgeGraphStore(ConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        IKnowledgeClient client = new KnowledgeClient(
            store, DefaultRankingWeights, new ChromaVectorStore(ChromaDbEndpoint), NeverCalledMemorySourceStore.Instance);
        var nodeId = Guid.NewGuid();
        var metadata = new KnowledgeMetadata { Taxonomy = TaxonomyClassification.Facts };

        await client.UpdateAsync(
            nodeId, KnowledgeNodeType.Fact, "first", [], [], metadata, CancellationToken.None);
        await client.UpdateAsync(
            nodeId, KnowledgeNodeType.Fact, "second", [], [], cancellationToken: CancellationToken.None);
        var persisted = await store.GetByIdAsync(nodeId, CancellationToken.None);

        Assert.NotNull(persisted);
        Assert.Equal("second", persisted.Content);
        Assert.NotNull(persisted.Metadata);
        Assert.Equal(TaxonomyClassification.Facts, persisted.Metadata.Taxonomy);
    }
}
