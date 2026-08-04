using System.Text.Json;
using EOS.Contracts;
using Microsoft.Data.SqlClient;

namespace EOS.Planner;

/// <summary>
/// Real, SQL-Server-backed persistence for <see cref="Goal"/> — this WP's own realization of
/// Constitution Part 8's Artifact Registry concept for Goal entities, mirroring
/// <c>KnowledgeGraphStore</c>'s exact established pattern (WP-023 Implementation Blueprint
/// Decision D5). Not "a second, parallel" store in FR-PE5's forbidden sense — FR-PE5 concerns
/// <see cref="Plan"/> specifically (see <see cref="PlanStore"/>); Goal itself has no other owner
/// anywhere in this codebase.
/// </summary>
public sealed class GoalStore(string connectionString)
{
    public async Task EnsureTableExistsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Goal')
            CREATE TABLE Goal (
                GoalId UNIQUEIDENTIFIER PRIMARY KEY,
                Statement NVARCHAR(MAX) NOT NULL,
                ParentGoalId UNIQUEIDENTIFIER NULL,
                DomainTagsJson NVARCHAR(MAX) NOT NULL,
                SubmittedByActor NVARCHAR(200) NOT NULL,
                State NVARCHAR(50) NOT NULL,
                PlanId UNIQUEIDENTIFIER NULL
            )
            """;

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 1913 or 2714)
        {
            // Benign race on the non-atomic IF NOT EXISTS guard above (matching
            // ArchivedContentStore's exact filter, WP-016): a concurrent caller creating the same
            // table loses the race but the table it wanted now exists anyway. 2705 ("duplicate
            // column name") is deliberately excluded — that error indicates a malformed CREATE
            // TABLE statement, not a concurrency artifact, and must not be silently swallowed.
        }
    }

    public async Task UpsertAsync(Goal goal, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF EXISTS (SELECT 1 FROM Goal WHERE GoalId = @GoalId)
                UPDATE Goal
                SET Statement = @Statement, ParentGoalId = @ParentGoalId, DomainTagsJson = @DomainTagsJson,
                    SubmittedByActor = @SubmittedByActor, State = @State, PlanId = @PlanId
                WHERE GoalId = @GoalId
            ELSE
                INSERT INTO Goal (GoalId, Statement, ParentGoalId, DomainTagsJson, SubmittedByActor, State, PlanId)
                VALUES (@GoalId, @Statement, @ParentGoalId, @DomainTagsJson, @SubmittedByActor, @State, @PlanId)
            """;
        command.Parameters.AddWithValue("@GoalId", goal.GoalId);
        command.Parameters.AddWithValue("@Statement", goal.Statement);
        command.Parameters.AddWithValue("@ParentGoalId", (object?)goal.ParentGoalId ?? DBNull.Value);
        command.Parameters.AddWithValue("@DomainTagsJson", JsonSerializer.Serialize(goal.DomainTags));
        command.Parameters.AddWithValue("@SubmittedByActor", goal.SubmittedByActor);
        command.Parameters.AddWithValue("@State", goal.State.ToString());
        command.Parameters.AddWithValue("@PlanId", (object?)goal.PlanId ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Goal?> GetByIdAsync(Guid goalId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT GoalId, Statement, ParentGoalId, DomainTagsJson, SubmittedByActor, State, PlanId
            FROM Goal
            WHERE GoalId = @GoalId
            """;
        command.Parameters.AddWithValue("@GoalId", goalId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadGoal(reader);
    }

    /// <summary>
    /// §11.2's Goal Hierarchy — direct children of <paramref name="parentGoalId"/>, used by
    /// Goal Cancellation's cascade (§11.6).
    /// </summary>
    public async Task<IReadOnlyList<Goal>> GetChildrenAsync(Guid parentGoalId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT GoalId, Statement, ParentGoalId, DomainTagsJson, SubmittedByActor, State, PlanId
            FROM Goal
            WHERE ParentGoalId = @ParentGoalId
            """;
        command.Parameters.AddWithValue("@ParentGoalId", parentGoalId);

        var results = new List<Goal>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadGoal(reader));
        }

        return results;
    }

    private static Goal ReadGoal(SqlDataReader reader)
    {
        return new Goal(
            GoalId: reader.GetGuid(0),
            Statement: reader.GetString(1),
            ParentGoalId: reader.IsDBNull(2) ? null : reader.GetGuid(2),
            DomainTags: JsonSerializer.Deserialize<string[]>(reader.GetString(3)) ?? [],
            SubmittedByActor: reader.GetString(4),
            State: Enum.Parse<GoalLifecycleState>(reader.GetString(5)),
            PlanId: reader.IsDBNull(6) ? null : reader.GetGuid(6));
    }
}
