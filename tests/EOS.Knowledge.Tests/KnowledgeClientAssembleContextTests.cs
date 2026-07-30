using EOS.KnowledgeGraph;

namespace EOS.Knowledge.Tests;

public class KnowledgeClientAssembleContextTests
{
    private static readonly RankingWeights DefaultRankingWeights = new(
        VectorSimilarity: 0.4, Recency: 0.3, DomainMatch: 0.2, AccessFrequency: 0.1);

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("EOS_SQLSERVER_CONNECTION_STRING")
        ?? throw new InvalidOperationException("EOS_SQLSERVER_CONNECTION_STRING is not set.");

    private static async Task<(KnowledgeGraphStore Store, IKnowledgeClient Client)> CreateClientAsync()
    {
        var store = new KnowledgeGraphStore(ConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        return (store, new KnowledgeClient(store, DefaultRankingWeights));
    }

    private static ContextRequest CreateRequest(
        int tokenOrSizeBudget = 10_000,
        bool includesEpisodic = true,
        bool includesSemantic = true,
        string[]? projectScope = null,
        DateRange? filters = null)
    {
        return new ContextRequest(
            TokenOrSizeBudget: tokenOrSizeBudget,
            IncludesWorking: false,
            IncludesShortTerm: false,
            IncludesEpisodic: includesEpisodic,
            IncludesSemantic: includesSemantic,
            ProjectScope: projectScope,
            Filters: filters,
            TaskId: null);
    }

    [Fact]
    public async Task AssembleContextAsync_ReturnsEmptyPayload_WhenNoMemoryTypeIsIncluded()
    {
        var (_, client) = await CreateClientAsync();
        var request = CreateRequest(includesEpisodic: false, includesSemantic: false);

        var payload = await client.AssembleContextAsync(request, CancellationToken.None);

        Assert.Empty(payload.Items);
        Assert.False(payload.Truncated);
    }

    [Fact]
    public async Task AssembleContextAsync_ReturnsLessonNodes_WhenIncludesEpisodicIsTrue()
    {
        var (store, client) = await CreateClientAsync();
        var lessonId = Guid.NewGuid();
        var factId = Guid.NewGuid();
        await store.UpsertAsync(
            new KnowledgeNode(lessonId, KnowledgeNodeType.Lesson, "a lesson", [], [], DateTimeOffset.UtcNow),
            CancellationToken.None);
        await store.UpsertAsync(
            new KnowledgeNode(factId, KnowledgeNodeType.Fact, "a fact", [], [], DateTimeOffset.UtcNow),
            CancellationToken.None);
        var request = CreateRequest(includesEpisodic: true, includesSemantic: false);

        var payload = await client.AssembleContextAsync(request, CancellationToken.None);

        Assert.Contains(payload.Items, node => node.NodeId == lessonId);
        Assert.DoesNotContain(payload.Items, node => node.NodeId == factId);
    }

    [Fact]
    public async Task AssembleContextAsync_ReturnsFactAndPatternNodes_WhenIncludesSemanticIsTrue()
    {
        var (store, client) = await CreateClientAsync();
        var factId = Guid.NewGuid();
        var patternId = Guid.NewGuid();
        var lessonId = Guid.NewGuid();
        await store.UpsertAsync(
            new KnowledgeNode(factId, KnowledgeNodeType.Fact, "a fact", [], [], DateTimeOffset.UtcNow),
            CancellationToken.None);
        await store.UpsertAsync(
            new KnowledgeNode(patternId, KnowledgeNodeType.Pattern, "a pattern", [], [], DateTimeOffset.UtcNow),
            CancellationToken.None);
        await store.UpsertAsync(
            new KnowledgeNode(lessonId, KnowledgeNodeType.Lesson, "a lesson", [], [], DateTimeOffset.UtcNow),
            CancellationToken.None);
        var request = CreateRequest(includesEpisodic: false, includesSemantic: true);

        var payload = await client.AssembleContextAsync(request, CancellationToken.None);

        Assert.Contains(payload.Items, node => node.NodeId == factId);
        Assert.Contains(payload.Items, node => node.NodeId == patternId);
        Assert.DoesNotContain(payload.Items, node => node.NodeId == lessonId);
    }

    [Fact]
    public async Task AssembleContextAsync_FiltersByProjectScope()
    {
        var (store, client) = await CreateClientAsync();
        var matchingId = Guid.NewGuid();
        var nonMatchingId = Guid.NewGuid();
        await store.UpsertAsync(
            new KnowledgeNode(matchingId, KnowledgeNodeType.Fact, "matching", ["backend"], [], DateTimeOffset.UtcNow),
            CancellationToken.None);
        await store.UpsertAsync(
            new KnowledgeNode(nonMatchingId, KnowledgeNodeType.Fact, "non-matching", ["mobile"], [], DateTimeOffset.UtcNow),
            CancellationToken.None);
        var request = CreateRequest(includesEpisodic: false, includesSemantic: true, projectScope: ["backend"]);

        var payload = await client.AssembleContextAsync(request, CancellationToken.None);

        Assert.Contains(payload.Items, node => node.NodeId == matchingId);
        Assert.DoesNotContain(payload.Items, node => node.NodeId == nonMatchingId);
    }

    [Fact]
    public async Task AssembleContextAsync_FiltersByDateRange()
    {
        var (store, client) = await CreateClientAsync();
        var oldId = Guid.NewGuid();
        var newId = Guid.NewGuid();
        await store.UpsertAsync(
            new KnowledgeNode(oldId, KnowledgeNodeType.Fact, "old", [], [], DateTimeOffset.UtcNow.AddDays(-30)),
            CancellationToken.None);
        await store.UpsertAsync(
            new KnowledgeNode(newId, KnowledgeNodeType.Fact, "new", [], [], DateTimeOffset.UtcNow),
            CancellationToken.None);
        var request = CreateRequest(
            includesEpisodic: false,
            includesSemantic: true,
            filters: new DateRange(From: DateTimeOffset.UtcNow.AddDays(-1), To: null));

        var payload = await client.AssembleContextAsync(request, CancellationToken.None);

        Assert.Contains(payload.Items, node => node.NodeId == newId);
        Assert.DoesNotContain(payload.Items, node => node.NodeId == oldId);
    }

    [Fact]
    public async Task AssembleContextAsync_TruncatesToBudget_AndFlagsTruncation()
    {
        var (store, client) = await CreateClientAsync();
        var isolationTag = $"test-scope-{Guid.NewGuid()}";
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        await store.UpsertAsync(
            new KnowledgeNode(firstId, KnowledgeNodeType.Fact, new string('a', 50), [isolationTag], [], DateTimeOffset.UtcNow),
            CancellationToken.None);
        await store.UpsertAsync(
            new KnowledgeNode(secondId, KnowledgeNodeType.Fact, new string('b', 50), [isolationTag], [], DateTimeOffset.UtcNow),
            CancellationToken.None);
        var request = CreateRequest(
            tokenOrSizeBudget: 60, includesEpisodic: false, includesSemantic: true, projectScope: [isolationTag]);

        var payload = await client.AssembleContextAsync(request, CancellationToken.None);

        Assert.Single(payload.Items);
        Assert.True(payload.Truncated);
    }

    [Fact]
    public async Task AssembleContextAsync_ReturnsEmptyPayload_WhenBudgetIsTooSmallForAnyItem()
    {
        var (store, client) = await CreateClientAsync();
        var isolationTag = $"test-scope-{Guid.NewGuid()}";
        await store.UpsertAsync(
            new KnowledgeNode(Guid.NewGuid(), KnowledgeNodeType.Fact, new string('a', 50), [isolationTag], [], DateTimeOffset.UtcNow),
            CancellationToken.None);
        var request = CreateRequest(
            tokenOrSizeBudget: 1, includesEpisodic: false, includesSemantic: true, projectScope: [isolationTag]);

        var payload = await client.AssembleContextAsync(request, CancellationToken.None);

        Assert.Empty(payload.Items);
        Assert.True(payload.Truncated);
    }

    [Fact]
    public async Task AssembleContextAsync_PublishesContextAssembled_WithMatchingPayload()
    {
        var store = new KnowledgeGraphStore(ConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        var isolationTag = $"test-scope-{Guid.NewGuid()}";
        await store.UpsertAsync(
            new KnowledgeNode(Guid.NewGuid(), KnowledgeNodeType.Fact, "content", [isolationTag], [], DateTimeOffset.UtcNow),
            CancellationToken.None);
        var publisher = new CapturingContextAssemblyEventPublisher();
        var client = new KnowledgeClient(store, DefaultRankingWeights, publisher);
        var request = CreateRequest(includesEpisodic: false, includesSemantic: true, projectScope: [isolationTag]);

        var payload = await client.AssembleContextAsync(request, CancellationToken.None);

        Assert.NotNull(publisher.LastRequestId);
        Assert.Equal(payload.Items.Count, publisher.LastItemCount);
        Assert.Equal(payload.Truncated, publisher.LastTruncated);
    }

    [Fact]
    public async Task AssembleContextAsync_DoesNotThrow_WhenNoPublisherIsSupplied()
    {
        var (_, client) = await CreateClientAsync();
        var request = CreateRequest(includesEpisodic: false, includesSemantic: false);

        var payload = await client.AssembleContextAsync(request, CancellationToken.None);

        Assert.Empty(payload.Items);
    }

    private sealed class CapturingContextAssemblyEventPublisher : IContextAssemblyEventPublisher
    {
        public Guid? LastRequestId { get; private set; }

        public int? LastItemCount { get; private set; }

        public bool? LastTruncated { get; private set; }

        public void PublishContextAssembled(Guid requestId, int itemCount, bool truncated)
        {
            LastRequestId = requestId;
            LastItemCount = itemCount;
            LastTruncated = truncated;
        }
    }
}
