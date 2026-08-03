using System.Reflection;
using EOS.Contracts;
using EOS.Orchestrator;
using EOS.Resources;

namespace EOS.Runner.Tests;

// WP-022 CodeRabbit review Finding 3: ResourceManagementBackgroundSlotRequester (Program.cs's
// composition-root adapter correlating the void RequestBackgroundSlot call with its resulting
// BackgroundJobGranted/BackgroundJobDeferred event) was never exercised by any test. These tests
// use the real EventMediator and a real BackgroundTaskController, exactly as Program.cs wires
// them, per the roadmap-required integration point.
public class ResourceManagementBackgroundSlotRequesterTests
{
    private static ResourceClassQuotas CreateQuotas() => new(
        UserRequests: new ResourceClassQuota(100, 8192, 3),
        InteractiveSessions: new ResourceClassQuota(90, 6144, 2),
        AutonomousTasks: new ResourceClassQuota(70, 4096, 2),
        BackgroundMaintenance: new ResourceClassQuota(40, 2048, 1),
        LearningActivities: new ResourceClassQuota(20, 1024, 1));

    private static (ResourceManagementBackgroundSlotRequester Requester, Dictionary<string, bool> OutcomesByJobId) CreateRequester(CapacityTier cpuTier)
    {
        var eventMediator = new EventMediator();
        var quotaManager = new QuotaManager(
            CreateQuotas(),
            starvationDenialCountThreshold: 3,
            windowSeconds: 3600,
            new EventMediatorResourceQuotaExhaustedEventPublisher(eventMediator));
        var backgroundTaskController = new BackgroundTaskController(
            () => cpuTier,
            quotaManager,
            new EventMediatorBackgroundJobGrantedEventPublisher(eventMediator),
            new EventMediatorBackgroundJobDeferredEventPublisher(eventMediator));
        var resourceManagementClient = new StubResourceManagementClient(backgroundTaskController);
        var requester = new ResourceManagementBackgroundSlotRequester(resourceManagementClient, eventMediator);

        var outcomesField = typeof(ResourceManagementBackgroundSlotRequester)
            .GetField("_outcomesByJobId", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var outcomesByJobId = (Dictionary<string, bool>)outcomesField.GetValue(requester)!;

        return (requester, outcomesByJobId);
    }

    [Fact]
    public void RequestSlot_ReturnsTrue_WhenTheBackgroundJobIsGranted()
    {
        var (requester, _) = CreateRequester(CapacityTier.Safe);

        var granted = requester.RequestSlot("job-granted", ResourceClass.BackgroundMaintenance);

        Assert.True(granted);
    }

    [Fact]
    public void RequestSlot_ReturnsFalse_WhenTheBackgroundJobIsDeferred()
    {
        var (requester, _) = CreateRequester(CapacityTier.Critical);

        var granted = requester.RequestSlot("job-deferred", ResourceClass.LearningActivities);

        Assert.False(granted);
    }

    [Fact]
    public void RequestSlot_RemovesTheJobIdFromItsCorrelationDictionary_AfterEachCall()
    {
        var (requester, outcomesByJobId) = CreateRequester(CapacityTier.Safe);

        requester.RequestSlot("job-cleanup-granted", ResourceClass.BackgroundMaintenance);

        Assert.Empty(outcomesByJobId);
    }

    [Fact]
    public void RequestSlot_RemovesTheJobIdFromItsCorrelationDictionary_AfterADeferredCall()
    {
        var (requester, outcomesByJobId) = CreateRequester(CapacityTier.Critical);

        requester.RequestSlot("job-cleanup-deferred", ResourceClass.LearningActivities);

        Assert.Empty(outcomesByJobId);
    }

    private sealed class StubResourceManagementClient(BackgroundTaskController backgroundTaskController) : IResourceManagementClient
    {
        public double GetCurrentBudget(ResourceType resourceType) => 0;

        public CapacityTier GetCurrentTier(ResourceType resourceType) => CapacityTier.Safe;

        public ModelResidencyStatus GetModelResidency(string modelId) => new(modelId, ModelResidencyState.Unloaded, null);

        public void RequestBackgroundSlot(string jobId, ResourceClass resourceClass) =>
            backgroundTaskController.RequestBackgroundSlot(jobId, resourceClass);
    }
}
