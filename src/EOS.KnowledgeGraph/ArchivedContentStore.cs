using Microsoft.Data.SqlClient;

namespace EOS.KnowledgeGraph;

/// <summary>
/// Memory-Management-Specification-v1.0 §17.2's "original archived to the Artifact Registry"
/// step, realized narrowly as an insert-only, immutable SQL Server table for exactly one
/// artifact kind — compressed Episodic entries' pre-compression original content — consistent
/// with Constitution §8.3's versioning rule ("a 'change' creates a new version... never an
/// in-place edit") applied to this one artifact type. This is the WP-016 roadmap row's own
/// "archival to the Artifact Registry <b>pattern</b>," not the full Constitution Part 8 service
/// (a separate, much larger concern spanning many other artifact types, untouched by this WP).
/// </summary>
public sealed class ArchivedContentStore(string connectionString)
{
    public async Task EnsureTableExistsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ArchivedContent')
            CREATE TABLE ArchivedContent (
                ArchiveId UNIQUEIDENTIFIER PRIMARY KEY,
                SourceNodeId UNIQUEIDENTIFIER NOT NULL,
                OriginalContent NVARCHAR(MAX) NOT NULL,
                ArchivedAt DATETIMEOFFSET NOT NULL
            )
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Insert-only archival: never updates or deletes an existing row. Returns the new
    /// archive entry's id, which callers persist as the compressed node's <c>original_ref</c>
    /// (§17.2).
    /// </summary>
    public async Task<Guid> ArchiveAsync(Guid sourceNodeId, string originalContent, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var archiveId = Guid.NewGuid();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ArchivedContent (ArchiveId, SourceNodeId, OriginalContent, ArchivedAt)
            VALUES (@ArchiveId, @SourceNodeId, @OriginalContent, @ArchivedAt)
            """;
        command.Parameters.AddWithValue("@ArchiveId", archiveId);
        command.Parameters.AddWithValue("@SourceNodeId", sourceNodeId);
        command.Parameters.AddWithValue("@OriginalContent", originalContent);
        command.Parameters.AddWithValue("@ArchivedAt", DateTimeOffset.UtcNow);

        await command.ExecuteNonQueryAsync(cancellationToken);

        return archiveId;
    }

    public async Task<string?> GetArchivedContentAsync(Guid archiveId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT OriginalContent FROM ArchivedContent WHERE ArchiveId = @ArchiveId
            """;
        command.Parameters.AddWithValue("@ArchiveId", archiveId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result as string;
    }

    /// <summary>
    /// Looks up the most recently archived original content for a given source node — the
    /// natural query shape for proving "original content archived, not deleted" after a
    /// compression event, since callers of <see cref="ArchiveAsync"/> know the source node id
    /// but not the generated <c>ArchiveId</c> without threading it through separately.
    /// </summary>
    public async Task<string?> GetLatestArchivedContentBySourceNodeIdAsync(
        Guid sourceNodeId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP 1 OriginalContent FROM ArchivedContent
            WHERE SourceNodeId = @SourceNodeId
            ORDER BY ArchivedAt DESC
            """;
        command.Parameters.AddWithValue("@SourceNodeId", sourceNodeId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result as string;
    }
}
