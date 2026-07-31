namespace EOS.Knowledge;

/// <summary>
/// Memory-Management-Specification-v1.0 §17.2's <c>MemoryCompressed</c> event, per the
/// Composition Root Adapter Pattern (ADR-015-001): <c>EOS.Knowledge</c> defines this small
/// interface; <c>EOS.Runner</c>'s <c>Program.cs</c> supplies the concrete adapter bridging to
/// <c>EventEnvelope</c>/<c>EventMediator</c>, which <c>EOS.Knowledge</c> has no legal
/// dependency path to reach directly.
/// </summary>
public interface IMemoryCompressedEventPublisher
{
    void PublishMemoryCompressed(Guid entryId, int originalSize, int summarySize);
}
