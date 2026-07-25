namespace EOS.Infrastructure;

public sealed record StoredEvent(
    Guid EventId,
    string EventType,
    string Version,
    string Producer,
    Guid CorrelationId,
    Guid? CausationId,
    DateTimeOffset OccurredAt,
    string PayloadJson);
