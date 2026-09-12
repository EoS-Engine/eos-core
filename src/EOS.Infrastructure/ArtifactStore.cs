using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using EOS.Contracts;
using Microsoft.Data.SqlClient;

namespace EOS.Infrastructure;

/// <summary>
/// Post-Roadmap WP-A: Constitution Part 8's Artifact Registry at its minimum useful surface —
/// SQL Server-backed (Constitution Part 4: indexed artifact metadata lives in SQL Server),
/// content-addressed (§8.1), insert-only (§8.3), text-only. Identity is the lowercase
/// 64-character hexadecimal SHA-256 of the exact UTF-8 bytes of the content, with no
/// normalization of any kind; that hash is the table's primary key, so the database enforces
/// uniqueness of identity. This class contains no <c>UPDATE</c> and no <c>DELETE</c> statement:
/// immutability through this API is guaranteed by construction, and a "changed" artifact is by
/// definition a different row. (No claim is made about operators with direct database access.)
/// Duplicate registration of byte-identical content is idempotent — the existing row is returned
/// and the first registration's metadata remains authoritative. Not a general artifact platform:
/// no query surface beyond lookup-by-hash, no binaries, no retention, no secondary index.
/// </summary>
public sealed class ArtifactStore(string connectionString) : IArtifactRegistryClient
{
    // 2627: PRIMARY KEY violation; 2601: unique index violation — the duplicate-registration race.
    private const int PrimaryKeyViolation = 2627;
    private const int UniqueIndexViolation = 2601;

    private static readonly Regex Sha256HexPattern = new("^[0-9a-f]{64}$", RegexOptions.Compiled);

    public async Task EnsureTableExistsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Artifact')
            CREATE TABLE Artifact (
                ContentHash CHAR(64) PRIMARY KEY,
                Type NVARCHAR(50) NOT NULL,
                Producer NVARCHAR(200) NOT NULL,
                Content NVARCHAR(MAX) NOT NULL,
                PreviousVersionHash CHAR(64) NULL,
                RegisteredAt DATETIMEOFFSET NOT NULL
            )
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// The registry's identity function: SHA-256 over <c>Encoding.UTF8.GetBytes(content)</c>
    /// (no BOM, no normalization), rendered as 64 lowercase hexadecimal characters.
    /// </summary>
    public static string ComputeContentHash(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    public async Task<ArtifactRecord> RegisterAsync(
        string type,
        string producer,
        string content,
        string? previousVersionHash = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(producer);
        ArgumentNullException.ThrowIfNull(content);

        if (previousVersionHash is not null)
        {
            // Part 8 §8.3: a new version references the prior version's hash — a dangling
            // reference would defeat the registry's audit purpose, so it must resolve.
            if (await GetByHashAsync(previousVersionHash, cancellationToken) is null)
            {
                throw new ArgumentException(
                    $"previousVersionHash '{previousVersionHash}' does not resolve to a registered artifact.", nameof(previousVersionHash));
            }
        }

        var record = new ArtifactRecord(
            ComputeContentHash(content), type, producer, content, previousVersionHash, DateTimeOffset.UtcNow);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Artifact (ContentHash, Type, Producer, Content, PreviousVersionHash, RegisteredAt)
            VALUES (@ContentHash, @Type, @Producer, @Content, @PreviousVersionHash, @RegisteredAt)
            """;
        command.Parameters.AddWithValue("@ContentHash", record.ContentHash);
        command.Parameters.AddWithValue("@Type", record.Type);
        command.Parameters.AddWithValue("@Producer", record.Producer);
        command.Parameters.AddWithValue("@Content", record.Content);
        command.Parameters.AddWithValue("@PreviousVersionHash", (object?)record.PreviousVersionHash ?? DBNull.Value);
        command.Parameters.AddWithValue("@RegisteredAt", record.RegisteredAt);

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            return record;
        }
        catch (SqlException ex) when (ex.Number is PrimaryKeyViolation or UniqueIndexViolation)
        {
            // Idempotent duplicate registration: the content is already registered under this
            // exact identity. The first registration's metadata is authoritative, so the
            // existing row is returned unchanged — never overwritten.
            return await GetByHashAsync(record.ContentHash, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Artifact '{record.ContentHash}' reported a duplicate key but could not be read back.");
        }
    }

    public async Task<ArtifactRecord?> GetByHashAsync(string contentHash, CancellationToken cancellationToken = default)
    {
        if (contentHash is null || !Sha256HexPattern.IsMatch(contentHash))
        {
            throw new ArgumentException(
                "contentHash must be the lowercase 64-character hexadecimal SHA-256 of the artifact content.", nameof(contentHash));
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ContentHash, Type, Producer, Content, PreviousVersionHash, RegisteredAt
            FROM Artifact
            WHERE ContentHash = @ContentHash
            """;
        command.Parameters.AddWithValue("@ContentHash", contentHash);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ArtifactRecord(
            ContentHash: reader.GetString(0),
            Type: reader.GetString(1),
            Producer: reader.GetString(2),
            Content: reader.GetString(3),
            PreviousVersionHash: reader.IsDBNull(4) ? null : reader.GetString(4),
            RegisteredAt: reader.GetFieldValue<DateTimeOffset>(5));
    }
}
