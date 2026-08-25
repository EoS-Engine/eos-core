using System.Text.Json;
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

        using var document = JsonDocument.Parse(rawJson);
        var root = document.RootElement;
        Assert.Equal("Facts", root.GetProperty("Taxonomy").GetString());
        Assert.Equal("Supports", root.GetProperty("Relationships")[0].GetProperty("RelationshipType").GetString());
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

    // ADR-005: proves KnowledgeGraphStore.QueryAsync's maxResults parameter is the actual
    // resource-safety enforcement point for Finding #2 — never returns more rows than requested,
    // even when the matching corpus is larger.
    [Fact]
    public async Task QueryAsync_NeverReturnsMoreThanMaxResults_WhenTheCorpusExceedsIt()
    {
        var store = new KnowledgeGraphStore(ConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        const int maxResults = 5;
        var nodeIds = Enumerable.Range(0, maxResults + 5).Select(_ => Guid.NewGuid()).ToArray();
        try
        {
            foreach (var nodeId in nodeIds)
            {
                await store.UpsertAsync(CreateNode(nodeId), CancellationToken.None);
            }

            var results = await store.QueryAsync(
                [KnowledgeNodeType.Fact], null, null, CancellationToken.None, maxResults);

            Assert.Equal(maxResults, results.Count);
        }
        finally
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync(CancellationToken.None);
            foreach (var nodeId in nodeIds)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM KnowledgeNode WHERE NodeId = @NodeId";
                command.Parameters.AddWithValue("@NodeId", nodeId);
                await command.ExecuteNonQueryAsync(CancellationToken.None);
            }
        }
    }

    // ADR-005: proves the ORDER BY CreatedAt DESC applied alongside maxResults is genuinely
    // necessary and deterministic — without it, a just-created row can fall outside an unordered
    // TOP against a large pre-existing same-NodeType corpus (this is exactly what this WP's
    // implementation pass observed against this repository's accumulated test data).
    [Fact]
    public async Task QueryAsync_WithMaxResults_AlwaysIncludesTheMostRecentlyCreatedMatchingNode()
    {
        var store = new KnowledgeGraphStore(ConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        const int maxResults = 3;
        var olderNodeIds = Enumerable.Range(0, maxResults + 5)
            .Select(_ => Guid.NewGuid()).ToArray();
        var mostRecentNodeId = Guid.NewGuid();
        var allNodeIds = olderNodeIds.Append(mostRecentNodeId).ToArray();
        try
        {
            foreach (var nodeId in olderNodeIds)
            {
                await store.UpsertAsync(CreateNode(nodeId), CancellationToken.None);
            }

            // The node under test must be provably the newest in this NodeType's corpus, not
            // merely inserted last — CreatedAt (not insertion order) is what ORDER BY relies on.
            await store.UpsertAsync(
                new KnowledgeNode(
                    mostRecentNodeId, KnowledgeNodeType.Fact, "content", ["backend", "mobile"],
                    ["artifact://evidence/1"], DateTimeOffset.UtcNow.AddDays(1)),
                CancellationToken.None);

            var results = await store.QueryAsync(
                [KnowledgeNodeType.Fact], null, null, CancellationToken.None, maxResults);

            Assert.Contains(results, node => node.NodeId == mostRecentNodeId);
        }
        finally
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync(CancellationToken.None);
            foreach (var nodeId in allNodeIds)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM KnowledgeNode WHERE NodeId = @NodeId";
                command.Parameters.AddWithValue("@NodeId", nodeId);
                await command.ExecuteNonQueryAsync(CancellationToken.None);
            }
        }
    }

    // CodeRabbit PR #28 follow-up finding: ORDER BY CreatedAt DESC alone has no tie-breaker, so
    // rows sharing an identical CreatedAt had no guaranteed stable order within a bounded TOP
    // selection. Proves ORDER BY CreatedAt DESC, NodeId ASC makes that tie deterministic. The
    // expected winner is established independently via SQL Server's own NodeId ordering (a raw
    // ORDER BY NodeId ASC query) rather than .NET's Guid.CompareTo, since .NET's default GUID
    // byte ordering does not match SQL Server's uniqueidentifier sort order.
    [Fact]
    public async Task QueryAsync_WithMaxResults_BreaksIdenticalCreatedAtTies_ByNodeIdAscending()
    {
        var store = new KnowledgeGraphStore(ConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        var tiedCreatedAt = DateTimeOffset.UtcNow;
        var tiedNodeIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        try
        {
            foreach (var nodeId in tiedNodeIds)
            {
                await store.UpsertAsync(
                    new KnowledgeNode(nodeId, KnowledgeNodeType.Fact, "content", ["backend", "mobile"],
                        ["artifact://evidence/1"], tiedCreatedAt),
                    CancellationToken.None);
            }

            Guid expectedWinner;
            await using (var connection = new SqlConnection(ConnectionString))
            {
                await connection.OpenAsync(CancellationToken.None);
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT TOP 1 NodeId FROM KnowledgeNode
                    WHERE NodeId IN (@Id0, @Id1, @Id2)
                    ORDER BY NodeId ASC
                    """;
                command.Parameters.AddWithValue("@Id0", tiedNodeIds[0]);
                command.Parameters.AddWithValue("@Id1", tiedNodeIds[1]);
                command.Parameters.AddWithValue("@Id2", tiedNodeIds[2]);
                expectedWinner = (Guid)(await command.ExecuteScalarAsync(CancellationToken.None))!;
            }

            var results = await store.QueryAsync(
                [KnowledgeNodeType.Fact], null, null, CancellationToken.None, maxResults: 1);

            Assert.Single(results);
            Assert.Equal(expectedWinner, results[0].NodeId);
        }
        finally
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync(CancellationToken.None);
            foreach (var nodeId in tiedNodeIds)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM KnowledgeNode WHERE NodeId = @NodeId";
                command.Parameters.AddWithValue("@NodeId", nodeId);
                await command.ExecuteNonQueryAsync(CancellationToken.None);
            }
        }
    }
}
