using Microsoft.Data.SqlClient;

namespace EOS.Infrastructure;

public sealed class SqlEventStore(string connectionString)
{
    public async Task EnsureTableExistsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'EventStore')
            CREATE TABLE EventStore (
                EventId UNIQUEIDENTIFIER PRIMARY KEY,
                EventType NVARCHAR(200) NOT NULL,
                Version NVARCHAR(50) NOT NULL,
                Producer NVARCHAR(200) NOT NULL,
                CorrelationId UNIQUEIDENTIFIER NOT NULL,
                CausationId UNIQUEIDENTIFIER NULL,
                OccurredAt DATETIMEOFFSET NOT NULL,
                PayloadJson NVARCHAR(MAX) NOT NULL
            )
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AppendAsync(StoredEvent storedEvent, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO EventStore (EventId, EventType, Version, Producer, CorrelationId, CausationId, OccurredAt, PayloadJson)
            VALUES (@EventId, @EventType, @Version, @Producer, @CorrelationId, @CausationId, @OccurredAt, @PayloadJson)
            """;
        command.Parameters.AddWithValue("@EventId", storedEvent.EventId);
        command.Parameters.AddWithValue("@EventType", storedEvent.EventType);
        command.Parameters.AddWithValue("@Version", storedEvent.Version);
        command.Parameters.AddWithValue("@Producer", storedEvent.Producer);
        command.Parameters.AddWithValue("@CorrelationId", storedEvent.CorrelationId);
        command.Parameters.AddWithValue("@CausationId", (object?)storedEvent.CausationId ?? DBNull.Value);
        command.Parameters.AddWithValue("@OccurredAt", storedEvent.OccurredAt);
        command.Parameters.AddWithValue("@PayloadJson", storedEvent.PayloadJson);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<StoredEvent?> ReadByIdAsync(Guid eventId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EventId, EventType, Version, Producer, CorrelationId, CausationId, OccurredAt, PayloadJson
            FROM EventStore
            WHERE EventId = @EventId
            """;
        command.Parameters.AddWithValue("@EventId", eventId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadCurrentRow(reader);
    }

    /// <summary>
    /// WP-030: minimal recent-event query capability for the Dashboard's "recent events"
    /// projection — the one piece of Dashboard data that genuinely needs event history rather
    /// than current-state store queries. Deterministic ordering (matching
    /// <c>LoopIterationStore.GetLatestAsync</c>'s own established <c>OccurredAt</c>-plus-tie-
    /// breaker precedent): most recent first, <c>EventId</c> as the stable tie-breaker for
    /// events sharing the same <c>OccurredAt</c> value.
    /// </summary>
    public async Task<IReadOnlyList<StoredEvent>> GetRecentAsync(int count, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (@Count) EventId, EventType, Version, Producer, CorrelationId, CausationId, OccurredAt, PayloadJson
            FROM EventStore
            ORDER BY OccurredAt DESC, EventId DESC
            """;
        command.Parameters.AddWithValue("@Count", count);

        var results = new List<StoredEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadCurrentRow(reader));
        }

        return results;
    }

    private static StoredEvent ReadCurrentRow(SqlDataReader reader) => new(
        EventId: reader.GetGuid(0),
        EventType: reader.GetString(1),
        Version: reader.GetString(2),
        Producer: reader.GetString(3),
        CorrelationId: reader.GetGuid(4),
        CausationId: reader.IsDBNull(5) ? null : reader.GetGuid(5),
        OccurredAt: reader.GetDateTimeOffset(6),
        PayloadJson: reader.GetString(7));
}
