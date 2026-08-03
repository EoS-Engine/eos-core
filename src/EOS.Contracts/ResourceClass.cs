namespace EOS.Contracts;

/// <summary>
/// Resource-Management-Specification-v1.0 §16's fixed, five-value resource-allocation-class
/// hierarchy, in rank order (User Requests highest, Learning Activities lowest) — distinct from
/// Planning & Execution Engine's numeric task-dispatch-order priority score (ADR-RM002).
/// </summary>
public enum ResourceClass
{
    UserRequests,
    InteractiveSessions,
    AutonomousTasks,
    BackgroundMaintenance,
    LearningActivities,
}
