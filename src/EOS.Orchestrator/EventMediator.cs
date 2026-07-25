using EOS.Contracts;

namespace EOS.Orchestrator;

public sealed class EventMediator
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = [];

    public void Subscribe<TPayload>(Action<EventEnvelope<TPayload>> handler)
    {
        var key = typeof(TPayload);

        if (!_handlers.TryGetValue(key, out var handlersForType))
        {
            handlersForType = [];
            _handlers[key] = handlersForType;
        }

        handlersForType.Add(handler);
    }

    public void Publish<TPayload>(EventEnvelope<TPayload> envelope)
    {
        if (!_handlers.TryGetValue(typeof(TPayload), out var handlersForType))
        {
            return;
        }

        foreach (var handler in handlersForType.ToArray())
        {
            ((Action<EventEnvelope<TPayload>>)handler).Invoke(envelope);
        }
    }
}
