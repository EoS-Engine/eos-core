namespace EOS.Orchestrator;

/// <summary>
/// Observable result of one <see cref="ExecutionCoordinator.DispatchNextAsync"/> call — lets
/// callers (and tests) distinguish "nothing was eligible this cycle" from "Protection denied the
/// one eligible Task" (Planning-Execution-Engine-Specification-v1.0 roadmap Test Verification:
/// "an integration test confirming a Task is denied dispatch when Protection returns Deny").
/// </summary>
public enum DispatchOutcome
{
    NoEligibleTask,
    ProtectionDenied,
    Dispatched,
}
