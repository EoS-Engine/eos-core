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
        command.CommandText = """
            IF EXISTS (SELECT 1 FROM IngestionRateGuardState WHERE ProducerRole = @ProducerRole AND WindowStart = @WindowStart)
                UPDATE IngestionRateGuardState
                SET EventCount = EventCount + 1
                WHERE ProducerRole = @ProducerRole AND WindowStart = @WindowStart
            ELSE
                INSERT INTO IngestionRateGuardState (ProducerRole, WindowStart, WindowEnd, EventCount)
                VALUES (@ProducerRole, @WindowStart, @WindowEnd, 1);

            SELECT EventCount FROM IngestionRateGuardState WHERE ProducerRole = @ProducerRole AND WindowStart = @WindowStart
            """;
        command.Parameters.AddWithValue("@ProducerRole", producerRole);
        command.Parameters.AddWithValue("@WindowStart", windowStart);
        command.Parameters.AddWithValue("@WindowEnd", windowEnd);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }
}
