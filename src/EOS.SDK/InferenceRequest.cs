namespace EOS.SDK;

public sealed record InferenceRequest(
    Guid RequestId,
    Guid CorrelationId,
    string CapabilityRequired,
    string Payload,
    string? ContextPayloadRef,
    int TokenBudgetEstimate,
    int Priority,
    string Caller);
