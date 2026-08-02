namespace EOS.Contracts;

/// <summary>
/// Learning-Engine-Specification-v1.1 §12/§14.1 — <c>IReasoningEngineClient.compare()</c>'s
/// return shape, ratified verbatim by Reasoning-Engine-Specification-v1.0 §16.1. Postconditions
/// (§14.1): <see cref="Confidence"/> in [0.0, 1.0]; <see cref="AcceptedMatches"/> union
/// <see cref="RejectedMatches"/> equals all input candidates (no candidate silently dropped).
/// </summary>
public sealed record ConfidenceGuardResult(
    double Confidence,
    PipelineRecord[] AcceptedMatches,
    RejectedMatch[] RejectedMatches);
