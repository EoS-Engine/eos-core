namespace EOS.Knowledge;

/// <summary>
/// Knowledge-Management-Specification-v1.0 §20.2's consumed <c>IReasoningEngineClient.compare()</c>
/// (Reasoning-Engine-Specification-v1.0 §16.1), "consumed for genuine semantic similarity
/// (§18.3), never re-implemented." <c>IReasoningEngineClient</c> (<c>EOS.Contracts</c>) has no
/// <c>compare()</c> member yet — real semantic comparison does not exist until WP-020. Per the
/// Composition Root Adapter Pattern (ADR-015-001 precedent, identical to WP-016's
/// <c>ISummarizer</c>): <c>EOS.Knowledge</c> defines this small interface; <c>EOS.Runner</c>'s
/// <c>Program.cs</c> supplies the concrete adapter — a real, WP-020-backed implementation once
/// it exists, an honestly-documented structural stub (never claiming real semantic judgment)
/// until then, per WP-018's own roadmap-authorized scope: "this WP stubs that call structurally
/// until WP-020 exists."
/// </summary>
public interface ICompareProvider
{
    Task<bool> AreSimilarAsync(string contentA, string contentB, CancellationToken cancellationToken = default);
}
