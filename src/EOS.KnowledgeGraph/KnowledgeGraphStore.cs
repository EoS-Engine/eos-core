using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;

namespace EOS.KnowledgeGraph;

public sealed class KnowledgeGraphStore(string connectionString)
{
    // TaxonomyClassification/RelationshipType (EOS.KnowledgeGraph) are persisted by name, not
    // ordinal — a future reordering or insertion of enum members must never silently reinterpret
    // already-persisted KnowledgeMetadataJson rows.
    private static readonly JsonSerializerOptions MetadataSerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task EnsureTableExistsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'KnowledgeNode')
            CREATE TABLE KnowledgeNode (
                NodeId UNIQUEIDENTIFIER PRIMARY KEY,
                NodeType NVARCHAR(50) NOT NULL,
                Content NVARCHAR(MAX) NOT NULL,
                DomainTagsJson NVARCHAR(MAX) NOT NULL,
                EvidenceRefsJson NVARCHAR(MAX) NOT NULL,
                CreatedAt DATETIMEOFFSET NOT NULL
            )
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('KnowledgeNode') AND name = 'KnowledgeMetadataJson')
            ALTER TABLE KnowledgeNode ADD KnowledgeMetadataJson NVARCHAR(MAX) NULL
            """;

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 2705 or 1913 or 2714)
        {
            // Benign race on the non-atomic IF NOT EXISTS guards above (identical class of
            // issue already found and fixed for ArchivedContentStore, WP-016): a concurrent
            // caller creating the same table/column/index loses the race but the object it
            // wanted now exists regardless of who created it. 2705: duplicate column.
        }
    }

    public async Task UpsertAsync(KnowledgeNode node, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF EXISTS (SELECT 1 FROM KnowledgeNode WHERE NodeId = @NodeId)
                UPDATE KnowledgeNode
                SET NodeType = @NodeType, Content = @Content, DomainTagsJson = @DomainTagsJson, EvidenceRefsJson = @EvidenceRefsJson, KnowledgeMetadataJson = @KnowledgeMetadataJson
                WHERE NodeId = @NodeId
            ELSE
                INSERT INTO KnowledgeNode (NodeId, NodeType, Content, DomainTagsJson, EvidenceRefsJson, CreatedAt, KnowledgeMetadataJson)
                VALUES (@NodeId, @NodeType, @Content, @DomainTagsJson, @EvidenceRefsJson, @CreatedAt, @KnowledgeMetadataJson)
            """;
        command.Parameters.AddWithValue("@NodeId", node.NodeId);
        command.Parameters.AddWithValue("@NodeType", node.NodeType.ToString());
        command.Parameters.AddWithValue("@Content", node.Content);
        command.Parameters.AddWithValue("@DomainTagsJson", JsonSerializer.Serialize(node.DomainTags));
        command.Parameters.AddWithValue("@EvidenceRefsJson", JsonSerializer.Serialize(node.EvidenceRefs));
        command.Parameters.AddWithValue("@CreatedAt", node.CreatedAt);
        command.Parameters.AddWithValue(
            "@KnowledgeMetadataJson",
            node.Metadata is null ? DBNull.Value : JsonSerializer.Serialize(node.Metadata, MetadataSerializerOptions));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Memory-Management-Specification-v1.0 §17.2's <c>replace_content()</c> step: updates only
    /// the <c>Content</c> column of an existing row, leaving <c>NodeType</c>, tags, evidence
    /// refs, and <c>CreatedAt</c> untouched — the compression event narrows storage footprint,
    /// it does not re-author the node's identity or provenance.
    /// </summary>
    public async Task ReplaceContentAsync(Guid nodeId, string newContent, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE KnowledgeNode SET Content = @Content WHERE NodeId = @NodeId
            """;
        command.Parameters.AddWithValue("@NodeId", nodeId);
        command.Parameters.AddWithValue("@Content", newContent);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<KnowledgeNode?> GetByIdAsync(Guid nodeId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT NodeId, NodeType, Content, DomainTagsJson, EvidenceRefsJson, CreatedAt, KnowledgeMetadataJson
            FROM KnowledgeNode
            WHERE NodeId = @NodeId
            """;
        command.Parameters.AddWithValue("@NodeId", nodeId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadNode(reader);
    }

    public async Task<IReadOnlyList<KnowledgeNode>> QueryAsync(
        IReadOnlyList<KnowledgeNodeType> nodeTypes,
        DateTimeOffset? createdFrom,
        DateTimeOffset? createdTo,
        CancellationToken cancellationToken,
        int? maxResults = null)
    {
        if (nodeTypes.Count == 0)
        {
            return [];
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var nodeTypeParameterNames = nodeTypes.Select((_, index) => $"@NodeType{index}").ToArray();
        // ADR-005: retrieval-resource safety is enforced here, at the source query, not by any
        // caller. TOP without ORDER BY is syntactically valid but returns SQL Server's own
        // arbitrary, unordered row selection — proven insufficient by this WP's own regression
        // test (a freshly-inserted candidate fell outside an unordered TOP against this
        // codebase's large accumulated same-NodeType corpus). ORDER BY CreatedAt DESC is the
        // minimal deterministic criterion available without reproducing RetrievalRanking's
        // frozen formula (ADR-015-005) in SQL. Trade-off, disclosed not hidden: once a
        // NodeType's corpus exceeds maxResults, this systematically favors more-recently-created
        // candidates over older ones — applied only when maxResults is requested, so every other
        // caller of this method (query(), assemble_context(), CompressionSweep) is unaffected.
        var topClause = maxResults.HasValue ? "TOP (@MaxResults) " : string.Empty;
        var orderByClause = maxResults.HasValue ? "ORDER BY CreatedAt DESC" : string.Empty;

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {topClause}NodeId, NodeType, Content, DomainTagsJson, EvidenceRefsJson, CreatedAt, KnowledgeMetadataJson
            FROM KnowledgeNode
            WHERE NodeType IN ({string.Join(", ", nodeTypeParameterNames)})
              AND (@CreatedFrom IS NULL OR CreatedAt >= @CreatedFrom)
              AND (@CreatedTo IS NULL OR CreatedAt <= @CreatedTo)
            {orderByClause}
            """;

        for (var index = 0; index < nodeTypeParameterNames.Length; index++)
        {
            command.Parameters.AddWithValue(nodeTypeParameterNames[index], nodeTypes[index].ToString());
        }

        command.Parameters.AddWithValue("@CreatedFrom", (object?)createdFrom ?? DBNull.Value);
        command.Parameters.AddWithValue("@CreatedTo", (object?)createdTo ?? DBNull.Value);
        if (maxResults.HasValue)
        {
            command.Parameters.AddWithValue("@MaxResults", maxResults.Value);
        }

        var results = new List<KnowledgeNode>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadNode(reader));
        }

        return results;
    }

    private static KnowledgeNode ReadNode(SqlDataReader reader)
    {
        return new KnowledgeNode(
            NodeId: reader.GetGuid(0),
            NodeType: Enum.Parse<KnowledgeNodeType>(reader.GetString(1)),
            Content: reader.GetString(2),
            DomainTags: JsonSerializer.Deserialize<string[]>(reader.GetString(3)) ?? [],
            EvidenceRefs: JsonSerializer.Deserialize<string[]>(reader.GetString(4)) ?? [],
            CreatedAt: reader.GetDateTimeOffset(5),
            Metadata: reader.IsDBNull(6)
                ? null
                : JsonSerializer.Deserialize<KnowledgeMetadata>(reader.GetString(6), MetadataSerializerOptions));
    }
}
