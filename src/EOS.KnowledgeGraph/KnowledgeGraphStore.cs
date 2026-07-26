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
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertAsync(KnowledgeNode node, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF EXISTS (SELECT 1 FROM KnowledgeNode WHERE NodeId = @NodeId)
                UPDATE KnowledgeNode
                SET NodeType = @NodeType, Content = @Content, DomainTagsJson = @DomainTagsJson, EvidenceRefsJson = @EvidenceRefsJson
                WHERE NodeId = @NodeId
            ELSE
                INSERT INTO KnowledgeNode (NodeId, NodeType, Content, DomainTagsJson, EvidenceRefsJson, CreatedAt)
                VALUES (@NodeId, @NodeType, @Content, @DomainTagsJson, @EvidenceRefsJson, @CreatedAt)
            """;
        command.Parameters.AddWithValue("@NodeId", node.NodeId);
        command.Parameters.AddWithValue("@NodeType", node.NodeType.ToString());
        command.Parameters.AddWithValue("@Content", node.Content);
        command.Parameters.AddWithValue("@DomainTagsJson", JsonSerializer.Serialize(node.DomainTags));
        command.Parameters.AddWithValue("@EvidenceRefsJson", JsonSerializer.Serialize(node.EvidenceRefs));
        command.Parameters.AddWithValue("@CreatedAt", node.CreatedAt);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<KnowledgeNode?> GetByIdAsync(Guid nodeId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT NodeId, NodeType, Content, DomainTagsJson, EvidenceRefsJson, CreatedAt
            FROM KnowledgeNode
            WHERE NodeId = @NodeId
            """;
        command.Parameters.AddWithValue("@NodeId", nodeId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new KnowledgeNode(
            NodeId: reader.GetGuid(0),
            NodeType: Enum.Parse<KnowledgeNodeType>(reader.GetString(1)),
            Content: reader.GetString(2),
            DomainTags: JsonSerializer.Deserialize<string[]>(reader.GetString(3)) ?? [],
            EvidenceRefs: JsonSerializer.Deserialize<string[]>(reader.GetString(4)) ?? [],
            CreatedAt: reader.GetDateTimeOffset(5));
    }
}
