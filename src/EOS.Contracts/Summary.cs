namespace EOS.Contracts;

/// <summary>
/// Reasoning-Engine-Specification-v1.0 §16.1/§19.3 — <c>IReasoningEngineClient.summarize()</c>'s
/// return shape, satisfying the call shape already assumed by Memory-Management-Specification-v1.0
/// §17.2 (<c>ReasoningEngine.summarize(entry.content)</c>).
/// </summary>
public sealed record Summary(string Content);
