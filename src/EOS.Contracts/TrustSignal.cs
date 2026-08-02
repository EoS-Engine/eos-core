namespace EOS.Contracts;

/// <summary>
/// Learning-Engine-Specification-v1.1 §12/§14.2 — <c>IReasoningEngineClient.get_trust_signal()</c>'s
/// return shape, ratified verbatim by Reasoning-Engine-Specification-v1.0 §16.1. Postcondition
/// (§14.2): <see cref="Score"/> in [0.0, 1.0]; if no history exists for the role, returns a
/// neutral default (0.5), never null.
/// </summary>
public sealed record TrustSignal(string SourceRole, double Score, string EvidenceRef);
