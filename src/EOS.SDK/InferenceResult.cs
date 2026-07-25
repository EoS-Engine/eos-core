namespace EOS.SDK;

public sealed record InferenceResult(
    bool Success,
    string? Output,
    string? Model,
    int? PromptTokens,
    int? CompletionTokens,
    TimeSpan? Latency,
    InferenceErrorType? ErrorType,
    string? ErrorMessage);
