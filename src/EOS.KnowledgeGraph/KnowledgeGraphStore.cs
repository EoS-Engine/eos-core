using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace EOS.KnowledgeGraph;

public sealed class KnowledgeGraphStore(string connectionString)
{
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
            node.Metadata is null ? DBNull.Value : JsonSerializer.Serialize(node.Metadata));

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
        CancellationToken cancellationToken)
    {
        if (nodeTypes.Count == 0)
        {
            return [];
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var nodeTypeParameterNames = nodeTypes.Select((_, index) => $"@NodeType{index}").ToArray();

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT NodeId, NodeType, Content, DomainTagsJson, EvidenceRefsJson, CreatedAt, KnowledgeMetadataJson
            FROM KnowledgeNode
            WHERE NodeType IN ({string.Join(", ", nodeTypeParameterNames)})
              AND (@CreatedFrom IS NULL OR CreatedAt >= @CreatedFrom)
              AND (@CreatedTo IS NULL OR CreatedAt <= @CreatedTo)
            """;

        for (var index = 0; index < nodeTypeParameterNames.Length; index++)
        {
            command.Parameters.AddWithValue(nodeTypeParameterNames[index], nodeTypes[index].ToString());
        }

        command.Parameters.AddWithValue("@CreatedFrom", (object?)createdFrom ?? DBNull.Value);
        command.Parameters.AddWithValue("@CreatedTo", (object?)createdTo ?? DBNull.Value);

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
            Metadata: reader.IsDBNull(6) ? null : JsonSerializer.Deserialize<KnowledgeMetadata>(reader.GetString(6)));
    }
}
