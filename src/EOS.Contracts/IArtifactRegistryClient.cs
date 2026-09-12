namespace EOS.Contracts;

/// <summary>
/// Constitution Part 8's Artifact Registry, at the minimum surface WP-A requires: register an
/// immutable, content-addressed text artifact and resolve one by its hash. Declared in
/// <c>EOS.Contracts</c> because the producing role project (<c>EOS.SeniorEngineer</c>) may
/// reference only <c>EOS.Contracts</c> (Constitution Part 1 §1.2, Part 11 §11.2), while the
/// implementation lives in <c>EOS.Infrastructure</c> (Constitution Part 4: SQL Server owns
/// indexed artifact metadata). Every Task Lifecycle transition's evidence must resolve here
/// (Constitution Part 6 §6.3, §0.15.1); evidence references use the form
/// <c>artifact:&lt;sha256&gt;</c>.
/// </summary>
public interface IArtifactRegistryClient
{
    /// <summary>
    /// Registers <paramref name="content"/> (text only). Identity is the SHA-256 of the exact
    /// UTF-8 bytes; registering byte-identical content again is idempotent and returns the
    /// already-registered record — the first registration's metadata remains authoritative.
    /// <paramref name="previousVersionHash"/>, when supplied, must resolve to an existing
    /// artifact (Part 8 §8.3), else <see cref="ArgumentException"/>.
    /// </summary>
    Task<ArtifactRecord> RegisterAsync(
        string type,
        string producer,
        string content,
        string? previousVersionHash = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves an artifact by its lowercase 64-character hexadecimal SHA-256. Returns
    /// <see langword="null"/> when no such artifact exists; throws
    /// <see cref="ArgumentException"/> when <paramref name="contentHash"/> is malformed.
    /// </summary>
    Task<ArtifactRecord?> GetByHashAsync(string contentHash, CancellationToken cancellationToken = default);
}
