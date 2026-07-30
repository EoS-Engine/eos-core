namespace EOS.Knowledge;

/// <summary>
/// Memory-Management-Specification-v1.0 §16.2/§21's <c>MemoryConsolidated</c> emission —
/// informational only (§21: "Dashboard, Learning Engine (informational only...)"), emitted for
/// every <c>consolidate()</c> trigger, including the Gate-failure trigger (which still emits
/// this event even though it suppresses <c>LessonLearned</c> re-emission per ADR-015-002).
/// Per the Composition Root Adapter Pattern (ADR-015-001): <c>EOS.Knowledge</c> defines this
/// small, BCL-typed interface; <c>EOS.Runner</c>'s <c>Program.cs</c> supplies the concrete
/// adapter bridging to <c>EventEnvelope</c>/<c>EventMediator</c>, which <c>EOS.Knowledge</c> has
/// no legal dependency path to reach directly.
/// </summary>
public interface IMemoryConsolidatedEventPublisher
{
    void PublishMemoryConsolidated(MemoryType sourceMemoryType, Guid episodicEntryId);
}
