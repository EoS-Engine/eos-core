namespace EOS.Contracts;

public enum ReasoningFailureMode
{
    MissingContext,
    ConflictingEvidence,
    InvalidGoal,
    AmbiguousRequest,
    UnsupportedTask,
    InternalError,
}
