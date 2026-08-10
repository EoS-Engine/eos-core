using System.Text.Json;
using EOS.Contracts;
using Microsoft.Data.SqlClient;

namespace EOS.Learning;

/// <summary>
/// Real, SQL-Server-backed persistence for <see cref="TransitionRecord"/>, mirroring
/// <c>PipelineRecordStore</c>'s exact established pattern: <c>IF NOT EXISTS</c> schema-evolution
/// guards, object-scoped index-existence checks (WP-026 CodeRabbit round-2 finding), JSON-
/// serialized array columns, benign-race <see cref="SqlException"/> catch.
/// </summary>
public sealed class TransitionRecordStore(string connectionString) : ITransitionRecordStore
{
    public async Task EnsureTableExistsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'TransitionRecord')
            CREATE TABLE TransitionRecord (
                TransitionId UNIQUEIDENTIFIER PRIMARY KEY,
                RecordId UNIQUEIDENTIFIER NOT NULL,
                FromStage NVARCHAR(50) NOT NULL,
                ToStage NVARCHAR(50) NOT NULL,
                TriggeredBy NVARCHAR(200) NOT NULL,
                EvidenceRefsJson NVARCHAR(MAX) NOT NULL,
                IntegrityHash NVARCHAR(64) NOT NULL,
                OccurredAt DATETIMEOFFSET NOT NULL
            )
            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.TransitionRecord')
                  AND name = N'IX_TransitionRecord_RecordId'
            )
            CREATE INDEX IX_TransitionRecord_RecordId ON TransitionRecord (RecordId)
            """;

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 2705 or 1913 or 2714)
        {
            // Benign race on the non-atomic IF NOT EXISTS guards above, matching
            // PipelineRecordStore's/PlanStore's/KnowledgeGraphStore's exact established precedent.
        }
    }

    public async Task InsertAsync(TransitionRecord record, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO TransitionRecord
                (TransitionId, RecordId, FromStage, ToStage, TriggeredBy, EvidenceRefsJson, IntegrityHash, OccurredAt)
            VALUES
                (@TransitionId, @RecordId, @FromStage, @ToStage, @TriggeredBy, @EvidenceRefsJson, @IntegrityHash, @OccurredAt)
            """;

        command.Parameters.AddWithValue("@TransitionId", record.TransitionId);
        command.Parameters.AddWithValue("@RecordId", record.RecordId);
        command.Parameters.AddWithValue("@FromStage", record.FromStage.ToString());
        command.Parameters.AddWithValue("@ToStage", record.ToStage.ToString());
        command.Parameters.AddWithValue("@TriggeredBy", record.TriggeredBy);
        command.Parameters.AddWithValue("@EvidenceRefsJson", JsonSerializer.Serialize(record.EvidenceRefs));
        command.Parameters.AddWithValue("@IntegrityHash", record.IntegrityHash);
        command.Parameters.AddWithValue("@OccurredAt", record.OccurredAt);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task<IReadOnlyList<TransitionRecord>> GetAllAsync(CancellationToken cancellationToken = default) =>
        QueryAsync(whereClause: null, bindParameters: null, cancellationToken);

    public Task<IReadOnlyList<TransitionRecord>> GetByRecordIdAsync(Guid recordId, CancellationToken cancellationToken = default) =>
        QueryAsync(
            "WHERE RecordId = @RecordId",
            command => command.Parameters.AddWithValue("@RecordId", recordId),
            cancellationToken);

    private async Task<IReadOnlyList<TransitionRecord>> QueryAsync(
        string? whereClause, Action<SqlCommand>? bindParameters, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TransitionId, RecordId, FromStage, ToStage, TriggeredBy, EvidenceRefsJson, IntegrityHash, OccurredAt
            FROM TransitionRecord
            {whereClause}
            """;
        bindParameters?.Invoke(command);

        var results = new List<TransitionRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new TransitionRecord(
                TransitionId: reader.GetGuid(0),
                RecordId: reader.GetGuid(1),
                FromStage: Enum.Parse<PipelineStage>(reader.GetString(2)),
                ToStage: Enum.Parse<PipelineStage>(reader.GetString(3)),
                TriggeredBy: reader.GetString(4),
                EvidenceRefs: JsonSerializer.Deserialize<string[]>(reader.GetString(5)) ?? [],
                IntegrityHash: reader.GetString(6),
                OccurredAt: reader.GetDateTimeOffset(7)));
        }

        return results;
    }
}
