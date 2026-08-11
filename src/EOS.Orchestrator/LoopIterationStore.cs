using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace EOS.Orchestrator;

/// <summary>
/// Real, SQL-Server-backed persistence for <see cref="LoopIteration"/>, mirroring
/// <c>TransitionRecordStore</c>'s exact established pattern: <c>IF NOT EXISTS</c> schema-evolution
/// guards, object-scoped index-existence checks, JSON-serialized array columns, benign-race
/// <see cref="SqlException"/> catch.
/// </summary>
public sealed class LoopIterationStore(string connectionString) : ILoopIterationStore
{
    public async Task EnsureTableExistsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'LoopIteration')
            CREATE TABLE LoopIteration (
                IterationId UNIQUEIDENTIFIER PRIMARY KEY,
                TriggerSource NVARCHAR(50) NOT NULL,
                EntryStep INT NOT NULL,
                State NVARCHAR(20) NOT NULL,
                StepsTraversedJson NVARCHAR(MAX) NOT NULL,
                Outcome NVARCHAR(MAX) NULL,
                StartedAt DATETIMEOFFSET NOT NULL,
                CompletedAt DATETIMEOFFSET NULL
            )
            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.LoopIteration')
                  AND name = N'IX_LoopIteration_StartedAt'
            )
            CREATE INDEX IX_LoopIteration_StartedAt ON LoopIteration (StartedAt)
            """;

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 2705 or 1913 or 2714)
        {
            // Benign race on the non-atomic IF NOT EXISTS guards above, matching
            // TransitionRecordStore's/PipelineRecordStore's exact established precedent.
        }
    }

    public async Task InsertAsync(LoopIteration iteration, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO LoopIteration
                (IterationId, TriggerSource, EntryStep, State, StepsTraversedJson, Outcome, StartedAt, CompletedAt)
            VALUES
                (@IterationId, @TriggerSource, @EntryStep, @State, @StepsTraversedJson, @Outcome, @StartedAt, @CompletedAt)
            """;

        command.Parameters.AddWithValue("@IterationId", iteration.IterationId);
        command.Parameters.AddWithValue("@TriggerSource", iteration.TriggerSource);
        command.Parameters.AddWithValue("@EntryStep", iteration.EntryStep);
        command.Parameters.AddWithValue("@State", iteration.State);
        command.Parameters.AddWithValue("@StepsTraversedJson", JsonSerializer.Serialize(iteration.StepsTraversed));
        command.Parameters.AddWithValue("@Outcome", (object?)iteration.Outcome ?? DBNull.Value);
        command.Parameters.AddWithValue("@StartedAt", iteration.StartedAt);
        command.Parameters.AddWithValue("@CompletedAt", (object?)iteration.CompletedAt ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateStateAsync(
        Guid iterationId, string state, int[] stepsTraversed, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE LoopIteration
            SET State = @State, StepsTraversedJson = @StepsTraversedJson
            WHERE IterationId = @IterationId
            """;

        command.Parameters.AddWithValue("@State", state);
        command.Parameters.AddWithValue("@StepsTraversedJson", JsonSerializer.Serialize(stepsTraversed));
        command.Parameters.AddWithValue("@IterationId", iterationId);

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0)
        {
            throw new InvalidOperationException($"LoopIteration '{iterationId}' does not exist.");
        }
    }

    public async Task CompleteAsync(
        Guid iterationId, string outcome, int[] stepsTraversed, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE LoopIteration
            SET State = 'Completed', Outcome = @Outcome, StepsTraversedJson = @StepsTraversedJson, CompletedAt = @CompletedAt
            WHERE IterationId = @IterationId
            """;

        command.Parameters.AddWithValue("@Outcome", outcome);
        command.Parameters.AddWithValue("@StepsTraversedJson", JsonSerializer.Serialize(stepsTraversed));
        command.Parameters.AddWithValue("@CompletedAt", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("@IterationId", iterationId);

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0)
        {
            throw new InvalidOperationException($"LoopIteration '{iterationId}' does not exist.");
        }
    }

    public Task<LoopIteration?> GetByIdAsync(Guid iterationId, CancellationToken cancellationToken = default) =>
        QuerySingleAsync("WHERE IterationId = @IterationId", command => command.Parameters.AddWithValue("@IterationId", iterationId), cancellationToken);

    public Task<LoopIteration?> GetLatestAsync(CancellationToken cancellationToken = default) =>
        QuerySingleAsync(whereClause: null, bindParameters: null, cancellationToken, orderByLatest: true);

    private async Task<LoopIteration?> QuerySingleAsync(
        string? whereClause, Action<SqlCommand>? bindParameters, CancellationToken cancellationToken, bool orderByLatest = false)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        // Deterministic ordering (WP-027 R1 finding #12's own precedent): OccurredAt-equivalent
        // primary key with a stable tie-breaker.
        var orderBy = orderByLatest ? "ORDER BY StartedAt DESC, IterationId DESC" : string.Empty;
        var top = orderByLatest ? "TOP (1)" : string.Empty;
        command.CommandText = $"""
            SELECT {top} IterationId, TriggerSource, EntryStep, State, StepsTraversedJson, Outcome, StartedAt, CompletedAt
            FROM LoopIteration
            {whereClause}
            {orderBy}
            """;
        bindParameters?.Invoke(command);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new LoopIteration(
            IterationId: reader.GetGuid(0),
            TriggerSource: reader.GetString(1),
            EntryStep: reader.GetInt32(2),
            State: reader.GetString(3),
            StepsTraversed: JsonSerializer.Deserialize<int[]>(reader.GetString(4)) ?? [],
            Outcome: reader.IsDBNull(5) ? null : reader.GetString(5),
            StartedAt: reader.GetDateTimeOffset(6),
            CompletedAt: reader.IsDBNull(7) ? null : reader.GetDateTimeOffset(7));
    }
}
