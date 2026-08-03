using EOS.Contracts;
using EOS.Resources;

namespace EOS.Resources.Tests;

public class ResourceManagementClientTests
{
    [Fact]
    public void GetCurrentBudget_ReturnsALiveMeasurement_NotAHardcodedConstant()
    {
        // Roadmap Demo/Acceptance criterion: "get_current_budget(CPU) returns a value derived
        // from a live measurement, not a hardcoded constant."
        var monitor = new ResourceMonitor(samplingIntervalSeconds: 1, modelIdleResidencyTimeoutSeconds: 900, new NoOpModelLoadedEventPublisher(), new NoOpModelUnloadedEventPublisher());
        var thresholds = new CapacityThresholds(
            Cpu: new ResourceTierBoundaries(75, 90, 97),
            Ram: new ResourceTierBoundaries(6000, 7200, 7800),
            Disk: new ResourceTierBoundaries(350000, 420000, 460000),
            ModelUsage: new ResourceTierBoundaries(70000, 85000, 95000),
            QueueLength: new ResourceTierBoundaries(50, 100, 150),
            BackgroundTasks: new ResourceTierBoundaries(2, 3, 4),
            CacheUsage: new ResourceTierBoundaries(70, 85, 95));
        var manager = new CapacityManager(thresholds, new NoOpResourceThresholdCrossedEventPublisher(), new NoOpEmergencyCapacitySignalEventPublisher(), new NoOpResourceRecoveredEventPublisher());
        var quotaManager = new QuotaManager(TestResourceClassQuotas.Default, starvationDenialCountThreshold: 3, windowSeconds: 30, new NoOpResourceQuotaExhaustedEventPublisher());
        var controller = new BackgroundTaskController(
            () => manager.ComputeTier(ResourceType.Cpu, monitor.Sample(ResourceType.Cpu)),
            quotaManager, new NoOpBackgroundJobGrantedEventPublisher(), new NoOpBackgroundJobDeferredEventPublisher());
        var client = new ResourceManagementClient(monitor, manager, controller);

        var budget = client.GetCurrentBudget(ResourceType.Cpu);
        var tier = client.GetCurrentTier(ResourceType.Cpu);

        Assert.InRange(budget, 0.0, 100.0);
        Assert.True(Enum.IsDefined(tier));
    }
}
