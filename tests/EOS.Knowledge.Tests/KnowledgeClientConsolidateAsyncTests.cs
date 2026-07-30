using EOS.KnowledgeGraph;
using EOS.VectorStore;

namespace EOS.Knowledge.Tests;

public class KnowledgeClientConsolidateAsyncTests
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
    public async Task ConsolidateAsync_CreatesRealEpisodicRow_IndexesARealEmbedding_AndEmitsLessonLearned()
    {
        var store = new KnowledgeGraphStore(ConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        var key = $"working:{Guid.NewGuid()}";
        var memorySourceStore = new InMemoryMemorySourceStore(key, "the reasoning engine explained SOLID principles");
        var publisher = new CapturingLessonLearnedEventPublisher();
        var client = new KnowledgeClient(
            store,
            DefaultRankingWeights,
            new ChromaVectorStore(ChromaDbEndpoint),
            memorySourceStore,
            embeddingGenerator: new FixedEmbeddingGenerator(),
            lessonLearnedEventPublisher: publisher);
        var source = new MemoryRef(MemoryType.Working, key);

        var episodicEntryId = await client.ConsolidateAsync(
            source, "this is worth remembering", ["artifact://evidence/1"], cancellationToken: CancellationToken.None);

        Assert.NotEqual(Guid.Empty, episodicEntryId);
        var persisted = await store.GetByIdAsync(episodicEntryId, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal(KnowledgeNodeType.Lesson, persisted.NodeType);
        Assert.Equal("the reasoning engine explained SOLID principles", persisted.Content);
        Assert.Equal(["artifact://evidence/1"], persisted.EvidenceRefs);
        Assert.Equal(episodicEntryId, publisher.LastEpisodicEntryId);
        Assert.Equal(key, publisher.LastSource);
    }

    [Fact]
    public async Task ConsolidateAsync_SuppressesLessonLearned_ForTheGateFailureTrigger()
    {
        var store = new KnowledgeGraphStore(ConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        var key = $"shortterm:{Guid.NewGuid()}";
        var memorySourceStore = new InMemoryMemorySourceStore(key, "a novel gate failure occurred");
        var publisher = new CapturingLessonLearnedEventPublisher();
        var client = new KnowledgeClient(
            store,
            DefaultRankingWeights,
            new ChromaVectorStore(ChromaDbEndpoint),
            memorySourceStore,
            lessonLearnedEventPublisher: publisher);
        var source = new MemoryRef(MemoryType.ShortTerm, key);

        var episodicEntryId = await client.ConsolidateAsync(
            source, "novel gate failure", [], suppressLessonLearned: true, cancellationToken: CancellationToken.None);

        Assert.NotEqual(Guid.Empty, episodicEntryId);
        Assert.Null(publisher.LastEpisodicEntryId);
    }

    [Fact]
    public async Task ConsolidateAsync_IsIdempotent_WhenSourceIsAlreadyConsolidated()
    {
        var store = new KnowledgeGraphStore(ConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        var key = $"session:{Guid.NewGuid()}";
        var memorySourceStore = new InMemoryMemorySourceStore(key, "session content");
        var client = new KnowledgeClient(
            store, DefaultRankingWeights, new ChromaVectorStore(ChromaDbEndpoint), memorySourceStore);
        var source = new MemoryRef(MemoryType.Session, key);

        var firstResult = await client.ConsolidateAsync(source, "reason", [], cancellationToken: CancellationToken.None);
        var secondResult = await client.ConsolidateAsync(source, "reason", [], cancellationToken: CancellationToken.None);

        Assert.NotEqual(Guid.Empty, firstResult);
        Assert.Equal(Guid.Empty, secondResult);
    }

    [Fact]
    public async Task ConsolidateAsync_Throws_WhenSourceDoesNotResolveToContent()
    {
        var store = new KnowledgeGraphStore(ConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        var memorySourceStore = new InMemoryMemorySourceStore("unrelated-key", "unrelated content");
        var client = new KnowledgeClient(
            store, DefaultRankingWeights, new ChromaVectorStore(ChromaDbEndpoint), memorySourceStore);
        var source = new MemoryRef(MemoryType.Working, $"missing:{Guid.NewGuid()}");

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.ConsolidateAsync(source, "reason", [], cancellationToken: CancellationToken.None));
    }

    private sealed class InMemoryMemorySourceStore(string key, string content) : IMemorySourceStore
    {
        private bool _consolidated;

        public Task<string?> GetContentAsync(MemoryRef source, CancellationToken cancellationToken = default) =>
            Task.FromResult(source.Key == key ? content : null);

        public Task<bool> IsConsolidatedAsync(MemoryRef source, CancellationToken cancellationToken = default) =>
            Task.FromResult(_consolidated);

        public Task MarkConsolidatedAsync(MemoryRef source, CancellationToken cancellationToken = default)
        {
            _consolidated = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedEmbeddingGenerator : IEmbeddingGenerator
    {
        public Task<IReadOnlyList<float>> EmbedAsync(string content, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<float>>([0.1f, 0.2f, 0.3f]);
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
