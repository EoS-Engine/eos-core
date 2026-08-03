using EOS.Contracts;
using EOS.Resources;

namespace EOS.Resources.Tests;

/// <summary>
/// Resource-Management-Specification-v1.0 WP-022 roadmap row, "Demo / acceptance criteria":
/// "Under simulated high CPU load, a test background job request is deferred; once load
/// subsides, it is granted automatically without manual intervention."
/// </summary>
public class BackgroundJobContentionIntegrationTests
{
    [Fact]
    public void RequestBackgroundSlot_IsDeferredUnderSimulatedContention_ThenGrantedAutomaticallyOnceLoadSubsides()
    {
        var cpuTier = CapacityTier.Critical; // Simulated high CPU load.
        var quotaManager = new QuotaManager(TestResourceClassQuotas.Default, starvationDenialCountThreshold: 3, windowSeconds: 3600, new NoOpResourceQuotaExhaustedEventPublisher());
        var granted = new CapturingBackgroundJobGrantedEventPublisher();
        var deferred = new CapturingBackgroundJobDeferredEventPublisher();
        var controller = new BackgroundTaskController(() => cpuTier, quotaManager, granted, deferred);

        controller.RequestBackgroundSlot("compression-sweep-1", ResourceClass.BackgroundMaintenance);
        Assert.Equal(0, granted.CallCount);
        Assert.Equal(1, deferred.CallCount);

        cpuTier = CapacityTier.Safe; // Load subsides.
        controller.RequestBackgroundSlot("compression-sweep-1", ResourceClass.BackgroundMaintenance);

        Assert.Equal(1, granted.CallCount);
        Assert.Equal(1, deferred.CallCount);
    }

    private sealed class CapturingBackgroundJobGrantedEventPublisher : IBackgroundJobGrantedEventPublisher
    {
        public int CallCount { get; private set; }

        public void PublishBackgroundJobGranted(string jobId, ResourceClass resourceClass) => CallCount++;
    }

    private sealed class CapturingBackgroundJobDeferredEventPublisher : IBackgroundJobDeferredEventPublisher
    {
        public int CallCount { get; private set; }

        public void PublishBackgroundJobDeferred(string jobId, string reason) => CallCount++;
    }
}
