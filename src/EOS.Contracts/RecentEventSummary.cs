namespace EOS.Contracts;

public sealed record RecentEventSummary(
    Guid EventId,
    string EventType,
    string Producer,
    DateTimeOffset OccurredAt,
    string PayloadJson);
