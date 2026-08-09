using EOS.Contracts;

namespace EOS.Orchestrator.Tests;

internal static class TestConnectionString
{
    public static string SqlServer =>
        Environment.GetEnvironmentVariable("EOS_SQLSERVER_CONNECTION_STRING")
        ?? throw new InvalidOperationException("EOS_SQLSERVER_CONNECTION_STRING is not set.");
}

/// <summary>
/// Hand-rolled <see cref="IPlanQueryClient"/> stub (this repository uses no mocking framework)
/// returning a fixed set of <see cref="Plan"/> instances keyed by <see cref="Plan.PlanId"/>.
/// </summary>
internal sealed class FixedPlanQueryClient(params Plan[] plans) : IPlanQueryClient
{
    public Task<Plan?> GetByIdAsync(Guid planId, CancellationToken cancellationToken = default) =>
        Task.FromResult(plans.FirstOrDefault(plan => plan.PlanId == planId));
}

/// <summary>
/// Hand-rolled <see cref="IResourceManagementClient"/> stub whose tier is configurable per test —
/// used to exercise <c>Scheduler.SelectNextDispatchableTaskAsync</c>'s Resource Budget headroom
/// check (§7.3 step 3).
/// </summary>
internal sealed class FixedTierResourceManagementClient(CapacityTier tier) : IResourceManagementClient
{
    public double GetCurrentBudget(ResourceType resourceType) => 0;

    public CapacityTier GetCurrentTier(ResourceType resourceType) => tier;

    public ModelResidencyStatus GetModelResidency(string modelId) => new(modelId, ModelResidencyState.Unloaded, null);

    public void RequestBackgroundSlot(string jobId, ResourceClass resourceClass)
    {
    }
}

internal sealed class AlwaysAllowProtectionClient : IProtectionClient
{
    public ValidationResult Validate(ActionRequest action) => new(ProtectionVerdict.Allow, RiskTier.Low, null);
}

internal sealed class AlwaysDenyProtectionClient : IProtectionClient
{
    public ValidationResult Validate(ActionRequest action) => new(ProtectionVerdict.Deny, RiskTier.Low, "Denied by test.");
}

/// <summary>
/// Simulates Protection infrastructure genuinely failing (as opposed to returning a Deny
/// verdict) — used to prove <see cref="ExecutionCoordinator"/> never converts a Protection
/// failure into a false-success dispatch.
/// </summary>
internal sealed class ThrowingProtectionClient : IProtectionClient
{
    public ValidationResult Validate(ActionRequest action) =>
        throw new InvalidOperationException("Simulated Protection infrastructure failure.");
}

internal sealed class RecordingTaskStartedEventPublisher : ITaskStartedEventPublisher
{
    public List<Guid> PublishedTaskIds { get; } = [];

    public void PublishTaskStarted(Guid taskId) => PublishedTaskIds.Add(taskId);
}

/// <summary>
/// Records the <see cref="DispatchedTask"/> state observed via <paramref name="store"/> at the
/// exact moment each <c>TaskStarted</c> fires — proves the event is published strictly after the
/// Ready → Running persistence write, not before or concurrently with it.
/// </summary>
internal sealed class StateCapturingTaskStartedEventPublisher(DispatchedTaskStore store) : ITaskStartedEventPublisher
{
    public List<TaskLifecycleState> ObservedStatesAtPublishTime { get; } = [];

    public void PublishTaskStarted(Guid taskId) =>
        ObservedStatesAtPublishTime.Add(store.GetByIdAsync(taskId, CancellationToken.None).GetAwaiter().GetResult()!.State);
}
