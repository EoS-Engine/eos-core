namespace EOS.Knowledge;

/// <summary>
/// Memory-Management-Specification-v1.0 §21's <c>ContextAssembled</c> event, per the
/// Composition Root Adapter Pattern (ADR-015-001): <c>EOS.Knowledge</c> defines this small,
/// BCL-typed interface; <c>EOS.Runner</c>'s <c>Program.cs</c> supplies the concrete adapter
/// bridging to <c>EventEnvelope</c>/<c>EventMediator</c> (<c>EOS.Contracts</c>/
/// <c>EOS.Orchestrator</c>), which <c>EOS.Knowledge</c> has no legal dependency path to reach
/// directly.
/// </summary>
public interface IContextAssemblyEventPublisher
{
    void PublishContextAssembled(Guid requestId, int itemCount, bool truncated);
}
