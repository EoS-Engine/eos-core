using EOS.Contracts;
using EOS.Knowledge;
using EOS.KnowledgeGraph;
using EOS.Orchestrator;
using EOS.VectorStore;

namespace EOS.Runner.Tests;

public class AutomaticConsolidationTriggerIntegrationTests
{
    private static readonly RankingWeights DefaultRankingWeights = new(
        VectorSimilarity: 0.4, Recency: 0.3, DomainMatch: 0.2, AccessFrequency: 0.1);

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("EOS_SQLSERVER_CONNECTION_STRING")
        ?? throw new InvalidOperationException("EOS_SQLSERVER_CONNECTION_STRING is not set.");

    private static string ChromaDbEndpoint =>
        Environment.GetEnvironmentVariable("EOS_CHROMADB_ENDPOINT")
        ?? throw new InvalidOperationException("EOS_CHROMADB_ENDPOINT is not set.");

    [Fact]
    public async Task RealEventMediatorPublish_ThroughTheRealProgramSubscription_TriggersConsolidateAsync_AndSuppressesLessonLearned()
    {
        var store = new KnowledgeGraphStore(ConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        var key = $"shortterm:{Guid.NewGuid()}";
        var content = $"a novel gate failure occurred {Guid.NewGuid()}";
        var memorySourceStore = new InMemoryMemorySourceStore(key, content);
        var lessonLearnedPublisher = new CapturingLessonLearnedEventPublisher();
        var client = new KnowledgeClient(
            store,
            DefaultRankingWeights,
            new ChromaVectorStore(ChromaDbEndpoint),
            memorySourceStore,
            lessonLearnedEventPublisher: lessonLearnedPublisher);
        var eventMediator = new EventMediator();

        // Calls the exact same registration Program.cs performs - not a copy of it. A regression
        // in either the handler bodies or which handler is wired to which signal type would be
        // caught by this test.
        AutomaticConsolidationTriggerHandlers.RegisterSubscriptions(eventMediator, client);

        eventMediator.Publish(EventEnvelope<GateFailureConsolidationSignal>.Create(
            eventType: "GateFailureConsolidationSignal",
            version: "v1",
            producer: "EOS.Gates",
            payload: new GateFailureConsolidationSignal(MemoryType.ShortTerm, key, "novel gate failure", [])));

        Assert.True(memorySourceStore.WasMarkedConsolidated);
        var persisted = await store.QueryAsync([KnowledgeNodeType.Lesson], null, null, CancellationToken.None);
        Assert.Contains(persisted, node => node.Content == content);
        Assert.Null(lessonLearnedPublisher.LastEpisodicEntryId);
    }

    [Fact]
    public async Task RealEventMediatorPublish_ThroughTheRealProgramSubscription_TriggersConsolidateAsync_AndEmitsLessonLearned_ForIncidentResolved()
    {
        var store = new KnowledgeGraphStore(ConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        var key = $"session:{Guid.NewGuid()}";
        var content = $"an incident was resolved {Guid.NewGuid()}";
        var memorySourceStore = new InMemoryMemorySourceStore(key, content);
        var lessonLearnedPublisher = new CapturingLessonLearnedEventPublisher();
        var client = new KnowledgeClient(
            store,
            DefaultRankingWeights,
            new ChromaVectorStore(ChromaDbEndpoint),
            memorySourceStore,
            lessonLearnedEventPublisher: lessonLearnedPublisher);
        var eventMediator = new EventMediator();

        AutomaticConsolidationTriggerHandlers.RegisterSubscriptions(eventMediator, client);

        eventMediator.Publish(EventEnvelope<IncidentResolvedConsolidationSignal>.Create(
            eventType: "IncidentResolvedConsolidationSignal",
            version: "v1",
            producer: "EOS.DevOps",
            payload: new IncidentResolvedConsolidationSignal(MemoryType.Session, key, "incident resolved", [])));

        Assert.True(memorySourceStore.WasMarkedConsolidated);
        var persisted = await store.QueryAsync([KnowledgeNodeType.Lesson], null, null, CancellationToken.None);
        Assert.Contains(persisted, node => node.Content == content);
        Assert.NotNull(lessonLearnedPublisher.LastEpisodicEntryId);
        Assert.Equal(key, lessonLearnedPublisher.LastSource);
    }

    private sealed class InMemoryMemorySourceStore(string key, string content) : IMemorySourceStore
    {
        public bool WasMarkedConsolidated { get; private set; }

        public Task<string?> GetContentAsync(MemoryRef source, CancellationToken cancellationToken = default) =>
            Task.FromResult(source.Key == key ? content : null);

        public Task<bool> IsConsolidatedAsync(MemoryRef source, CancellationToken cancellationToken = default) =>
            Task.FromResult(WasMarkedConsolidated);

        public Task MarkConsolidatedAsync(MemoryRef source, CancellationToken cancellationToken = default)
        {
            WasMarkedConsolidated = true;
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingLessonLearnedEventPublisher : ILessonLearnedEventPublisher
    {
        public Guid? LastEpisodicEntryId { get; private set; }

        public string? LastSource { get; private set; }

        public void PublishLessonLearned(Guid episodicEntryId, string source)
        {
            LastEpisodicEntryId = episodicEntryId;
            LastSource = source;
        }
    }
}
