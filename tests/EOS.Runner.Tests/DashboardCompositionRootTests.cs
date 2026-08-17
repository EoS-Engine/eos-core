using EOS.Contracts;
using EOS.Infrastructure;
using EOS.Orchestrator;
using EOS.SharedKernel.Configuration;

namespace EOS.Runner.Tests;

public class DashboardCompositionRootTests
{
    private static readonly DataStoreConnectionOptions ConnectionOptions = DataStoreConnectionOptions.FromEnvironment();

    private sealed record SamplePayload(Guid Id, string Name);

    // --- StoredEventMapper: EventEnvelope<TPayload> -> StoredEvent (items 2, 3) ---

    [Fact]
    public void ToStoredEvent_MapsEveryEnvelopeMetadataFieldVerbatim()
    {
        var envelope = EventEnvelope<SamplePayload>.Create(
            eventType: "SampleEvent",
            version: "v1",
            producer: "EOS.Runner.Tests",
            payload: new SamplePayload(Guid.NewGuid(), "test"),
            correlationId: Guid.NewGuid(),
            causationId: Guid.NewGuid());

        var storedEvent = StoredEventMapper.ToStoredEvent(envelope);

        Assert.Equal(envelope.EventId, storedEvent.EventId);
        Assert.Equal(envelope.EventType, storedEvent.EventType);
        Assert.Equal(envelope.Version, storedEvent.Version);
        Assert.Equal(envelope.Producer, storedEvent.Producer);
        Assert.Equal(envelope.CorrelationId, storedEvent.CorrelationId);
        Assert.Equal(envelope.CausationId, storedEvent.CausationId);
        Assert.Equal(envelope.OccurredAt, storedEvent.OccurredAt);
    }

    [Fact]
    public void ToStoredEvent_SerializesOnlyThePayloadAsPayloadJson()
    {
        var payload = new SamplePayload(Guid.NewGuid(), "widget");
        var envelope = EventEnvelope<SamplePayload>.Create(
            eventType: "SampleEvent", version: "v1", producer: "EOS.Runner.Tests", payload: payload);

        var storedEvent = StoredEventMapper.ToStoredEvent(envelope);
        var roundTripped = System.Text.Json.JsonSerializer.Deserialize<SamplePayload>(storedEvent.PayloadJson);

        Assert.Equal(payload, roundTripped);
    }

    [Fact]
    public void ToStoredEvent_PreservesNullCausationId()
    {
        var envelope = EventEnvelope<SamplePayload>.Create(
            eventType: "SampleEvent", version: "v1", producer: "EOS.Runner.Tests", payload: new SamplePayload(Guid.NewGuid(), "x"));

        var storedEvent = StoredEventMapper.ToStoredEvent(envelope);

        Assert.Null(storedEvent.CausationId);
    }

    // --- Event subscription -> SqlEventStore persistence, approved-type-only (items 1, 4) ---

    [Fact]
    public async Task EventMediatorSubscription_ForAnApprovedType_PersistsTheMappedStoredEvent()
    {
        var sqlEventStore = new SqlEventStore(ConnectionOptions.SqlServerConnectionString);
        await sqlEventStore.EnsureTableExistsAsync(CancellationToken.None);

        var eventMediator = new EventMediator();
        eventMediator.Subscribe<SamplePayload>(envelope =>
            sqlEventStore.AppendAsync(StoredEventMapper.ToStoredEvent(envelope), CancellationToken.None).GetAwaiter().GetResult());

        var payload = new SamplePayload(Guid.NewGuid(), "approved");
        var envelope = EventEnvelope<SamplePayload>.Create(
            eventType: "SampleEvent", version: "v1", producer: "EOS.Runner.Tests", payload: payload);

        eventMediator.Publish(envelope);

        var persisted = await sqlEventStore.ReadByIdAsync(envelope.EventId, CancellationToken.None);

        Assert.NotNull(persisted);
        Assert.Equal(envelope.EventType, persisted.EventType);
        Assert.Equal(envelope.EventId, persisted.EventId);
    }

    [Fact]
    public async Task EventMediatorSubscription_ForAnUnapprovedType_DoesNotReachSqlEventStore()
    {
        var sqlEventStore = new SqlEventStore(ConnectionOptions.SqlServerConnectionString);
        await sqlEventStore.EnsureTableExistsAsync(CancellationToken.None);

        // No subscription is registered for SamplePayload here at all — reproducing WP-030's
        // actual approved-list selectivity: EventMediator.Publish only reaches SqlEventStore for
        // the 7 payload types Program.cs explicitly subscribes for persistence.
        var eventMediator = new EventMediator();

        var envelope = EventEnvelope<SamplePayload>.Create(
            eventType: "SampleEvent", version: "v1", producer: "EOS.Runner.Tests", payload: new SamplePayload(Guid.NewGuid(), "unapproved"));

        eventMediator.Publish(envelope);

        var persisted = await sqlEventStore.ReadByIdAsync(envelope.EventId, CancellationToken.None);

        Assert.Null(persisted);
    }

    // --- Adapter delegation (items 5, 6, 7, 9) ---

    private sealed class FixedLoopControlClient(LoopStatus status) : ILoopControlClient
    {
        public CancellationToken? LastCancellationToken { get; private set; }

        public Task<LoopStatus> GetCurrentStatusAsync(CancellationToken cancellationToken = default)
        {
            LastCancellationToken = cancellationToken;
            return Task.FromResult(status);
        }

        public Task<ValidationResult> SetOperationalModeAsync(OperationalMode mode, string requestedBy, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by ILoopStatusQueryClient.");

        public Task<ValidationResult> EmergencyStopAsync(string requestedBy, string reason, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by ILoopStatusQueryClient.");
    }

    [Fact]
    public async Task LoopControllerLoopStatusQueryClient_DelegatesToTheUnderlyingLoopControlClient()
    {
        var expected = new LoopStatus(Guid.NewGuid(), OperationalMode.Assisted, null);
        var inner = new FixedLoopControlClient(expected);
        ILoopStatusQueryClient adapter = new LoopControllerLoopStatusQueryClient(inner);

        using var cts = new CancellationTokenSource();
        var actual = await adapter.GetCurrentStatusAsync(cts.Token);

        Assert.Same(expected, actual);
        Assert.Equal(cts.Token, inner.LastCancellationToken);
    }

    [Fact]
    public async Task DispatchedTaskStoreTaskStatusQueryClient_GetByStateAsync_DelegatesToTheStore()
    {
        var store = new DispatchedTaskStore(ConnectionOptions.SqlServerConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);

        var task = new DispatchedTask(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "WP030-04 adapter test task", [], [], 1,
            TaskLifecycleState.Ready, SchedulingMode.Immediate, null, null, false, 0, null);
        await store.UpsertAsync(task, CancellationToken.None);

        ITaskStatusQueryClient adapter = new DispatchedTaskStoreTaskStatusQueryClient(store);
        var results = await adapter.GetByStateAsync(TaskLifecycleState.Ready, CancellationToken.None);

        Assert.Contains(results, t => t.TaskId == task.TaskId);
    }

    [Fact]
    public async Task DispatchedTaskStoreTaskStatusQueryClient_CountByStateAsync_DelegatesToTheStore()
    {
        var store = new DispatchedTaskStore(ConnectionOptions.SqlServerConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);

        var task = new DispatchedTask(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "WP030-04 adapter count test task", [], [], 1,
            TaskLifecycleState.Blocked, SchedulingMode.Immediate, null, null, false, 0, "test");
        await store.UpsertAsync(task, CancellationToken.None);

        ITaskStatusQueryClient adapter = new DispatchedTaskStoreTaskStatusQueryClient(store);
        var directCount = await store.CountByStateAsync(TaskLifecycleState.Blocked, CancellationToken.None);
        var adapterCount = await adapter.CountByStateAsync(TaskLifecycleState.Blocked, CancellationToken.None);

        Assert.Equal(directCount, adapterCount);
        Assert.True(adapterCount >= 1);
    }

    [Fact]
    public async Task SqlEventStoreRecentEventsQueryClient_MapsStoredEventToRecentEventSummary_WithOnlyTheApprovedFields()
    {
        var store = new SqlEventStore(ConnectionOptions.SqlServerConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);

        var storedEvent = new StoredEvent(
            EventId: Guid.NewGuid(),
            EventType: "SampleEvent",
            Version: "v1",
            Producer: "EOS.Runner.Tests",
            CorrelationId: Guid.NewGuid(),
            CausationId: Guid.NewGuid(),
            OccurredAt: DateTimeOffset.UtcNow,
            PayloadJson: """{"marker":"recent-events-adapter-test"}""");
        await store.AppendAsync(storedEvent, CancellationToken.None);

        IRecentEventsQueryClient adapter = new SqlEventStoreRecentEventsQueryClient(store);
        var recent = await adapter.GetRecentAsync(2000, CancellationToken.None);
        var mapped = Assert.Single(recent, e => e.EventId == storedEvent.EventId);

        Assert.Equal(storedEvent.EventId, mapped.EventId);
        Assert.Equal(storedEvent.EventType, mapped.EventType);
        Assert.Equal(storedEvent.Producer, mapped.Producer);
        Assert.Equal(storedEvent.OccurredAt, mapped.OccurredAt);
        Assert.Equal(storedEvent.PayloadJson, mapped.PayloadJson);
    }
}
