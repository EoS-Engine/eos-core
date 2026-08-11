namespace EOS.Contracts;

/// <summary>
/// Autonomous-Engineering-Loop-Specification-v1.0 §8's Trigger Sources, reduced to the minimal
/// shape <see cref="ILoopControlClient"/>'s caller-facing entry point needs — WP-028 Decision 6
/// (locked): no discriminated union, no trigger-specific payload hierarchy. <see cref="TriggerSource"/>
/// is one of the 7 in-scope names (<c>UserRequest</c>, <c>ManualRequest</c>, <c>ScheduledTask</c>,
/// <c>LearningOpportunity</c>, <c>KnowledgeUpdate</c>, <c>PerformanceDegradation</c>,
/// <c>Failure</c>) — File/Git Events are permanently excluded (§8.9) and never constructed here.
/// <see cref="SourcePayloadRef"/>'s exact meaning is trigger-source-specific (a Goal statement for
/// User/Manual Requests, a resource type name for Performance Degradation, a Task id for Failure,
/// etc.) — deliberately untyped, matching Decision 6's rejection of a richer shape.
/// </summary>
public sealed record TriggerContext(string TriggerSource, string? SourcePayloadRef);
