using System.Text.Json;
using EOS.Contracts;

namespace EOS.Orchestrator.Tests;

public class EventEnvelopeTests
{
    private sealed record SamplePayload(string Message);

    [Fact]
    public void Envelope_SurvivesJsonRoundTrip()
    {
        var original = EventEnvelope<SamplePayload>.Create(
            eventType: "SampleEvent",
            version: "v1",
            producer: "EOS.Orchestrator.Tests",
            payload: new SamplePayload("hello"));

        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<EventEnvelope<SamplePayload>>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(original.EventId, roundTripped.EventId);
        Assert.Equal(original.EventType, roundTripped.EventType);
        Assert.Equal(original.Version, roundTripped.Version);
        Assert.Equal(original.Producer, roundTripped.Producer);
        Assert.Equal(original.CorrelationId, roundTripped.CorrelationId);
        Assert.Equal(original.CausationId, roundTripped.CausationId);
        Assert.Equal(original.OccurredAt, roundTripped.OccurredAt);
        Assert.Equal(original.Payload, roundTripped.Payload);
    }
}
