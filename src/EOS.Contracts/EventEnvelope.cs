namespace EOS.Contracts;

public sealed record EventEnvelope<TPayload>(
    Guid EventId,
    string EventType,
    string Version,
    string Producer,
    Guid CorrelationId,
    Guid? CausationId,
    DateTimeOffset OccurredAt,
    TPayload Payload)
{
    public static EventEnvelope<TPayload> Create(
        string eventType,
        string version,
        string producer,
        TPayload payload,
        Guid? correlationId = null,
        Guid? causationId = null)
    {
        return new EventEnvelope<TPayload>(
            EventId: Guid.NewGuid(),
            EventType: eventType,
            Version: version,
            Producer: producer,
            CorrelationId: correlationId ?? Guid.NewGuid(),
            CausationId: causationId,
            OccurredAt: DateTimeOffset.UtcNow,
            Payload: payload);
    }
}
