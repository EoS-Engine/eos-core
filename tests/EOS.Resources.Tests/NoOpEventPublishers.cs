using EOS.Contracts;
using EOS.Resources;

namespace EOS.Resources.Tests;

internal sealed class NoOpResourceThresholdCrossedEventPublisher : IResourceThresholdCrossedEventPublisher
{
    public void PublishResourceThresholdCrossed(ResourceType resourceType, CapacityTier tier)
    {
    }
}

internal sealed class NoOpEmergencyCapacitySignalEventPublisher : IEmergencyCapacitySignalEventPublisher
{
    public void PublishEmergencyCapacitySignal(ResourceType resourceType, double measuredValue)
    {
    }
}

internal sealed class NoOpResourceRecoveredEventPublisher : IResourceRecoveredEventPublisher
{
    public void PublishResourceRecovered(ResourceType resourceType)
    {
    }
}

internal sealed class NoOpModelLoadedEventPublisher : IModelLoadedEventPublisher
{
    public void PublishModelLoaded(string modelId, double ramFootprintMegabytes)
    {
    }
}

internal sealed class NoOpModelUnloadedEventPublisher : IModelUnloadedEventPublisher
{
    public void PublishModelUnloaded(string modelId, double ramFootprintMegabytes)
    {
    }
}

internal sealed class NoOpResourceQuotaExhaustedEventPublisher : IResourceQuotaExhaustedEventPublisher
{
    public void PublishResourceQuotaExhausted(ResourceClass resourceClass, ResourceType resourceType)
    {
    }
}

internal sealed class NoOpBackgroundJobGrantedEventPublisher : IBackgroundJobGrantedEventPublisher
{
    public void PublishBackgroundJobGranted(string jobId, ResourceClass resourceClass)
    {
    }
}

internal sealed class NoOpBackgroundJobDeferredEventPublisher : IBackgroundJobDeferredEventPublisher
{
    public void PublishBackgroundJobDeferred(string jobId, string reason)
    {
    }
}

internal static class TestResourceClassQuotas
{
    public static ResourceClassQuotas Default { get; } = new(
        UserRequests: new ResourceClassQuota(100, 8192, 3),
        InteractiveSessions: new ResourceClassQuota(90, 6144, 2),
        AutonomousTasks: new ResourceClassQuota(70, 4096, 2),
        BackgroundMaintenance: new ResourceClassQuota(40, 2048, 1),
        LearningActivities: new ResourceClassQuota(20, 1024, 1));
}
