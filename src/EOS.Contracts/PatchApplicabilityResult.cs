namespace EOS.Contracts;

/// <summary>
/// Result of <see cref="IWorkspaceClient.CheckPatchAppliesAsync"/> (ADR-007): whether a unified
/// diff applies, as-is, to the workspace it was generated from. <see cref="Error"/> carries the
/// bounded reason when it does not — the checker's own output, or the failure of the check
/// itself (a check that could not run is reported as not applicable, never as applicable).
/// This is applicability only: it says nothing about whether the change compiles, passes tests,
/// or implements the requested intent — those remain <c>Review</c>/<c>Testing</c> (Constitution
/// Part 6 §6.2, human).
/// </summary>
public sealed record PatchApplicabilityResult(bool Applies, string? Error);
