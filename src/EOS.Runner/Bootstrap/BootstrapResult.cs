namespace EOS.Runner.Bootstrap;

public sealed record BootstrapResult(
    string StepName,
    bool Status,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    TimeSpan Duration,
    string? Error);
