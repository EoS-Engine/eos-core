namespace EOS.Learning;

/// <summary>
/// Learning-Engine-Specification-v1.1 §15's <c>PlatformCapabilityPipelineAdvanced</c> event,
/// carried forward unchanged from v1.0 — see <see cref="IBestPracticeRatifiedEventPublisher"/>
/// for why the payload is the minimal, non-invented record-identity shape. §11.5's Feedback Loop
/// Guard subscribes to exactly this event.
/// </summary>
public interface IPlatformCapabilityPipelineAdvancedEventPublisher
{
    void PublishPlatformCapabilityPipelineAdvanced(Guid recordId);
}
