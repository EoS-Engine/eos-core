using EOS.KnowledgeGraph;
using Microsoft.Data.SqlClient;

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

    [Fact]
    public async Task UpsertAsync_ThenGetByIdAsync_RoundTripsKnowledgeMetadata()
    {
        var store = new KnowledgeGraphStore(ConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        var nodeId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var node = CreateNode(nodeId) with
        {
            Metadata = new KnowledgeMetadata
            {
                Taxonomy = TaxonomyClassification.Facts,
                Relationships =
                [
                    new RelationshipEdge { TargetNodeId = targetNodeId, RelationshipType = RelationshipType.Supports },
                ],
            },
        };

        await store.UpsertAsync(node, CancellationToken.None);
        var persisted = await store.GetByIdAsync(nodeId, CancellationToken.None);

        Assert.NotNull(persisted);
        Assert.NotNull(persisted.Metadata);
        Assert.Equal(TaxonomyClassification.Facts, persisted.Metadata.Taxonomy);
        Assert.Single(persisted.Metadata.Relationships);
        Assert.Equal(targetNodeId, persisted.Metadata.Relationships[0].TargetNodeId);
        Assert.Equal(RelationshipType.Supports, persisted.Metadata.Relationships[0].RelationshipType);
    }

    [Fact]
    public async Task UpsertAsync_ThenGetByIdAsync_RoundTripsNullMetadata_WhenNeverSet()
    {
        var store = new KnowledgeGraphStore(ConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        var node = CreateNode(Guid.NewGuid());

        await store.UpsertAsync(node, CancellationToken.None);
        var persisted = await store.GetByIdAsync(node.NodeId, CancellationToken.None);

        Assert.NotNull(persisted);
        Assert.Null(persisted.Metadata);
    }

    [Fact]
    public async Task UpsertAsync_PersistsTaxonomyAndRelationshipType_AsStringNames_NotNumericOrdinals()
    {
        var store = new KnowledgeGraphStore(ConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        var nodeId = Guid.NewGuid();
        var node = CreateNode(nodeId) with
        {
            Metadata = new KnowledgeMetadata
            {
                Taxonomy = TaxonomyClassification.Facts,
                Relationships =
                [
                    new RelationshipEdge { TargetNodeId = Guid.NewGuid(), RelationshipType = RelationshipType.Supports },
                ],
            },
        };
        await store.UpsertAsync(node, CancellationToken.None);

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT KnowledgeMetadataJson FROM KnowledgeNode WHERE NodeId = @NodeId";
        command.Parameters.AddWithValue("@NodeId", nodeId);
        var rawJson = (string)(await command.ExecuteScalarAsync(CancellationToken.None))!;

        Assert.Contains("\"Facts\"", rawJson);
        Assert.Contains("\"Supports\"", rawJson);
        Assert.DoesNotContain("\"taxonomy\":0", rawJson.Replace(" ", ""));
    }

    [Fact]
    public async Task QueryAsync_ReturnsEmpty_WhenNodeTypesIsEmpty()
    {
        var store = new KnowledgeGraphStore(ConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        await store.UpsertAsync(CreateNode(Guid.NewGuid()), CancellationToken.None);

        var results = await store.QueryAsync([], null, null, CancellationToken.None);

        Assert.Empty(results);
    }
}
