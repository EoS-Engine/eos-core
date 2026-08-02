namespace EOS.Contracts;

/// <summary>
/// Reasoning-Engine-Specification-v1.0 §13.2's Decision Inputs. <see cref="ReasoningType"/>,
/// <see cref="Constraints"/>, and <see cref="ContextScope"/> are additive to the WP-008 baseline
/// — all optional, so existing callers that supply only the original four fields continue to
/// behave exactly as before <see cref="EOS.Contracts.ReasoningType"/> is left unspecified
/// (§13.2: "may be inferred if unspecified").
/// </summary>
public sealed record ReasoningRequest(
    Guid RequestId,
    Guid CorrelationId,
    string Goal,
    string RequestingRole,
    ReasoningType? ReasoningType = null,
    string[]? Constraints = null,
    ReasoningContextScope? ContextScope = null);
