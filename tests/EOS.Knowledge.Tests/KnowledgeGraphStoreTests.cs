using EOS.KnowledgeGraph;

namespace EOS.Knowledge.Tests;

public class KnowledgeGraphStoreTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("EOS_SQLSERVER_CONNECTION_STRING")
        ?? throw new InvalidOperationException("EOS_SQLSERVER_CONNECTION_STRING is not set.");

    private static KnowledgeNode CreateNode(Guid nodeId, string content = "content")
    {
        return new KnowledgeNode(
            NodeId: nodeId,
            NodeType: KnowledgeNodeType.Fact,
            Content: content,
            DomainTags: ["backend", "mobile"],
            EvidenceRefs: ["artifact://evidence/1"],
            CreatedAt: DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task EnsureTableExistsAsync_IsIdempotent()
    {
        var store = new KnowledgeGraphStore(ConnectionString);

        await store.EnsureTableExistsAsync(CancellationToken.None);
        await store.EnsureTableExistsAsync(CancellationToken.None);
    }

    [Fact]
    public async Task UpsertAsync_InsertsANewRow_WhenNodeIdIsUnseen()
    {
        var store = new KnowledgeGraphStore(ConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        var node = CreateNode(Guid.NewGuid());

        await store.UpsertAsync(node, CancellationToken.None);
        var persisted = await store.GetByIdAsync(node.NodeId, CancellationToken.None);

        Assert.NotNull(persisted);
        Assert.Equal(node.NodeId, persisted.NodeId);
        Assert.Equal(node.NodeType, persisted.NodeType);
        Assert.Equal(node.Content, persisted.Content);
        Assert.Equal(node.DomainTags, persisted.DomainTags);
        Assert.Equal(node.EvidenceRefs, persisted.EvidenceRefs);
    }

    [Fact]
    public async Task UpsertAsync_UpdatesExistingRow_WhenNodeIdAlreadyExists()
    {
        var store = new KnowledgeGraphStore(ConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        var nodeId = Guid.NewGuid();
        await store.UpsertAsync(CreateNode(nodeId, "original content"), CancellationToken.None);

        await store.UpsertAsync(CreateNode(nodeId, "updated content"), CancellationToken.None);
        var persisted = await store.GetByIdAsync(nodeId, CancellationToken.None);

        Assert.NotNull(persisted);
        Assert.Equal("updated content", persisted.Content);
    }

    [Fact]
    public async Task UpsertAsync_NeverRewritesCreatedAt_OnUpdate()
    {
        var store = new KnowledgeGraphStore(ConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        var nodeId = Guid.NewGuid();
        await store.UpsertAsync(CreateNode(nodeId), CancellationToken.None);
        var originalPersisted = await store.GetByIdAsync(nodeId, CancellationToken.None);

        await Task.Delay(50);
        await store.UpsertAsync(CreateNode(nodeId, "changed content"), CancellationToken.None);
        var updatedPersisted = await store.GetByIdAsync(nodeId, CancellationToken.None);

        Assert.NotNull(originalPersisted);
        Assert.NotNull(updatedPersisted);
        Assert.Equal(originalPersisted.CreatedAt, updatedPersisted.CreatedAt);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNodeDoesNotExist()
    {
        var store = new KnowledgeGraphStore(ConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);

        var result = await store.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }
}
