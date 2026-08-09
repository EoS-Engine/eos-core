using System.Text.Json;
using EOS.Contracts;
using Microsoft.Data.SqlClient;

namespace EOS.Orchestrator;

/// <summary>
/// Real, SQL-Server-backed persistence for <see cref="DispatchedTask"/> — current dispatch state
/// only, never a parallel history/audit table (ADR-PE002/FR-PE10), mirroring
/// <c>GoalStore</c>'s/<c>PlanStore</c>'s established pattern (WP-023 precedent).
/// </summary>
public sealed class DispatchedTaskStore(string connectionString)
{
    public async Task EnsureTableExistsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DispatchedTask')
            CREATE TABLE DispatchedTask (
                TaskId UNIQUEIDENTIFIER PRIMARY KEY,
                PlanId UNIQUEIDENTIFIER NOT NULL,
                GoalId UNIQUEIDENTIFIER NOT NULL,
                Description NVARCHAR(MAX) NOT NULL,
                CompetencyRequirementsJson NVARCHAR(MAX) NOT NULL,
                DependsOnTaskIdsJson NVARCHAR(MAX) NOT NULL,
                Priority INT NOT NULL,
                State NVARCHAR(50) NOT NULL,
                SchedulingMode NVARCHAR(50) NOT NULL,
                NotBefore DATETIMEOFFSET NULL,
                RunningAt DATETIMEOFFSET NULL,
                EventObserved BIT NOT NULL
            )
            """;

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 1913 or 2714)
        {
            // Benign race on the non-atomic IF NOT EXISTS guard above, matching
            // ArchivedContentStore's exact filter (WP-016) and WP-023's corrected filter
            // (PR #20 round 1): 2705 ("duplicate column name") is deliberately excluded — that
            // error indicates a malformed CREATE TABLE statement, not a concurrency artifact.
        }
    }

    public async Task UpsertAsync(DispatchedTask task, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF EXISTS (SELECT 1 FROM DispatchedTask WHERE TaskId = @TaskId)
                UPDATE DispatchedTask
                SET PlanId = @PlanId, GoalId = @GoalId, Description = @Description,
                    CompetencyRequirementsJson = @CompetencyRequirementsJson,
                    DependsOnTaskIdsJson = @DependsOnTaskIdsJson, Priority = @Priority,
                    State = @State, SchedulingMode = @SchedulingMode, NotBefore = @NotBefore,
                    RunningAt = @RunningAt, EventObserved = @EventObserved
                WHERE TaskId = @TaskId
            ELSE
                INSERT INTO DispatchedTask (
                    TaskId, PlanId, GoalId, Description, CompetencyRequirementsJson,
                    DependsOnTaskIdsJson, Priority, State, SchedulingMode, NotBefore, RunningAt,
                    EventObserved)
                VALUES (
                    @TaskId, @PlanId, @GoalId, @Description, @CompetencyRequirementsJson,
                    @DependsOnTaskIdsJson, @Priority, @State, @SchedulingMode, @NotBefore, @RunningAt,
                    @EventObserved)
            """;
        command.Parameters.AddWithValue("@TaskId", task.TaskId);
        command.Parameters.AddWithValue("@PlanId", task.PlanId);
        command.Parameters.AddWithValue("@GoalId", task.GoalId);
        command.Parameters.AddWithValue("@Description", task.Description);
        command.Parameters.AddWithValue("@CompetencyRequirementsJson", JsonSerializer.Serialize(task.CompetencyRequirements));
        command.Parameters.AddWithValue("@DependsOnTaskIdsJson", JsonSerializer.Serialize(task.DependsOnTaskIds));
        command.Parameters.AddWithValue("@Priority", task.Priority);
        command.Parameters.AddWithValue("@State", task.State.ToString());
        command.Parameters.AddWithValue("@SchedulingMode", task.SchedulingMode.ToString());
        command.Parameters.AddWithValue("@NotBefore", (object?)task.NotBefore ?? DBNull.Value);
        command.Parameters.AddWithValue("@RunningAt", (object?)task.RunningAt ?? DBNull.Value);
        command.Parameters.AddWithValue("@EventObserved", task.EventObserved);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<DispatchedTask?> GetByIdAsync(Guid taskId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"{SelectColumns} WHERE TaskId = @TaskId";
        command.Parameters.AddWithValue("@TaskId", taskId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadTask(reader) : null;
    }

    /// <summary>
    /// Constitution Part 6 §6.2's "Planned → Ready" transition candidates.
    /// </summary>
    public Task<IReadOnlyList<DispatchedTask>> GetByStateAsync(TaskLifecycleState state, CancellationToken cancellationToken) =>
        QueryAsync($"{SelectColumns} WHERE State = @State", command => command.Parameters.AddWithValue("@State", state.ToString()), cancellationToken);

    /// <summary>
    /// Constitution Part 7 §7.2's Priority Queue — "Ready" tasks ordered by Planner's own
    /// computed priority score, highest first (§7.3 step 1).
    /// </summary>
    public Task<IReadOnlyList<DispatchedTask>> GetReadyTasksOrderedByPriorityAsync(CancellationToken cancellationToken) =>
        QueryAsync(
            $"{SelectColumns} WHERE State = @State ORDER BY Priority DESC",
            command => command.Parameters.AddWithValue("@State", TaskLifecycleState.Ready.ToString()),
            cancellationToken);

    /// <summary>
    /// Constitution Part 7 §7.2's Concurrency ceiling ("Max simultaneous Running tasks") input.
    /// </summary>
    public async Task<int> CountByStateAsync(TaskLifecycleState state, CancellationToken cancellationToken)
    {
        var tasks = await GetByStateAsync(state, cancellationToken);
        return tasks.Count;
    }

    /// <summary>
    /// Constitution Part 7 §7.2's Daily Capacity ("Aggregate task-throughput ceiling per day")
    /// input — computed from this store's own current-state <see cref="DispatchedTask.RunningAt"/>
    /// field, never a separate history table (ADR-PE002).
    /// </summary>
    public async Task<int> CountRunningSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM DispatchedTask WHERE RunningAt IS NOT NULL AND RunningAt >= @Since";
        command.Parameters.AddWithValue("@Since", since);

        return (int)(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
    }

    private const string SelectColumns = """
        SELECT TaskId, PlanId, GoalId, Description, CompetencyRequirementsJson, DependsOnTaskIdsJson,
               Priority, State, SchedulingMode, NotBefore, RunningAt, EventObserved
        FROM DispatchedTask
        """;

    private async Task<IReadOnlyList<DispatchedTask>> QueryAsync(
        string commandText, Action<SqlCommand> configureParameters, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        configureParameters(command);

        var results = new List<DispatchedTask>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadTask(reader));
        }

        return results;
    }

    private static DispatchedTask ReadTask(SqlDataReader reader)
    {
        return new DispatchedTask(
            TaskId: reader.GetGuid(0),
            PlanId: reader.GetGuid(1),
            GoalId: reader.GetGuid(2),
            Description: reader.GetString(3),
            CompetencyRequirements: JsonSerializer.Deserialize<string[]>(reader.GetString(4)) ?? [],
            DependsOnTaskIds: JsonSerializer.Deserialize<Guid[]>(reader.GetString(5)) ?? [],
            Priority: reader.GetInt32(6),
            State: Enum.Parse<TaskLifecycleState>(reader.GetString(7)),
            SchedulingMode: Enum.Parse<SchedulingMode>(reader.GetString(8)),
            NotBefore: reader.IsDBNull(9) ? null : reader.GetDateTimeOffset(9),
            RunningAt: reader.IsDBNull(10) ? null : reader.GetDateTimeOffset(10),
            EventObserved: reader.GetBoolean(11));
    }
}
