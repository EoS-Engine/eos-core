using EOS.Contracts;
using Microsoft.Data.SqlClient;

namespace EOS.Orchestrator;

/// <summary>
/// Real, SQL-Server-backed persistence for the single current Operational Mode value, mirroring
/// <see cref="LoopIterationStore"/>'s exact established pattern: <c>IF NOT EXISTS</c> schema-
/// evolution guards, benign-race <see cref="SqlException"/> catch, connection-per-call,
/// parameterized SQL. A single, fixed-key row (<c>Id = 1</c>) realizes §19.2's "one active mode at
/// a time" — <see cref="SetCurrentModeAsync"/> upserts via <c>UPDATE ... OUTPUT deleted.CurrentMode</c>
/// (falling back to INSERT-if-absent) rather than <c>MERGE</c>, matching this codebase's existing
/// plain-SQL style throughout <c>EOS.Orchestrator</c> — the OUTPUT clause is what makes the
/// returned previous-mode value atomic with the write itself (CodeRabbit pre-merge P1 finding #2).
/// </summary>
public sealed class OperationalModeStore(string connectionString) : IOperationalModeStore
{
    public async Task EnsureTableExistsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'OperationalModeState')
            CREATE TABLE OperationalModeState (
                Id INT NOT NULL PRIMARY KEY,
                CurrentMode NVARCHAR(20) NOT NULL
            )
            """;

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 2705 or 1913 or 2714)
        {
            // Benign race on the non-atomic IF NOT EXISTS guard above, matching
            // LoopIterationStore's/TransitionRecordStore's exact established precedent.
        }
    }

    public async Task<OperationalMode> GetCurrentModeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CurrentMode FROM OperationalModeState WHERE Id = 1";

        var result = await command.ExecuteScalarAsync(cancellationToken);
        // §22.2: Assisted is the Loop's default mode in the absence of an explicit selection.
        return result is string modeName ? Enum.Parse<OperationalMode>(modeName) : OperationalMode.Assisted;
    }

    public async Task<OperationalMode> SetCurrentModeAsync(OperationalMode mode, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // CodeRabbit pre-merge P1 finding #2 fix: UPDATE ... OUTPUT deleted.CurrentMode returns
        // the pre-update value in the same atomic statement as the write itself — no separate
        // read-then-write race window exists between "observe the previous mode" and "persist the
        // new one." 0 rows in the OUTPUT result (ExecuteScalarAsync returns null) means no row
        // exists yet, exactly as before.
        var previousModeName = await ExecuteUpdateOutputPreviousModeAsync(connection, mode, cancellationToken);
        if (previousModeName is not null)
        {
            return Enum.Parse<OperationalMode>(previousModeName);
        }

        await using var insertCommand = connection.CreateCommand();
        insertCommand.CommandText = "INSERT INTO OperationalModeState (Id, CurrentMode) VALUES (1, @CurrentMode)";
        insertCommand.Parameters.AddWithValue("@CurrentMode", mode.ToString());

        try
        {
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
            // §22.2: Assisted is the Loop's default mode in the absence of an explicit selection —
            // the true previous state before this first-ever persisted write.
            return OperationalMode.Assisted;
        }
        catch (SqlException ex) when (ex.Number == 2627)
        {
            // Benign race: another concurrent SetCurrentModeAsync call inserted the row first
            // between this call's UPDATE (0 rows affected) and its own INSERT attempt. The row
            // now genuinely exists with that call's value, so retrying via the same atomic
            // UPDATE ... OUTPUT (not a plain UPDATE) is required here too — returning "Assisted"
            // for this branch would be wrong, since a real prior value now exists to observe.
            var retryPreviousModeName = await ExecuteUpdateOutputPreviousModeAsync(connection, mode, cancellationToken)
                ?? throw new InvalidOperationException(
                    "OperationalModeState row unexpectedly absent immediately after a concurrent INSERT succeeded.");
            return Enum.Parse<OperationalMode>(retryPreviousModeName);
        }
    }

    private static async Task<string?> ExecuteUpdateOutputPreviousModeAsync(
        SqlConnection connection, OperationalMode mode, CancellationToken cancellationToken)
    {
        await using var updateCommand = connection.CreateCommand();
        updateCommand.CommandText = """
            UPDATE OperationalModeState
            SET CurrentMode = @CurrentMode
            OUTPUT deleted.CurrentMode
            WHERE Id = 1
            """;
        updateCommand.Parameters.AddWithValue("@CurrentMode", mode.ToString());

        return await updateCommand.ExecuteScalarAsync(cancellationToken) as string;
    }
}
