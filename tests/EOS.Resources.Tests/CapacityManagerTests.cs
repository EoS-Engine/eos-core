using EOS.Contracts;
using EOS.Resources;

namespace EOS.Resources.Tests;

public class CapacityManagerTests
{
    private static CapacityManager CreateManager(CapacityThresholds thresholds, IResourceThresholdCrossedEventPublisher publisher) =>
        new(thresholds, publisher, new CapturingEmergencyCapacitySignalEventPublisher(), new CapturingResourceRecoveredEventPublisher());

    private static CapacityThresholds CreateThresholds() => new(
        Cpu: new ResourceTierBoundaries(Warning: 75, Critical: 90, Emergency: 97),
        Ram: new ResourceTierBoundaries(Warning: 6000, Critical: 7200, Emergency: 7800),
        Disk: new ResourceTierBoundaries(Warning: 350000, Critical: 420000, Emergency: 460000),
        ModelUsage: new ResourceTierBoundaries(Warning: 70000, Critical: 85000, Emergency: 95000),
        QueueLength: new ResourceTierBoundaries(Warning: 50, Critical: 100, Emergency: 150),
        BackgroundTasks: new ResourceTierBoundaries(Warning: 2, Critical: 3, Emergency: 4),
        CacheUsage: new ResourceTierBoundaries(Warning: 70, Critical: 85, Emergency: 95));

    [Theory]
    [InlineData(0, CapacityTier.Safe)]
    [InlineData(74, CapacityTier.Safe)]
    [InlineData(75, CapacityTier.Warning)]
    [InlineData(89, CapacityTier.Warning)]
    [InlineData(90, CapacityTier.Critical)]
    [InlineData(96, CapacityTier.Critical)]
    [InlineData(97, CapacityTier.Emergency)]
    [InlineData(100, CapacityTier.Emergency)]
    public void ComputeTier_ClassifiesCpuAcrossAllFourTierBoundaries(double measuredValue, CapacityTier expected)
    {
        var manager = CreateManager(CreateThresholds(), new CapturingResourceThresholdCrossedEventPublisher());

        var tier = manager.ComputeTier(ResourceType.Cpu, measuredValue);

        Assert.Equal(expected, tier);
    }

    [Fact]
    public void ComputeTier_NeverReturnsEmptyHeadroom_AtExactEmergencyBoundary()
    {
        // FR-RM3: the Emergency threshold is never zero-headroom — verified here as: a value
        // exactly at the configured Emergency boundary still classifies deterministically as
        // Emergency (the boundary itself is real, configured headroom below 100%/the ceiling,
        // not an unreachable value), per §17.4.
        var manager = CreateManager(CreateThresholds(), new CapturingResourceThresholdCrossedEventPublisher());

        var tier = manager.ComputeTier(ResourceType.Cpu, measuredValue: 97);

        Assert.Equal(CapacityTier.Emergency, tier);
        Assert.True(97 < 100, "Emergency boundary must leave real headroom below the dimension's own ceiling.");
    }

    [Fact]
    public void ComputeTier_DoesNotPublish_OnFirstEverClassification()
    {
        var publisher = new CapturingResourceThresholdCrossedEventPublisher();
        var manager = CreateManager(CreateThresholds(), publisher);

        manager.ComputeTier(ResourceType.Cpu, measuredValue: 50);

        Assert.Equal(0, publisher.CallCount);
    }

    [Fact]
    public void ComputeTier_Publishes_WhenTierChangesFromPreviousClassification()
    {
        var publisher = new CapturingResourceThresholdCrossedEventPublisher();
        var manager = CreateManager(CreateThresholds(), publisher);
        manager.ComputeTier(ResourceType.Cpu, measuredValue: 50); // Safe, establishes baseline

        manager.ComputeTier(ResourceType.Cpu, measuredValue: 80); // Warning

        Assert.Equal(1, publisher.CallCount);
        Assert.Equal(ResourceType.Cpu, publisher.LastResourceType);
        Assert.Equal(CapacityTier.Warning, publisher.LastTier);
    }

    [Fact]
    public void ComputeTier_DoesNotPublish_WhenTierIsUnchanged()
    {
        var publisher = new CapturingResourceThresholdCrossedEventPublisher();
        var manager = CreateManager(CreateThresholds(), publisher);
        manager.ComputeTier(ResourceType.Cpu, measuredValue: 50); // Safe

        manager.ComputeTier(ResourceType.Cpu, measuredValue: 55); // Still Safe

        Assert.Equal(0, publisher.CallCount);
    }

    [Fact]
    public void ComputeTier_TracksEachResourceTypeIndependently()
    {
        var publisher = new CapturingResourceThresholdCrossedEventPublisher();
        var manager = CreateManager(CreateThresholds(), publisher);
        manager.ComputeTier(ResourceType.Cpu, measuredValue: 50);
        manager.ComputeTier(ResourceType.Ram, measuredValue: 100);

        manager.ComputeTier(ResourceType.Cpu, measuredValue: 80); // Cpu: Safe -> Warning

        Assert.Equal(1, publisher.CallCount);
        Assert.Equal(ResourceType.Cpu, publisher.LastResourceType);
    }

    // WP-022 Implementation Plan Decision D6: EmergencyCapacitySignal fires only on transition
    // to Emergency; ResourceRecovered fires only on Critical/Emergency -> Safe.
    [Fact]
    public void ComputeTier_PublishesEmergencyCapacitySignal_OnTransitionToEmergency()
    {
        var emergencyPublisher = new CapturingEmergencyCapacitySignalEventPublisher();
        var manager = new CapacityManager(CreateThresholds(), new CapturingResourceThresholdCrossedEventPublisher(), emergencyPublisher, new CapturingResourceRecoveredEventPublisher());
        manager.ComputeTier(ResourceType.Cpu, measuredValue: 50); // Safe, establishes baseline

        manager.ComputeTier(ResourceType.Cpu, measuredValue: 97); // Emergency

        Assert.Equal(1, emergencyPublisher.CallCount);
        Assert.Equal(ResourceType.Cpu, emergencyPublisher.LastResourceType);
        Assert.Equal(97, emergencyPublisher.LastMeasuredValue);
    }

    [Fact]
    public void ComputeTier_DoesNotPublishEmergencyCapacitySignal_OnTransitionToWarningOnly()
    {
        var emergencyPublisher = new CapturingEmergencyCapacitySignalEventPublisher();
        var manager = new CapacityManager(CreateThresholds(), new CapturingResourceThresholdCrossedEventPublisher(), emergencyPublisher, new CapturingResourceRecoveredEventPublisher());
        manager.ComputeTier(ResourceType.Cpu, measuredValue: 50); // Safe

        manager.ComputeTier(ResourceType.Cpu, measuredValue: 80); // Warning

        Assert.Equal(0, emergencyPublisher.CallCount);
    }

    [Fact]
    public void ComputeTier_PublishesResourceRecovered_OnTransitionFromCriticalToSafe()
    {
        var recoveredPublisher = new CapturingResourceRecoveredEventPublisher();
        var manager = new CapacityManager(CreateThresholds(), new CapturingResourceThresholdCrossedEventPublisher(), new CapturingEmergencyCapacitySignalEventPublisher(), recoveredPublisher);
        manager.ComputeTier(ResourceType.Cpu, measuredValue: 90); // Critical

        manager.ComputeTier(ResourceType.Cpu, measuredValue: 50); // Safe

        Assert.Equal(1, recoveredPublisher.CallCount);
        Assert.Equal(ResourceType.Cpu, recoveredPublisher.LastResourceType);
    }

    [Fact]
    public void ComputeTier_PublishesResourceRecovered_OnTransitionFromEmergencyToSafe()
    {
        var recoveredPublisher = new CapturingResourceRecoveredEventPublisher();
        var manager = new CapacityManager(CreateThresholds(), new CapturingResourceThresholdCrossedEventPublisher(), new CapturingEmergencyCapacitySignalEventPublisher(), recoveredPublisher);
        manager.ComputeTier(ResourceType.Cpu, measuredValue: 97); // Emergency

        manager.ComputeTier(ResourceType.Cpu, measuredValue: 50); // Safe

        Assert.Equal(1, recoveredPublisher.CallCount);
    }

    [Fact]
    public void ComputeTier_DoesNotPublishResourceRecovered_OnTransitionFromCriticalToWarningOnly()
    {
        var recoveredPublisher = new CapturingResourceRecoveredEventPublisher();
        var manager = new CapacityManager(CreateThresholds(), new CapturingResourceThresholdCrossedEventPublisher(), new CapturingEmergencyCapacitySignalEventPublisher(), recoveredPublisher);
        manager.ComputeTier(ResourceType.Cpu, measuredValue: 90); // Critical

        manager.ComputeTier(ResourceType.Cpu, measuredValue: 80); // Warning, not Safe

        Assert.Equal(0, recoveredPublisher.CallCount);
    }

    // CodeRabbit PR #19 finding: a gradual Critical -> Warning -> Safe descent must still
    // publish ResourceRecovered once Safe is reached, not only a direct Critical/Emergency ->
    // Safe transition — §19.5's "after a Critical/Emergency threshold crossing... resolves"
    // does not require the immediately preceding sample to itself be Critical/Emergency.
    [Fact]
    public void ComputeTier_PublishesResourceRecovered_AfterAGradualDescentThroughWarning()
    {
        var recoveredPublisher = new CapturingResourceRecoveredEventPublisher();
        var manager = new CapacityManager(CreateThresholds(), new CapturingResourceThresholdCrossedEventPublisher(), new CapturingEmergencyCapacitySignalEventPublisher(), recoveredPublisher);
        manager.ComputeTier(ResourceType.Cpu, measuredValue: 90); // Critical
        manager.ComputeTier(ResourceType.Cpu, measuredValue: 80); // Warning (recovery still pending)

        manager.ComputeTier(ResourceType.Cpu, measuredValue: 50); // Safe

        Assert.Equal(1, recoveredPublisher.CallCount);
        Assert.Equal(ResourceType.Cpu, recoveredPublisher.LastResourceType);
    }

    [Fact]
    public void ComputeTier_DoesNotRepublishResourceRecovered_OnRepeatedSafeReadingsAfterRecovery()
    {
        var recoveredPublisher = new CapturingResourceRecoveredEventPublisher();
        var manager = new CapacityManager(CreateThresholds(), new CapturingResourceThresholdCrossedEventPublisher(), new CapturingEmergencyCapacitySignalEventPublisher(), recoveredPublisher);
        manager.ComputeTier(ResourceType.Cpu, measuredValue: 90); // Critical
        manager.ComputeTier(ResourceType.Cpu, measuredValue: 50); // Safe -> publishes once

        manager.ComputeTier(ResourceType.Cpu, measuredValue: 55); // Still Safe

        Assert.Equal(1, recoveredPublisher.CallCount);
    }

    private sealed class CapturingResourceThresholdCrossedEventPublisher : IResourceThresholdCrossedEventPublisher
    {
        public int CallCount { get; private set; }
        public ResourceType LastResourceType { get; private set; }
        public CapacityTier LastTier { get; private set; }

        public void PublishResourceThresholdCrossed(ResourceType resourceType, CapacityTier tier)
        {
            CallCount++;
            LastResourceType = resourceType;
            LastTier = tier;
        }
    }

    private sealed class CapturingEmergencyCapacitySignalEventPublisher : IEmergencyCapacitySignalEventPublisher
    {
        public int CallCount { get; private set; }
        public ResourceType LastResourceType { get; private set; }
        public double LastMeasuredValue { get; private set; }

        public void PublishEmergencyCapacitySignal(ResourceType resourceType, double measuredValue)
        {
            CallCount++;
            LastResourceType = resourceType;
            LastMeasuredValue = measuredValue;
        }
    }

    private sealed class CapturingResourceRecoveredEventPublisher : IResourceRecoveredEventPublisher
    {
        public int CallCount { get; private set; }
        public ResourceType LastResourceType { get; private set; }

        public void PublishResourceRecovered(ResourceType resourceType)
        {
            CallCount++;
            LastResourceType = resourceType;
        }
    }
}
