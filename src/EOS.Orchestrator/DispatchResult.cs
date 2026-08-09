using EOS.Contracts;

namespace EOS.Orchestrator;

public sealed record DispatchResult(DispatchOutcome Outcome, DispatchedTask? Task);
