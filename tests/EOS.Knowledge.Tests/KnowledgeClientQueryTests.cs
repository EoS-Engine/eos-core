using EOS.KnowledgeGraph;

namespace EOS.Knowledge.Tests;

public class KnowledgeClientQueryTests
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

    [Fact]
    public async Task QueryAsync_ReturnsOnlyLessonNodes_ForEpisodicMemoryType()
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

        var results = (await client.QueryAsync(MemoryType.Episodic, null, null, CancellationToken.None)).ToList();

        Assert.Contains(results, node => node.NodeId == lessonId);
        Assert.DoesNotContain(results, node => node.NodeId == factId);
    }

    [Fact]
    public async Task QueryAsync_ReturnsFactAndPatternNodes_ForSemanticMemoryType()
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

        var results = (await client.QueryAsync(MemoryType.Semantic, null, null, CancellationToken.None)).ToList();

        Assert.Contains(results, node => node.NodeId == factId);
        Assert.Contains(results, node => node.NodeId == patternId);
        Assert.DoesNotContain(results, node => node.NodeId == lessonId);
    }

    [Fact]
    public async Task QueryAsync_ExcludesLessonNodes_ForLongTermMemoryType()
    {
        var (store, client) = await CreateClientAsync();
        var riskId = Guid.NewGuid();
        var lessonId = Guid.NewGuid();
        await store.UpsertAsync(
            new KnowledgeNode(riskId, KnowledgeNodeType.Risk, "a risk", [], [], DateTimeOffset.UtcNow),
            CancellationToken.None);
        await store.UpsertAsync(
            new KnowledgeNode(lessonId, KnowledgeNodeType.Lesson, "a lesson", [], [], DateTimeOffset.UtcNow),
            CancellationToken.None);

        var results = (await client.QueryAsync(MemoryType.LongTerm, null, null, CancellationToken.None)).ToList();

        Assert.Contains(results, node => node.NodeId == riskId);
        Assert.DoesNotContain(results, node => node.NodeId == lessonId);
    }

    [Fact]
    public async Task QueryAsync_FiltersByDomainTags_ForProjectMemoryType()
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

        var results = (await client.QueryAsync(MemoryType.Project, ["backend"], null, CancellationToken.None)).ToList();

        Assert.Contains(results, node => node.NodeId == matchingId);
        Assert.DoesNotContain(results, node => node.NodeId == nonMatchingId);
    }

    [Fact]
    public async Task QueryAsync_ThrowsArgumentException_ForProjectMemoryType_WhenDomainTagsAreNull()
    {
        var (_, client) = await CreateClientAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.QueryAsync(MemoryType.Project, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task QueryAsync_ThrowsArgumentException_ForProjectMemoryType_WhenDomainTagsAreEmpty()
    {
        var (_, client) = await CreateClientAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.QueryAsync(MemoryType.Project, [], null, CancellationToken.None));
    }

    [Theory]
    [InlineData(MemoryType.Working)]
    [InlineData(MemoryType.ShortTerm)]
    [InlineData(MemoryType.Session)]
    public async Task QueryAsync_ThrowsNotSupported_ForEphemeralMemoryTypes(MemoryType type)
    {
        var (_, client) = await CreateClientAsync();

        await Assert.ThrowsAsync<NotSupportedException>(
            () => client.QueryAsync(type, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task QuerySimilarAsync_ThrowsArgumentException_WhenRefDoesNotResolve()
    {
        var (_, client) = await CreateClientAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.QuerySimilarAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task QuerySimilarAsync_ReturnsOtherNodesOfTheSameNodeType_ExcludingItself()
    {
        var (store, client) = await CreateClientAsync();
        var nodeId = Guid.NewGuid();
        var sameTypeId = Guid.NewGuid();
        var differentTypeId = Guid.NewGuid();
        await store.UpsertAsync(
            new KnowledgeNode(nodeId, KnowledgeNodeType.Fact, "the querying node", [], [], DateTimeOffset.UtcNow),
            CancellationToken.None);
        await store.UpsertAsync(
            new KnowledgeNode(sameTypeId, KnowledgeNodeType.Fact, "a candidate", [], [], DateTimeOffset.UtcNow),
            CancellationToken.None);
        await store.UpsertAsync(
            new KnowledgeNode(differentTypeId, KnowledgeNodeType.Lesson, "not a candidate", [], [], DateTimeOffset.UtcNow),
            CancellationToken.None);

        var results = (await client.QuerySimilarAsync(nodeId, CancellationToken.None)).ToList();

        Assert.Contains(results, node => node.NodeId == sameTypeId);
        Assert.DoesNotContain(results, node => node.NodeId == nodeId);
        Assert.DoesNotContain(results, node => node.NodeId == differentTypeId);
    }
}
