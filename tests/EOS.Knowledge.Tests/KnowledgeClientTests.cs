using EOS.KnowledgeGraph;

namespace EOS.Knowledge.Tests;

public class KnowledgeClientTests
{
    private static readonly RankingWeights DefaultRankingWeights = new(
        VectorSimilarity: 0.4, Recency: 0.3, DomainMatch: 0.2, AccessFrequency: 0.1);

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("EOS_SQLSERVER_CONNECTION_STRING")
        ?? throw new InvalidOperationException("EOS_SQLSERVER_CONNECTION_STRING is not set.");

    [Fact]
    public async Task UpdateAsync_PersistsASchemaValidKnowledgeNode_VerifiedThroughTheStoreDirectly()
    {
        var store = new KnowledgeGraphStore(ConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        IKnowledgeClient client = new KnowledgeClient(store, DefaultRankingWeights);
        var nodeId = Guid.NewGuid();

        await client.UpdateAsync(
            nodeId,
            KnowledgeNodeType.Fact,
            "The vertical slice's interaction",
            ["backend"],
            ["artifact://evidence/1"],
            CancellationToken.None);

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
        IKnowledgeClient client = new KnowledgeClient(store, DefaultRankingWeights);
        var nodeId = Guid.NewGuid();

        await client.UpdateAsync(nodeId, KnowledgeNodeType.Fact, "first", [], [], CancellationToken.None);
        await client.UpdateAsync(nodeId, KnowledgeNodeType.Fact, "second", [], [], CancellationToken.None);
        var persisted = await store.GetByIdAsync(nodeId, CancellationToken.None);

        Assert.NotNull(persisted);
        Assert.Equal("second", persisted.Content);
    }
}
