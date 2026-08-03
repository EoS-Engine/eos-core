using EOS.Contracts;
using EOS.Resources;

namespace EOS.Resources.Tests;

public class QuotaManagerTests
{
    private static QuotaManager CreateManager(int starvationDenialCountThreshold = 3, int windowSeconds = 3600, IResourceQuotaExhaustedEventPublisher? eventPublisher = null) =>
        new(TestResourceClassQuotas.Default, starvationDenialCountThreshold, windowSeconds, eventPublisher ?? new NoOpResourceQuotaExhaustedEventPublisher());

    [Fact]
    public void IsClassQuotaExhausted_ReturnsFalse_ForFreshClassWithPositiveQuota()
    {
        var manager = CreateManager();

        Assert.False(manager.IsClassQuotaExhausted(ResourceClass.BackgroundMaintenance));
    }

    [Fact]
    public void IsClassQuotaExhausted_ReturnsTrue_OnceModelSlotQuotaIsReachedWithinTheWindow()
    {
        // §19.2's Model-slot ceiling for BackgroundMaintenance is 1 (TestResourceClassQuotas).
        var manager = CreateManager();
        manager.RecordGrant(ResourceClass.BackgroundMaintenance);

        Assert.True(manager.IsClassQuotaExhausted(ResourceClass.BackgroundMaintenance));
    }

    [Fact]
    public void IsClassQuotaExhausted_ResetsAfterTheWindowElapses()
    {
        var manager = CreateManager(windowSeconds: 0);
        manager.RecordGrant(ResourceClass.BackgroundMaintenance);

        // windowSeconds: 0 means any elapsed time (even sub-millisecond) is already "beyond the
        // window", exercising the reset path deterministically without a real sleep.
        Assert.False(manager.IsClassQuotaExhausted(ResourceClass.BackgroundMaintenance));
    }

    [Fact]
    public void IsClassQuotaExhausted_TracksEachResourceClassIndependently()
    {
        var manager = CreateManager();
        manager.RecordGrant(ResourceClass.BackgroundMaintenance);

        Assert.True(manager.IsClassQuotaExhausted(ResourceClass.BackgroundMaintenance));
        Assert.False(manager.IsClassQuotaExhausted(ResourceClass.LearningActivities));
    }

    // §19.4 Starvation Prevention (WP-022 Implementation Plan Decision D4): a class denied for
    // more than the configured number of consecutive evaluations is guaranteed its next slot.
    [Fact]
    public void IsClassQuotaExhausted_GuaranteesGrant_AfterConsecutiveDenialsReachTheThreshold()
    {
        var manager = CreateManager(starvationDenialCountThreshold: 3);
        manager.RecordGrant(ResourceClass.LearningActivities); // Exhaust the single Model-slot.
        manager.RecordDenial(ResourceClass.LearningActivities);
        manager.RecordDenial(ResourceClass.LearningActivities);
        manager.RecordDenial(ResourceClass.LearningActivities);

        Assert.False(manager.IsClassQuotaExhausted(ResourceClass.LearningActivities));
    }

    [Fact]
    public void IsClassQuotaExhausted_DoesNotOverrideBelowTheStarvationThreshold()
    {
        var manager = CreateManager(starvationDenialCountThreshold: 3);
        manager.RecordGrant(ResourceClass.LearningActivities);
        manager.RecordDenial(ResourceClass.LearningActivities);
        manager.RecordDenial(ResourceClass.LearningActivities);

        Assert.True(manager.IsClassQuotaExhausted(ResourceClass.LearningActivities));
    }

    [Fact]
    public void RecordGrant_ResetsTheConsecutiveDenialCounter()
    {
        // Threshold 2, two prior denials would otherwise force the starvation override (always
        // "not exhausted") to mask the real quota state. If RecordGrant failed to reset the
        // denial counter, the starvation override would still be active here and this would
        // incorrectly report "not exhausted" despite the class's single Model-slot already being
        // consumed by the grant just recorded.
        var manager = CreateManager(starvationDenialCountThreshold: 2, windowSeconds: 3600);
        manager.RecordDenial(ResourceClass.LearningActivities);
        manager.RecordDenial(ResourceClass.LearningActivities);

        manager.RecordGrant(ResourceClass.LearningActivities);

        Assert.True(manager.IsClassQuotaExhausted(ResourceClass.LearningActivities));
    }

    // WP-022 Recovery Plan Slice R2/Finding F2: RecordDenial (the starvation counter) and
    // PublishQuotaExhausted (the §20 event) are separate responsibilities — RecordDenial alone
    // must never publish anything.
    [Fact]
    public void RecordDenial_DoesNotPublishResourceQuotaExhausted()
    {
        var publisher = new CapturingResourceQuotaExhaustedEventPublisher();
        var manager = CreateManager(eventPublisher: publisher);

        manager.RecordDenial(ResourceClass.BackgroundMaintenance);

        Assert.Equal(0, publisher.CallCount);
    }

    [Fact]
    public void PublishQuotaExhausted_PublishesResourceQuotaExhausted()
    {
        var publisher = new CapturingResourceQuotaExhaustedEventPublisher();
        var manager = CreateManager(eventPublisher: publisher);

        manager.PublishQuotaExhausted(ResourceClass.BackgroundMaintenance, ResourceType.BackgroundTasks);

        Assert.Equal(1, publisher.CallCount);
        Assert.Equal(ResourceClass.BackgroundMaintenance, publisher.LastResourceClass);
        Assert.Equal(ResourceType.BackgroundTasks, publisher.LastResourceType);
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
}
