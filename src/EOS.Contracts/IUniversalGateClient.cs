namespace EOS.Contracts;

/// <summary>
/// ADR-009: Constitution Part 6 §6.2 requires Universal Gates §0.8.1 before
/// <c>Running → Review</c>; this is the boundary through which the Execution Coordinator obtains
/// the Gate 1–2 decision for a registered artifact. Declared in <c>EOS.Contracts</c> because the
/// Orchestrator may reference neither <c>EOS.Infrastructure</c> (gate execution I/O) nor
/// <c>EOS.Gates</c> (the decision); the composition root composes the two behind this interface
/// and Protection-gates the run. Gate execution never touches the real workspace: it works on an
/// isolated throwaway copy and is fail-closed. Only Gates 1 and 2 are covered; Gates 3–5 remain
/// deferred and are not claimed.
/// </summary>
public interface IUniversalGateClient
{
    /// <summary>
    /// Evaluates Universal Gates 1–2 for the artifact(s) referenced by <paramref name="evidenceRefs"/>
    /// (<c>artifact:&lt;sha256&gt;</c>) belonging to <paramref name="task"/>. Throws only for
    /// Protection denial or cancellation; every execution problem is reported as a failed gate.
    /// </summary>
    Task<UniversalGateDecision> EvaluateAsync(DispatchedTask task, IReadOnlyList<string> evidenceRefs, CancellationToken cancellationToken = default);
}
