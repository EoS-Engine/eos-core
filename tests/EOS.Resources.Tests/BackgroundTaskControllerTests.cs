using EOS.Contracts;
using EOS.Resources;

namespace EOS.Resources.Tests;

public class BackgroundTaskControllerTests
{
    private static (BackgroundTaskController Controller, CapturingBackgroundJobGrantedEventPublisher Granted, CapturingBackgroundJobDeferredEventPublisher Deferred) CreateController(
        CapacityTier cpuTier = CapacityTier.Safe, QuotaManager? quotaManager = null)
    {
        quotaManager ??= new QuotaManager(TestResourceClassQuotas.Default, starvationDenialCountThreshold: 3, windowSeconds: 3600, new NoOpResourceQuotaExhaustedEventPublisher());
        var granted = new CapturingBackgroundJobGrantedEventPublisher();
        var deferred = new CapturingBackgroundJobDeferredEventPublisher();
        var controller = new BackgroundTaskController(() => cpuTier, quotaManager, granted, deferred);
        return (controller, granted, deferred);
    }

    [Fact]
    public void RequestBackgroundSlot_Grants_WhenAllThreeChecksPass()
    {
        var (controller, granted, deferred) = CreateController();

        controller.RequestBackgroundSlot("job-1", ResourceClass.BackgroundMaintenance);

        Assert.Equal(1, granted.CallCount);
        Assert.Equal("job-1", granted.LastJobId);
        Assert.Equal(ResourceClass.BackgroundMaintenance, granted.LastResourceClass);
        Assert.Equal(0, deferred.CallCount);
    }

    [Theory]
    [InlineData(CapacityTier.Warning)]
    [InlineData(CapacityTier.Critical)]
    [InlineData(CapacityTier.Emergency)]
    public void RequestBackgroundSlot_Defers_WhenCpuIsAtOrAboveWarning(CapacityTier cpuTier)
    {
        // §15.1 step 1: CPU load vs. Warning threshold, checked before quota/maintenance.
        var (controller, granted, deferred) = CreateController(cpuTier: cpuTier);

        controller.RequestBackgroundSlot("job-2", ResourceClass.BackgroundMaintenance);

        Assert.Equal(0, granted.CallCount);
        Assert.Equal(1, deferred.CallCount);
        Assert.Equal("job-2", deferred.LastJobId);
    }

    [Fact]
    public void RequestBackgroundSlot_Defers_WhenClassQuotaIsExhausted()
    {
        var quotaManager = new QuotaManager(TestResourceClassQuotas.Default, starvationDenialCountThreshold: 3, windowSeconds: 3600, new NoOpResourceQuotaExhaustedEventPublisher());
        quotaManager.RecordGrant(ResourceClass.BackgroundMaintenance); // Consumes the class's single Model-slot.
        var (controller, granted, deferred) = CreateController(quotaManager: quotaManager);

        controller.RequestBackgroundSlot("job-3", ResourceClass.BackgroundMaintenance);

        Assert.Equal(0, granted.CallCount);
        Assert.Equal(1, deferred.CallCount);
    }

    [Fact]
    public void RequestBackgroundSlot_RecoversAfterCpuLoadSubsides()
    {
        // Roadmap Demo/Acceptance criterion: "Under simulated high CPU load, a test background
        // job request is deferred; once load subsides, it is granted automatically."
        var cpuTier = CapacityTier.Warning;
        var quotaManager = new QuotaManager(TestResourceClassQuotas.Default, starvationDenialCountThreshold: 3, windowSeconds: 3600, new NoOpResourceQuotaExhaustedEventPublisher());
        var granted = new CapturingBackgroundJobGrantedEventPublisher();
        var deferred = new CapturingBackgroundJobDeferredEventPublisher();
        var controller = new BackgroundTaskController(() => cpuTier, quotaManager, granted, deferred);
        controller.RequestBackgroundSlot("job-4", ResourceClass.BackgroundMaintenance);
        Assert.Equal(1, deferred.CallCount);

        cpuTier = CapacityTier.Safe; // Load subsides.
        controller.RequestBackgroundSlot("job-4", ResourceClass.BackgroundMaintenance);

        Assert.Equal(1, granted.CallCount);
    }

    [Fact]
    public void RequestBackgroundSlot_DoesNotConsumeTheClassQuota_WhenDeferredForCpuLoad()
    {
        var quotaManager = new QuotaManager(TestResourceClassQuotas.Default, starvationDenialCountThreshold: 3, windowSeconds: 3600, new NoOpResourceQuotaExhaustedEventPublisher());
        var (controller, granted, _) = CreateController(cpuTier: CapacityTier.Warning, quotaManager: quotaManager);

        controller.RequestBackgroundSlot("job-5", ResourceClass.LearningActivities);

        Assert.Equal(0, granted.CallCount);
        Assert.False(quotaManager.IsClassQuotaExhausted(ResourceClass.LearningActivities));
    }

    [Fact]
    public void RequestBackgroundSlot_StarvationOverride_GrantsDespiteSustainedCpuContention()
    {
        // WP-022 Recovery Plan Slice R1/Finding F1: §19.4's "regardless of contention" must
        // override CPU-load-caused deferral too, not only quota exhaustion.
        var quotaManager = new QuotaManager(TestResourceClassQuotas.Default, starvationDenialCountThreshold: 3, windowSeconds: 3600, new NoOpResourceQuotaExhaustedEventPublisher());
        var granted = new CapturingBackgroundJobGrantedEventPublisher();
        var deferred = new CapturingBackgroundJobDeferredEventPublisher();
        var controller = new BackgroundTaskController(() => CapacityTier.Critical, quotaManager, granted, deferred);

        for (var i = 0; i < 10; i++)
        {
            controller.RequestBackgroundSlot("learning-job", ResourceClass.LearningActivities);
        }

        Assert.True(granted.CallCount > 0, "Starvation Prevention must force at least one grant despite sustained CPU contention.");
    }

    // WP-022 Recovery Plan Slice R2/Finding F2: ResourceQuotaExhausted must reflect the actual
    // cause of deferral, never fire for CPU contention, and always fire for genuine quota
    // exhaustion. (Maintenance-window deferral cannot be independently exercised here — per
    // WP-022 Implementation Plan Decision D10, WithinMaintenanceWindow() always returns true, so
    // that branch is currently unreachable; it shares the exact same Defer(...)/RecordDenial(...)
    // path already proven never to publish in QuotaManagerTests.RecordDenial_DoesNotPublishResourceQuotaExhausted.)
    [Fact]
    public void RequestBackgroundSlot_DoesNotPublishResourceQuotaExhausted_WhenDeferredForCpuLoad()
    {
        var quotaExhaustedPublisher = new CapturingResourceQuotaExhaustedEventPublisher();
        var quotaManager = new QuotaManager(TestResourceClassQuotas.Default, starvationDenialCountThreshold: 3, windowSeconds: 3600, quotaExhaustedPublisher);
        var controller = new BackgroundTaskController(() => CapacityTier.Critical, quotaManager, new NoOpBackgroundJobGrantedEventPublisher(), new NoOpBackgroundJobDeferredEventPublisher());

        controller.RequestBackgroundSlot("job-6", ResourceClass.BackgroundMaintenance);

        Assert.Equal(0, quotaExhaustedPublisher.CallCount);
    }

    [Fact]
    public void RequestBackgroundSlot_PublishesResourceQuotaExhausted_WhenClassQuotaIsGenuinelyExhausted()
    {
        var quotaExhaustedPublisher = new CapturingResourceQuotaExhaustedEventPublisher();
        var quotaManager = new QuotaManager(TestResourceClassQuotas.Default, starvationDenialCountThreshold: 3, windowSeconds: 3600, quotaExhaustedPublisher);
        quotaManager.RecordGrant(ResourceClass.BackgroundMaintenance); // Consumes the class's single Model-slot.
        var controller = new BackgroundTaskController(() => CapacityTier.Safe, quotaManager, new NoOpBackgroundJobGrantedEventPublisher(), new NoOpBackgroundJobDeferredEventPublisher());

        controller.RequestBackgroundSlot("job-7", ResourceClass.BackgroundMaintenance);

        Assert.Equal(1, quotaExhaustedPublisher.CallCount);
        Assert.Equal(ResourceClass.BackgroundMaintenance, quotaExhaustedPublisher.LastResourceClass);
    }

    private sealed class CapturingResourceQuotaExhaustedEventPublisher : IResourceQuotaExhaustedEventPublisher
    {
        public int CallCount { get; private set; }
        public ResourceClass LastResourceClass { get; private set; }
        public ResourceType LastResourceType { get; private set; }

        public void PublishResourceQuotaExhausted(ResourceClass resourceClass, ResourceType resourceType)
        {
            CallCount++;
            LastResourceClass = resourceClass;
            LastResourceType = resourceType;
        }
    }

    private sealed class CapturingBackgroundJobGrantedEventPublisher : IBackgroundJobGrantedEventPublisher
    {
        public int CallCount { get; private set; }
        public string? LastJobId { get; private set; }
        public ResourceClass LastResourceClass { get; private set; }

        public void PublishBackgroundJobGranted(string jobId, ResourceClass resourceClass)
        {
            CallCount++;
            LastJobId = jobId;
            LastResourceClass = resourceClass;
        }
    }

    private sealed class CapturingBackgroundJobDeferredEventPublisher : IBackgroundJobDeferredEventPublisher
    {
        public int CallCount { get; private set; }
        public string? LastJobId { get; private set; }
        public string? LastReason { get; private set; }

        public void PublishBackgroundJobDeferred(string jobId, string reason)
        {
            CallCount++;
            LastJobId = jobId;
            LastReason = reason;
        }
    }
}
