using EOS.Contracts;

namespace EOS.Orchestrator.Tests;

public class EventMediatorTests
{
    private sealed record Event1Payload(string Message);

    private sealed record Event2Payload(string Message);

    [Fact]
    public void Publish_DeliversToAllRegisteredSubscribers()
    {
        var mediator = new EventMediator();
        var receivedByFirst = new List<EventEnvelope<Event1Payload>>();
        var receivedBySecond = new List<EventEnvelope<Event1Payload>>();

        mediator.Subscribe<Event1Payload>(receivedByFirst.Add);
        mediator.Subscribe<Event1Payload>(receivedBySecond.Add);

        var envelope = EventEnvelope<Event1Payload>.Create(
            eventType: "Event1",
            version: "v1",
            producer: "EOS.Orchestrator.Tests",
            payload: new Event1Payload("hello"));

        mediator.Publish(envelope);

        Assert.Single(receivedByFirst);
        Assert.Single(receivedBySecond);
        Assert.Equal(envelope.CorrelationId, receivedByFirst[0].CorrelationId);
        Assert.Equal(envelope.CorrelationId, receivedBySecond[0].CorrelationId);
    }

    [Fact]
    public void Publish_DoesNotDeliverToSubscribersOfADifferentPayloadType()
    {
        var mediator = new EventMediator();
        var receivedEvent2 = new List<EventEnvelope<Event2Payload>>();

        mediator.Subscribe<Event2Payload>(receivedEvent2.Add);

        var event1 = EventEnvelope<Event1Payload>.Create(
            eventType: "Event1",
            version: "v1",
            producer: "EOS.Orchestrator.Tests",
            payload: new Event1Payload("hello"));

        mediator.Publish(event1);

        Assert.Empty(receivedEvent2);
    }

    [Fact]
    public void Publish_PropagatesCorrelationAndCausationId_AcrossTwoHops()
    {
        var mediator = new EventMediator();
        EventEnvelope<Event2Payload>? capturedEvent2 = null;

        mediator.Subscribe<Event1Payload>(event1 =>
        {
            var event2 = EventEnvelope<Event2Payload>.Create(
                eventType: "Event2",
                version: "v1",
                producer: "EOS.Orchestrator.Tests",
                payload: new Event2Payload("reacted"),
                correlationId: event1.CorrelationId,
                causationId: event1.EventId);

            mediator.Publish(event2);
        });

        mediator.Subscribe<Event2Payload>(event2 => capturedEvent2 = event2);

        var publishedEvent1 = EventEnvelope<Event1Payload>.Create(
            eventType: "Event1",
            version: "v1",
            producer: "EOS.Orchestrator.Tests",
            payload: new Event1Payload("hello"));

        mediator.Publish(publishedEvent1);

        Assert.NotNull(capturedEvent2);
        Assert.Equal(publishedEvent1.CorrelationId, capturedEvent2.CorrelationId);
        Assert.Equal(publishedEvent1.EventId, capturedEvent2.CausationId);
    }

    [Fact]
    public void Publish_WithNoSubscribers_DoesNotThrow()
    {
        var mediator = new EventMediator();

        var envelope = EventEnvelope<Event1Payload>.Create(
            eventType: "Event1",
            version: "v1",
            producer: "EOS.Orchestrator.Tests",
            payload: new Event1Payload("hello"));

        var exception = Record.Exception(() => mediator.Publish(envelope));

        Assert.Null(exception);
    }
}
