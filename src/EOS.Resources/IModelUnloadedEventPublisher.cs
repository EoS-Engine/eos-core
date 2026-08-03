namespace EOS.Resources;

/// <summary>
/// Resource-Management-Specification-v1.0 §20's <c>ModelUnloaded</c> event (producer: Resource
/// Monitor), per the Composition Root Adapter Pattern (ADR-015-001).
/// </summary>
public interface IModelUnloadedEventPublisher
{
    void PublishModelUnloaded(string modelId, double ramFootprintMegabytes);
}
