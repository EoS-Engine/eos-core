namespace EOS.Contracts;

public sealed record ReasoningRequest(
    Guid RequestId,
    Guid CorrelationId,
    string Goal,
    string RequestingRole);
