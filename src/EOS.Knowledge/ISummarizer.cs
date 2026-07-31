namespace EOS.Knowledge;

/// <summary>
/// Memory-Management-Specification-v1.0 §17.2's content-generation delegation to
/// <c>EOS.Reasoning</c>'s <c>summarize()</c>, ratified by name in Reasoning-Engine-Specification-
/// v1.0 but not implemented until WP-020 (<c>IReasoningEngineClient</c> in <c>EOS.Contracts</c>
/// has no <c>summarize()</c> member yet). Per the Composition Root Adapter Pattern
/// (ADR-015-001 precedent): <c>EOS.Knowledge</c> defines this small interface;
/// <c>EOS.Runner</c>'s <c>Program.cs</c> supplies the concrete adapter — a real, WP-020-backed
/// implementation once it exists, an honestly-documented stub (never claiming real
/// summarization) until then, per this WP's own explicit "stubs that call until WP-020 exists,
/// then is revisited" scope.
/// </summary>
public interface ISummarizer
{
    Task<string> SummarizeAsync(string content, CancellationToken cancellationToken = default);
}
