using Microsoft.Data.SqlClient;

namespace EOS.Planner;

/// <summary>
/// Real, SQL-Server-backed persistence for Goal-level dependency edges
/// (Planning-Execution-Engine-Specification-v1.0 §11.4: "Goal A depends on Goal B if any Task
/// in A's eventual Task Graph requires evidence or capability only available once B completes")
/// — "distinct from and layered above the unchanged Task-level Dependency Graph," owned by the
/// Dependency Manager (§10.4), never merged into <c>Goal</c> itself. Mirrors
/// <c>GoalStore</c>'s/<c>KnowledgeGraphStore</c>'s established persistence pattern.
/// </summary>
public sealed class GoalDependencyStore(string connectionString)
{
    public async Task EnsureTableExistsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'GoalDependency')
            CREATE TABLE GoalDependency (
                GoalId UNIQUEIDENTIFIER NOT NULL,
                DependsOnGoalId UNIQUEIDENTIFIER NOT NULL,
                PRIMARY KEY (GoalId, DependsOnGoalId)
            )
            """;

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 2705 or 1913 or 2714)
        {
            // Benign race on the non-atomic IF NOT EXISTS guard above (identical class of issue
            // already found and fixed for ArchivedContentStore, WP-016).
        }
    }

    public async Task AddDependencyAsync(Guid goalId, Guid dependsOnGoalId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF NOT EXISTS (SELECT 1 FROM GoalDependency WHERE GoalId = @GoalId AND DependsOnGoalId = @DependsOnGoalId)
                INSERT INTO GoalDependency (GoalId, DependsOnGoalId) VALUES (@GoalId, @DependsOnGoalId)
            """;
        command.Parameters.AddWithValue("@GoalId", goalId);
        command.Parameters.AddWithValue("@DependsOnGoalId", dependsOnGoalId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetDependenciesAsync(Guid goalId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DependsOnGoalId FROM GoalDependency WHERE GoalId = @GoalId";
        command.Parameters.AddWithValue("@GoalId", goalId);

        var results = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(reader.GetGuid(0));
        }

        return results;
    }

    /// <summary>
    /// The reverse of <see cref="GetDependenciesAsync"/> — every Goal that depends on
    /// <paramref name="goalId"/>. Feeds §11.3's "aggregate priority of dependent Goals" input to
    /// the Priority Manager (§10.5).
    /// </summary>
    public async Task<IReadOnlyList<Guid>> GetDependentsAsync(Guid goalId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT GoalId FROM GoalDependency WHERE DependsOnGoalId = @DependsOnGoalId";
        command.Parameters.AddWithValue("@DependsOnGoalId", goalId);

        var results = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(reader.GetGuid(0));
        }

        return results;
    }
}
