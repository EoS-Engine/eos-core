using EOS.Contracts;

namespace EOS.Resources;

/// <summary>
/// WP-021: the concrete <see cref="IResourceManagementClient"/>, composing
/// <see cref="ResourceMonitor"/> (§18) and <see cref="CapacityManager"/> (§17) — the one
/// public entry point <c>EOS.Gates</c>, <c>EOS.Orchestrator</c>, and <c>EOS.AIProvider</c>
/// consume via <c>EOS.Contracts</c>. <c>query_history()</c> is not implemented here — see
/// AG-0003 (unrelated to this WP's scope).
/// </summary>
public sealed class ResourceManagementClient(ResourceMonitor resourceMonitor, CapacityManager capacityManager, BackgroundTaskController backgroundTaskController) : IResourceManagementClient
{
    public double GetCurrentBudget(ResourceType resourceType) => resourceMonitor.Sample(resourceType);

    public CapacityTier GetCurrentTier(ResourceType resourceType) =>
        capacityManager.ComputeTier(resourceType, resourceMonitor.Sample(resourceType));

    public ModelResidencyStatus GetModelResidency(string modelId) => resourceMonitor.GetModelResidency(modelId);

    public void RequestBackgroundSlot(string jobId, ResourceClass resourceClass) =>
        backgroundTaskController.RequestBackgroundSlot(jobId, resourceClass);
}
