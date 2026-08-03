using EOS.KnowledgeGraph;

namespace EOS.Knowledge.Tests;

public class CompressionSweepTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("EOS_SQLSERVER_CONNECTION_STRING")
        ?? throw new InvalidOperationException("EOS_SQLSERVER_CONNECTION_STRING is not set.");

    [Fact]
    public async Task RunAsync_CompressesAnEligibleEntry_ArchivingTheOriginalContentWithoutDeletingIt()
    {
        var store = new KnowledgeGraphStore(ConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        var archivedContentStore = new ArchivedContentStore(ConnectionString);
        await archivedContentStore.EnsureTableExistsAsync(CancellationToken.None);

        var nodeId = Guid.NewGuid();
        var originalContent = $"a long, generalizable lesson worth compressing {Guid.NewGuid()}";
        await store.UpsertAsync(
            new KnowledgeNode(nodeId, KnowledgeNodeType.Lesson, originalContent, [], [], DateTimeOffset.UtcNow),
            CancellationToken.None);

        var summarizer = new FixedSummarizer("a short summary");
        var eventPublisher = new CapturingMemoryCompressedEventPublisher();
        var sweep = new CompressionSweep(
            store,
            archivedContentStore,
            new FixedPipelineStageStore(nodeId),
            new FixedReadRecencyTracker(readRecently: false),
            new FixedRetentionHoldPolicy(hasActiveHold: false),
            summarizer,
            new AlwaysGrantBackgroundSlotRequester(),
            eventPublisher);

        var compressedCount = await sweep.RunAsync(CancellationToken.None);

        var updatedNode = await store.GetByIdAsync(nodeId, CancellationToken.None);
        var archivedOriginal = await archivedContentStore.GetLatestArchivedContentBySourceNodeIdAsync(
            nodeId, CancellationToken.None);
        Assert.Equal(1, compressedCount);
        Assert.NotNull(updatedNode);
        Assert.Equal("a short summary", updatedNode.Content);
        Assert.Equal(originalContent, archivedOriginal);
        Assert.Contains(nodeId, eventPublisher.PublishedEntryIds);
        Assert.Equal((originalContent.Length, "a short summary".Length), eventPublisher.PublishedSizes[nodeId]);
    }

    [Fact]
    public async Task RunAsync_SkipsAnEntry_WhosePipelineRecordHasNotReachedPatternStage()
    {
        var store = new KnowledgeGraphStore(ConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        var archivedContentStore = new ArchivedContentStore(ConnectionString);
        await archivedContentStore.EnsureTableExistsAsync(CancellationToken.None);

        var nodeId = Guid.NewGuid();
        var originalContent = $"a lesson not yet promoted to pattern {Guid.NewGuid()}";
        await store.UpsertAsync(
            new KnowledgeNode(nodeId, KnowledgeNodeType.Lesson, originalContent, [], [], DateTimeOffset.UtcNow),
            CancellationToken.None);

        var eventPublisher = new CapturingMemoryCompressedEventPublisher();
        var sweep = new CompressionSweep(
            store,
            archivedContentStore,
            new FixedPipelineStageStore(targetEntryId: Guid.NewGuid()),
            new FixedReadRecencyTracker(readRecently: false),
            new FixedRetentionHoldPolicy(hasActiveHold: false),
            new FixedSummarizer("should never be called"),
            new AlwaysGrantBackgroundSlotRequester(),
            eventPublisher);

        await sweep.RunAsync(CancellationToken.None);

        var untouchedNode = await store.GetByIdAsync(nodeId, CancellationToken.None);
        Assert.NotNull(untouchedNode);
        Assert.Equal(originalContent, untouchedNode.Content);
        Assert.DoesNotContain(nodeId, eventPublisher.PublishedEntryIds);
    }

    [Fact]
    public async Task RunAsync_SkipsAnEntry_ThatWasReadRecently_EvenThoughItReachedPatternStage()
    {
        var store = new KnowledgeGraphStore(ConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        var archivedContentStore = new ArchivedContentStore(ConnectionString);
        await archivedContentStore.EnsureTableExistsAsync(CancellationToken.None);

        var nodeId = Guid.NewGuid();
        var originalContent = $"a recently-read lesson {Guid.NewGuid()}";
        await store.UpsertAsync(
            new KnowledgeNode(nodeId, KnowledgeNodeType.Lesson, originalContent, [], [], DateTimeOffset.UtcNow),
            CancellationToken.None);

        var eventPublisher = new CapturingMemoryCompressedEventPublisher();
        var sweep = new CompressionSweep(
            store,
            archivedContentStore,
            new FixedPipelineStageStore(nodeId),
            new FixedReadRecencyTracker(readRecently: true),
            new FixedRetentionHoldPolicy(hasActiveHold: false),
            new FixedSummarizer("should never be called"),
            new AlwaysGrantBackgroundSlotRequester(),
            eventPublisher);

        await sweep.RunAsync(CancellationToken.None);

        var untouchedNode = await store.GetByIdAsync(nodeId, CancellationToken.None);
        Assert.NotNull(untouchedNode);
        Assert.Equal(originalContent, untouchedNode.Content);
        Assert.DoesNotContain(nodeId, eventPublisher.PublishedEntryIds);
    }

    [Fact]
    public async Task RunAsync_SkipsAnEntry_UnderAnActiveRetentionHold_EvenThoughItReachedPatternStage()
    {
        var store = new KnowledgeGraphStore(ConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        var archivedContentStore = new ArchivedContentStore(ConnectionString);
        await archivedContentStore.EnsureTableExistsAsync(CancellationToken.None);

        var nodeId = Guid.NewGuid();
        var originalContent = $"a lesson under legal hold {Guid.NewGuid()}";
        await store.UpsertAsync(
            new KnowledgeNode(nodeId, KnowledgeNodeType.Lesson, originalContent, [], [], DateTimeOffset.UtcNow),
            CancellationToken.None);

        var eventPublisher = new CapturingMemoryCompressedEventPublisher();
        var sweep = new CompressionSweep(
            store,
            archivedContentStore,
            new FixedPipelineStageStore(nodeId),
            new FixedReadRecencyTracker(readRecently: false),
            new FixedRetentionHoldPolicy(hasActiveHold: true),
            new FixedSummarizer("should never be called"),
            new AlwaysGrantBackgroundSlotRequester(),
            eventPublisher);

        await sweep.RunAsync(CancellationToken.None);

        var untouchedNode = await store.GetByIdAsync(nodeId, CancellationToken.None);
        Assert.NotNull(untouchedNode);
        Assert.Equal(originalContent, untouchedNode.Content);
        Assert.DoesNotContain(nodeId, eventPublisher.PublishedEntryIds);
    }

    /// <summary>
    /// Reports "reached Pattern stage" for exactly one target entry id and "not yet promoted"
    /// for every other id — so these tests never mutate unrelated Lesson rows that happen to
    /// already exist in the shared, persistent dev SQL Server instance other tests also use.
    /// </summary>
    private sealed class FixedPipelineStageStore(Guid targetEntryId) : IPipelineStageStore
    {
        public Task<bool> HasReachedPatternStageOrBeyondAsync(
            Guid episodicEntryId, CancellationToken cancellationToken = default) =>
            Task.FromResult(episodicEntryId == targetEntryId);
    }

    private sealed class FixedReadRecencyTracker(bool readRecently) : IReadRecencyTracker
    {
        public Task<bool> WasReadRecentlyAsync(Guid episodicEntryId, CancellationToken cancellationToken = default) =>
            Task.FromResult(readRecently);
    }

    private sealed class FixedRetentionHoldPolicy(bool hasActiveHold) : IRetentionHoldPolicy
    {
        public Task<bool> HasActiveHoldAsync(Guid episodicEntryId, CancellationToken cancellationToken = default) =>
            Task.FromResult(hasActiveHold);
    }

    private sealed class FixedSummarizer(string summary) : ISummarizer
    {
        public Task<string> SummarizeAsync(string content, CancellationToken cancellationToken = default) =>
            Task.FromResult(summary);
    }

    private sealed class AlwaysGrantBackgroundSlotRequester : IBackgroundSlotRequester
    {
        public bool RequestSlot(string jobId, EOS.Contracts.ResourceClass resourceClass) => true;
    }

    private sealed class AlwaysDeferBackgroundSlotRequester : IBackgroundSlotRequester
    {
        public bool RequestSlot(string jobId, EOS.Contracts.ResourceClass resourceClass) => false;
    }

    // WP-022 CodeRabbit review Finding 2: the roadmap's own required deferral behavior
    // ("correctly deferred under simulated contention") had no test at CompressionSweep's own
    // integration point — only one layer down, in BackgroundTaskController/QuotaManager.
    [Fact]
    public async Task RunAsync_PerformsNoCompressionWork_WhenTheBackgroundSlotIsDeferred()
    {
        var store = new KnowledgeGraphStore(ConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        var archivedContentStore = new ArchivedContentStore(ConnectionString);
        await archivedContentStore.EnsureTableExistsAsync(CancellationToken.None);

        var nodeId = Guid.NewGuid();
        var originalContent = $"a lesson that would be eligible, if the slot were granted {Guid.NewGuid()}";
        await store.UpsertAsync(
            new KnowledgeNode(nodeId, KnowledgeNodeType.Lesson, originalContent, [], [], DateTimeOffset.UtcNow),
            CancellationToken.None);

        var eventPublisher = new CapturingMemoryCompressedEventPublisher();
        var sweep = new CompressionSweep(
            store,
            archivedContentStore,
            new FixedPipelineStageStore(nodeId),
            new FixedReadRecencyTracker(readRecently: false),
            new FixedRetentionHoldPolicy(hasActiveHold: false),
            new FixedSummarizer("should never be called"),
            new AlwaysDeferBackgroundSlotRequester(),
            eventPublisher);

        var compressedCount = await sweep.RunAsync(CancellationToken.None);

        var untouchedNode = await store.GetByIdAsync(nodeId, CancellationToken.None);
        var archivedContent = await archivedContentStore.GetLatestArchivedContentBySourceNodeIdAsync(nodeId, CancellationToken.None);
        Assert.Equal(0, compressedCount);
        Assert.NotNull(untouchedNode);
        Assert.Equal(originalContent, untouchedNode.Content);
        Assert.Null(archivedContent);
        Assert.DoesNotContain(nodeId, eventPublisher.PublishedEntryIds);
    }

    private sealed class CapturingMemoryCompressedEventPublisher : IMemoryCompressedEventPublisher
    {
        public List<Guid> PublishedEntryIds { get; } = [];

        public Dictionary<Guid, (int OriginalSize, int SummarySize)> PublishedSizes { get; } = [];

        public void PublishMemoryCompressed(Guid entryId, int originalSize, int summarySize)
        {
            PublishedEntryIds.Add(entryId);
            PublishedSizes[entryId] = (originalSize, summarySize);
        }
    }
}
