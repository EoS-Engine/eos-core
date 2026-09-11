namespace EOS.Contracts;

/// <summary>
/// Protection-Layer-Specification-v1.0 §11's "Local Files" domain ("governance over which
/// autonomous roles may read/write which repository paths … as an EOS-level policy"; "actual
/// file I/O — whichever project performs the operation"): the read-only surface through which
/// an Autonomous Role sees explicitly referenced repository files. Roles never perform file
/// I/O themselves; the composition root supplies an implementation whose every read is routed
/// through <see cref="IProtectionClient.Validate"/> (Protection §10: enforcement is structural,
/// via the composition root, never a subsystem "remembering" to call Protection). No listing,
/// no crawling, no writes — WP-A exposes exactly one read of one explicitly named path.
/// </summary>
public interface IWorkspaceClient
{
    /// <summary>
    /// Reads the repository file at <paramref name="relativePath"/> (repository-root-relative,
    /// forward slashes, under <c>src/</c> or <c>tests/</c>). Returns <see langword="null"/> when
    /// the file does not exist. Throws for any path outside the permitted roots or when
    /// Protection denies the read.
    /// </summary>
    Task<string?> ReadFileAsync(string relativePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// ADR-007: reports whether <paramref name="unifiedDiff"/> applies, as-is, to the workspace
    /// this client is bound to — Constitution §0.1.1.6 ("reality over simulation") applied to
    /// Part 6 §6.2's "Implementation evidence (diff)". Read-only: never modifies the working
    /// tree, index, or repository metadata. A check that cannot be performed is reported as
    /// not applicable (fail closed), never as applicable. Throws for Protection denial.
    /// </summary>
    Task<PatchApplicabilityResult> CheckPatchAppliesAsync(string unifiedDiff, CancellationToken cancellationToken = default);
}
