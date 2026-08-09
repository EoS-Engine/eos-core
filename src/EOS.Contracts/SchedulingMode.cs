namespace EOS.Contracts;

/// <summary>
/// Planning-Execution-Engine-Specification-v1.0 §14's seven Scheduling modes — every mode passes
/// through the same unchanged Scheduling Algorithm (Constitution Part 7 §7.3) and Execution
/// Coordinator/Protection gate (§10.7); modes differ only in *when* a <c>DispatchedTask</c>
/// becomes eligible for that algorithm to consider it, never in *how* it dispatches.
/// </summary>
public enum SchedulingMode
{
    Immediate,
    Delayed,
    Background,
    Scheduled,
    Periodic,
    EventDriven,
    IdleTime,
}
