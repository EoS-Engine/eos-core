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
            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ArchivedContent_SourceNodeId_ArchivedAt')
            CREATE INDEX IX_ArchivedContent_SourceNodeId_ArchivedAt ON ArchivedContent (SourceNodeId, ArchivedAt DESC)
            """;

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 1913 or 2714)
        {
            // 1913: "already has an index" / 2714: "object already exists" — the IF NOT EXISTS
            // check above is not atomic, so concurrent callers (this method has no caller-side
            // synchronization, by design, matching KnowledgeGraphStore.EnsureTableExistsAsync's
            // identical pattern) can race to create the same index/table; the loser's error is
            // benign since the object the loser wanted now exists regardless of who created it.
        }
    }

    /// <summary>
    /// Insert-only archival: never updates or deletes an existing row. Returns the new
    /// archive entry's id. The current implementation does not persist this id on the
    /// compressed node; retrieval instead uses
    /// <see cref="GetLatestArchivedContentBySourceNodeIdAsync"/>, keyed by source node id
    /// rather than by this returned <c>ArchiveId</c> (see that method's own documentation).
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
