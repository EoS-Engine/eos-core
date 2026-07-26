namespace EOS.Contracts;

public sealed class ReasoningFailedException(ReasoningFailureMode failureMode, string message)
    : Exception(message)
{
    public ReasoningFailureMode FailureMode { get; } = failureMode;
}
