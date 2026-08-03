namespace EOS.Contracts;

/// <summary>
/// WP-021: Resource-Management-Specification-v1.0 §10.2/§10.3/§17/§18's published
/// budget/tier values. Synchronous — matching <see cref="IProtectionClient"/>'s own shape
/// (Implementation Plan Decision D1) — since local resource measurement (§18.1: "sampled...
/// never continuously instrumented") is not equivalent to the remote I/O other async client
/// interfaces in this codebase perform. Declared in <c>EOS.Contracts</c> because none of
/// <c>EOS.Gates</c>, <c>EOS.Orchestrator</c>, or <c>EOS.AIProvider</c> (§10.1's consumers) may
/// reference <c>EOS.Resources</c> directly (Constitution Part 1 §1.2).
/// </summary>
public interface IResourceManagementClient
{
    double GetCurrentBudget(ResourceType resourceType);

    CapacityTier GetCurrentTier(ResourceType resourceType);

    /// <summary>
    /// WP-022: Resource-Management-Specification-v1.0 §21.1/§14.3 read-only residency signal.
    /// </summary>
    ModelResidencyStatus GetModelResidency(string modelId);

    /// <summary>
    /// WP-022: Resource-Management-Specification-v1.0 §21.1's one write-shaped call — requests a
    /// background resource slot; the Background Task Controller (§10.6) grants or defers it
    /// (§15.1), never altering what the job itself does. Outcome is observed via the
    /// <c>BackgroundJobGranted</c>/<c>BackgroundJobDeferred</c> events (§20), not a return value.
    /// </summary>
    void RequestBackgroundSlot(string jobId, ResourceClass resourceClass);
}
