using System.Text.Json;
using EOS.Contracts;
using Microsoft.Data.SqlClient;

namespace EOS.Planner;

/// <summary>
/// Real, SQL-Server-backed persistence for <see cref="Plan"/> — this WP's own realization of
/// Constitution Part 8's Artifact Registry (FR-PE5: "Every Task Graph MUST resolve to a Plan
/// artifact in the Artifact Registry... no second, parallel plan store") for Plan artifacts
/// specifically, mirroring <c>KnowledgeGraphStore</c>'s/<c>ArchivedContentStore</c>'s exact
/// established pattern (WP-023 Implementation Blueprint Decision D5). <see cref="Plan.Tasks"/>
/// is stored as JSON, matching the existing precedent of JSON-serializing structured array
/// fields (e.g. <c>KnowledgeGraphStore</c>'s <c>DomainTagsJson</c>).
/// </summary>
public sealed class PlanStore(string connectionString)
{
    public async Task EnsureTableExistsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PlanArtifact')
            CREATE TABLE PlanArtifact (
                PlanId UNIQUEIDENTIFIER PRIMARY KEY,
                GoalId UNIQUEIDENTIFIER NOT NULL,
                TasksJson NVARCHAR(MAX) NOT NULL,
                EstimatedResourceCost FLOAT NOT NULL,
                RiskAdjustedConfidenceScore FLOAT NOT NULL
            )
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PlanArtifact') AND name = 'PreviousPlanId')
            ALTER TABLE PlanArtifact ADD PreviousPlanId UNIQUEIDENTIFIER NULL
            """;

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 2705 or 1913 or 2714)
        {
            // Benign race on the non-atomic IF NOT EXISTS guards above (matching
            // ArchivedContentStore's exact filter, WP-016, and KnowledgeGraphStore's
            // ALTER-TABLE-ADD-column precedent, WP-025): 2705 here is a genuine "duplicate
            // column" race on the new ALTER TABLE guard above, not a malformed CREATE TABLE
            // statement.
        }
    }

    public async Task InsertAsync(Plan plan, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO PlanArtifact (PlanId, GoalId, TasksJson, EstimatedResourceCost, RiskAdjustedConfidenceScore, PreviousPlanId)
            VALUES (@PlanId, @GoalId, @TasksJson, @EstimatedResourceCost, @RiskAdjustedConfidenceScore, @PreviousPlanId)
            """;
        command.Parameters.AddWithValue("@PlanId", plan.PlanId);
        command.Parameters.AddWithValue("@GoalId", plan.GoalId);
        command.Parameters.AddWithValue("@TasksJson", JsonSerializer.Serialize(plan.Tasks));
        command.Parameters.AddWithValue("@EstimatedResourceCost", plan.EstimatedResourceCost);
        command.Parameters.AddWithValue("@RiskAdjustedConfidenceScore", plan.RiskAdjustedConfidenceScore);
        command.Parameters.AddWithValue("@PreviousPlanId", (object?)plan.PreviousPlanId ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Plan?> GetByIdAsync(Guid planId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT PlanId, GoalId, TasksJson, EstimatedResourceCost, RiskAdjustedConfidenceScore, PreviousPlanId
            FROM PlanArtifact
            WHERE PlanId = @PlanId
            """;
        command.Parameters.AddWithValue("@PlanId", planId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new Plan(
            PlanId: reader.GetGuid(0),
            GoalId: reader.GetGuid(1),
            Tasks: JsonSerializer.Deserialize<PlanTask[]>(reader.GetString(2)) ?? [],
            EstimatedResourceCost: reader.GetDouble(3),
            RiskAdjustedConfidenceScore: reader.GetDouble(4),
            PreviousPlanId: reader.IsDBNull(5) ? null : reader.GetGuid(5));
    }
}
