using System.Text.Json;
using EOS.Contracts;
using Microsoft.Data.SqlClient;

namespace EOS.Learning;

/// <summary>
/// Real, SQL-Server-backed persistence for <see cref="PipelineRecord"/>, mirroring
/// <c>PlanStore</c>'s/<c>KnowledgeGraphStore</c>'s exact established pattern (JSON-serialized
/// array columns, <c>IF NOT EXISTS</c> schema-evolution guards, benign-race
/// <see cref="SqlException"/> catch). Per this codebase's own convention (no store anywhere
/// queries into a JSON array column via SQL), <see cref="GetBySourceLessonIdAsync"/> and
/// <see cref="GetByKnowledgeGraphRefsAsync"/> fetch a scoped row set and filter in C#, exactly
/// like <c>KnowledgeClient.QuerySimilarAsync</c> does.
/// </summary>
public sealed class PipelineRecordStore(string connectionString) : IPipelineRecordStore
{
    public async Task EnsureTableExistsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PipelineRecord')
            CREATE TABLE PipelineRecord (
                RecordId UNIQUEIDENTIFIER PRIMARY KEY,
                Stage NVARCHAR(50) NOT NULL,
                KnowledgeGraphRef UNIQUEIDENTIFIER NOT NULL,
                SourceLessonIdsJson NVARCHAR(MAX) NOT NULL,
                DomainTagsJson NVARCHAR(MAX) NOT NULL,
                CreatedAt DATETIMEOFFSET NOT NULL,
                LastAdvancedAt DATETIMEOFFSET NOT NULL,
                ApprovalRefsJson NVARCHAR(MAX) NOT NULL,
                RoiEvaluationRef NVARCHAR(500) NULL,
                TrustScore FLOAT NOT NULL,
                ConfidenceScore FLOAT NOT NULL,
                Status NVARCHAR(50) NOT NULL
            )
            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'UX_PipelineRecord_KnowledgeGraphRef')
            CREATE UNIQUE INDEX UX_PipelineRecord_KnowledgeGraphRef ON PipelineRecord (KnowledgeGraphRef)
            """;

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 2705 or 1913 or 2714)
        {
            // Benign race on the non-atomic IF NOT EXISTS guards above, matching
            // PlanStore's/KnowledgeGraphStore's exact established precedent. 1913 here is a
            // genuine "duplicate index name" race on the new UNIQUE INDEX guard, not a
            // malformed CREATE TABLE statement.
        }
    }

    public async Task InsertAsync(PipelineRecord record, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO PipelineRecord
                (RecordId, Stage, KnowledgeGraphRef, SourceLessonIdsJson, DomainTagsJson, CreatedAt,
                 LastAdvancedAt, ApprovalRefsJson, RoiEvaluationRef, TrustScore, ConfidenceScore, Status)
            VALUES
                (@RecordId, @Stage, @KnowledgeGraphRef, @SourceLessonIdsJson, @DomainTagsJson, @CreatedAt,
                 @LastAdvancedAt, @ApprovalRefsJson, @RoiEvaluationRef, @TrustScore, @ConfidenceScore, @Status)
            """;

        command.Parameters.AddWithValue("@RecordId", record.RecordId);
        command.Parameters.AddWithValue("@Stage", record.Stage.ToString());
        command.Parameters.AddWithValue("@KnowledgeGraphRef", record.KnowledgeGraphRef);
        command.Parameters.AddWithValue("@SourceLessonIdsJson", JsonSerializer.Serialize(record.SourceLessonIds));
        command.Parameters.AddWithValue("@DomainTagsJson", JsonSerializer.Serialize(record.DomainTags));
        command.Parameters.AddWithValue("@CreatedAt", record.CreatedAt);
        command.Parameters.AddWithValue("@LastAdvancedAt", record.LastAdvancedAt);
        command.Parameters.AddWithValue("@ApprovalRefsJson", JsonSerializer.Serialize(record.ApprovalRefs));
        command.Parameters.AddWithValue("@RoiEvaluationRef", (object?)record.RoiEvaluationRef ?? DBNull.Value);
        command.Parameters.AddWithValue("@TrustScore", record.TrustScore);
        command.Parameters.AddWithValue("@ConfidenceScore", record.ConfidenceScore);
        command.Parameters.AddWithValue("@Status", record.Status.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PipelineRecord?> GetBySourceLessonIdAsync(
        Guid episodicEntryId, CancellationToken cancellationToken = default)
    {
        // SourceLessonIdsJson is a JSON array column — per this codebase's own convention (no
        // store anywhere queries into a JSON array column via SQL), this fetches all rows and
        // filters in C#, exactly like KnowledgeClient.QuerySimilarAsync does.
        var all = await QueryAsync(whereClause: null, bindParameters: null, cancellationToken);
        return all.FirstOrDefault(record => record.SourceLessonIds.Contains(episodicEntryId));
    }

    public async Task<IReadOnlyList<PipelineRecord>> GetByKnowledgeGraphRefsAsync(
        IEnumerable<Guid> knowledgeGraphRefs, CancellationToken cancellationToken = default)
    {
        var refs = knowledgeGraphRefs.ToHashSet();
        if (refs.Count == 0)
        {
            return [];
        }

        // Unlike SourceLessonIdsJson, KnowledgeGraphRef is a real (non-JSON) column, so this
        // filter can — and, for cost that no longer grows with total pipeline history
        // regardless of candidate-pool size, should — run in SQL.
        var parameterNames = refs.Select((_, index) => $"@Ref{index}").ToArray();
        return await QueryAsync(
            whereClause: $"WHERE KnowledgeGraphRef IN ({string.Join(", ", parameterNames)})",
            bindParameters: command =>
            {
                var refArray = refs.ToArray();
                for (var index = 0; index < refArray.Length; index++)
                {
                    command.Parameters.AddWithValue(parameterNames[index], refArray[index]);
                }
            },
            cancellationToken);
    }

    public async Task UpdateStageAsync(
        Guid recordId,
        PipelineStage stage,
        PipelineRecordStatus status,
        double confidenceScore,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE PipelineRecord
            SET Stage = @Stage, Status = @Status, ConfidenceScore = @ConfidenceScore, LastAdvancedAt = @LastAdvancedAt
            WHERE RecordId = @RecordId
            """;

        command.Parameters.AddWithValue("@Stage", stage.ToString());
        command.Parameters.AddWithValue("@Status", status.ToString());
        command.Parameters.AddWithValue("@ConfidenceScore", confidenceScore);
        command.Parameters.AddWithValue("@LastAdvancedAt", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("@RecordId", recordId);

        // Persistence failures must stay observable rather than being silently converted into
        // success — a promotion (or any other stage/status write) that targets a RecordId which
        // no longer resolves to a row must never be treated as if it had actually been
        // persisted, since the caller (ClusterTrigger) only publishes LessonPromoted after this
        // call returns successfully.
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0)
        {
            throw new InvalidOperationException($"PipelineRecord '{recordId}' was not found.");
        }
    }

    private async Task<IReadOnlyList<PipelineRecord>> QueryAsync(
        string? whereClause, Action<SqlCommand>? bindParameters, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT RecordId, Stage, KnowledgeGraphRef, SourceLessonIdsJson, DomainTagsJson, CreatedAt,
                   LastAdvancedAt, ApprovalRefsJson, RoiEvaluationRef, TrustScore, ConfidenceScore, Status
            FROM PipelineRecord
            {whereClause}
            """;
        bindParameters?.Invoke(command);

        var results = new List<PipelineRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new PipelineRecord(
                RecordId: reader.GetGuid(0),
                Stage: Enum.Parse<PipelineStage>(reader.GetString(1)),
                KnowledgeGraphRef: reader.GetGuid(2),
                SourceLessonIds: JsonSerializer.Deserialize<Guid[]>(reader.GetString(3)) ?? [],
                DomainTags: JsonSerializer.Deserialize<string[]>(reader.GetString(4)) ?? [],
                CreatedAt: reader.GetDateTimeOffset(5),
                LastAdvancedAt: reader.GetDateTimeOffset(6),
                ApprovalRefs: JsonSerializer.Deserialize<string[]>(reader.GetString(7)) ?? [],
                RoiEvaluationRef: reader.IsDBNull(8) ? null : reader.GetString(8),
                TrustScore: reader.GetDouble(9),
                ConfidenceScore: reader.GetDouble(10),
                Status: Enum.Parse<PipelineRecordStatus>(reader.GetString(11))));
        }

        return results;
    }
}
