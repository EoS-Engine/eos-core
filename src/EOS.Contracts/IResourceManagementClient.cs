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
}
