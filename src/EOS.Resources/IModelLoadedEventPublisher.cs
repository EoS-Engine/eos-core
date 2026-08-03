namespace EOS.Resources;

/// <summary>
/// Resource-Management-Specification-v1.0 §20's <c>ModelLoaded</c> event (producer: Resource
/// Monitor, per §24's Component Diagram edge exposing <c>get_model_residency</c>), per the
/// Composition Root Adapter Pattern (ADR-015-001).
/// </summary>
public interface IModelLoadedEventPublisher
{
    void PublishModelLoaded(string modelId, double ramFootprintMegabytes);
}
