using Microsoft.Data.SqlClient;

namespace EOS.Learning;

/// <summary>
/// Real, SQL-Server-backed persistence for <see cref="EOS.Contracts.IngestionRateGuardState"/>,
/// mirroring <c>KnowledgeGraphStore.UpsertAsync</c>'s exact <c>IF EXISTS UPDATE ELSE INSERT</c>
/// pattern. Keyed by <c>(ProducerRole, WindowStart)</c> — a new wall-clock bucket is a new row,
/// never overwriting a prior bucket's count.
/// </summary>
public sealed class IngestionRateGuardStore(string connectionString) : IIngestionRateGuardStore
{
    public async Task EnsureTableExistsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'IngestionRateGuardState')
            CREATE TABLE IngestionRateGuardState (
                ProducerRole NVARCHAR(200) NOT NULL,
                WindowStart DATETIMEOFFSET NOT NULL,
                WindowEnd DATETIMEOFFSET NOT NULL,
                EventCount INT NOT NULL,
                CONSTRAINT PK_IngestionRateGuardState PRIMARY KEY (ProducerRole, WindowStart)
            )
            """;

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 2705 or 1913 or 2714)
        {
            // Benign race on the non-atomic IF NOT EXISTS guard above, matching
            // PipelineRecordStore's/PlanStore's/KnowledgeGraphStore's exact established precedent.
        }
    }

    public async Task<int> IncrementAndGetCountAsync(
        string producerRole, DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        // SERIALIZABLE isolation makes the UPDATE's WHERE clause (matching the primary key
        // exactly) take a key-range lock covering that key even when zero rows currently match
        // — a concurrent transaction attempting to UPDATE or INSERT the same (ProducerRole,
        // WindowStart) key blocks until this transaction commits, so two concurrent "first
        // events" for a brand-new bucket can no longer both fall through to INSERT and collide
        // on PK_IngestionRateGuardState. Scoped to this single connection/command only — the
        // isolation level and transaction are both torn down when the connection is disposed.
        command.CommandText = """
            SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
            BEGIN TRANSACTION;

            UPDATE IngestionRateGuardState
            SET EventCount = EventCount + 1
            WHERE ProducerRole = @ProducerRole AND WindowStart = @WindowStart;

            IF @@ROWCOUNT = 0
                INSERT INTO IngestionRateGuardState (ProducerRole, WindowStart, WindowEnd, EventCount)
                VALUES (@ProducerRole, @WindowStart, @WindowEnd, 1);

            SELECT EventCount FROM IngestionRateGuardState WHERE ProducerRole = @ProducerRole AND WindowStart = @WindowStart;

            COMMIT TRANSACTION;
            """;
        command.Parameters.AddWithValue("@ProducerRole", producerRole);
        command.Parameters.AddWithValue("@WindowStart", windowStart);
        command.Parameters.AddWithValue("@WindowEnd", windowEnd);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }
}
