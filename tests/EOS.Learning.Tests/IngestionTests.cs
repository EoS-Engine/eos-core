using EOS.Contracts;

namespace EOS.Learning.Tests;

public class IngestionTests
{
    private static (Ingestion Ingestion, IPipelineRecordStore Store, FixedKnowledgeClient KnowledgeClient) CreateIngestion(
        IIngestionRateGuardStore? rateGuardStore = null,
        int windowSeconds = 3600,
        int thresholdCount = 100,
        IPipelineRecordStore? store = null,
        ILessonQuarantinedEventPublisher? quarantinedPublisher = null,
        Guid? episodicEntryId = null)
    {
        var pipelineRecordStore = store ?? new InMemoryPipelineRecordStore();
        var knowledgeClient = new FixedKnowledgeClient();
        if (episodicEntryId is { } id)
        {
            knowledgeClient.EpisodicNodes = [TestRecords.Node(id, ["backend"])];
        }

        var reasoningEngineClient = new FixedReasoningEngineClient
        {
            TrustSignalResult = new TrustSignal("test-source", 0.8, "test"),
            CompareResult = (_, candidates) => new ConfidenceGuardResult(0.0, [], candidates.Select(c => new RejectedMatch(c, "no signal")).ToArray()),
        };
        var clusterTrigger = new ClusterTrigger(
            knowledgeClient, reasoningEngineClient, pipelineRecordStore, new ConfidenceGuard(),
            new NeverCalledLessonPromotedEventPublisher(), 0.5);
        var rateGuard = new IngestionRateGuard(rateGuardStore ?? new InMemoryIngestionRateGuardStore(), windowSeconds, thresholdCount);
        var ingestion = new Ingestion(
            knowledgeClient, reasoningEngineClient, pipelineRecordStore, rateGuard, clusterTrigger,
            quarantinedPublisher ?? new RecordingLessonQuarantinedEventPublisher());

        return (ingestion, pipelineRecordStore, knowledgeClient);
    }

    [Fact]
    public async Task OnLessonLearnedAsync_CreatesAnActivePipelineRecord_ForAValidLesson()
    {
        var episodicEntryId = Guid.NewGuid();
        var (ingestion, store, _) = CreateIngestion(episodicEntryId: episodicEntryId);

        await ingestion.OnLessonLearnedAsync(episodicEntryId, "test-source", CancellationToken.None);

        var record = await store.GetBySourceLessonIdAsync(episodicEntryId, CancellationToken.None);
        Assert.NotNull(record);
        Assert.Equal(PipelineStage.Lesson, record.Stage);
        Assert.Equal(PipelineRecordStatus.Active, record.Status);
        Assert.Equal(episodicEntryId, record.KnowledgeGraphRef);
        Assert.Equal([episodicEntryId], record.SourceLessonIds);
        Assert.Equal(["backend"], record.DomainTags);
        Assert.Equal(0.8, record.TrustScore, precision: 10);
    }

    [Fact]
    public async Task OnLessonLearnedAsync_IsANoOp_ForADuplicateEpisodicEntryId()
    {
        var episodicEntryId = Guid.NewGuid();
        var (ingestion, store, _) = CreateIngestion(episodicEntryId: episodicEntryId);

        await ingestion.OnLessonLearnedAsync(episodicEntryId, "test-source", CancellationToken.None);
        await ingestion.OnLessonLearnedAsync(episodicEntryId, "test-source", CancellationToken.None);

        Assert.Equal(1, ((InMemoryPipelineRecordStore)store).InsertCallCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task OnLessonLearnedAsync_ThrowsArgumentException_ForAnEmptyOrWhitespaceSource(string source)
    {
        var (ingestion, _, _) = CreateIngestion();

        await Assert.ThrowsAsync<ArgumentException>(
            () => ingestion.OnLessonLearnedAsync(Guid.NewGuid(), source, CancellationToken.None));
    }

    [Fact]
    public async Task OnLessonLearnedAsync_Quarantines_WhenTheIngestionRateGuardTrips()
    {
        var episodicEntryId = Guid.NewGuid();
        var quarantinedPublisher = new RecordingLessonQuarantinedEventPublisher();
        var (ingestion, store, _) = CreateIngestion(
            rateGuardStore: new FixedCountIngestionRateGuardStore(101), thresholdCount: 100,
            quarantinedPublisher: quarantinedPublisher, episodicEntryId: episodicEntryId);

        await ingestion.OnLessonLearnedAsync(episodicEntryId, "test-source", CancellationToken.None);

        var record = await store.GetBySourceLessonIdAsync(episodicEntryId, CancellationToken.None);
        Assert.NotNull(record);
        Assert.Equal(PipelineRecordStatus.Quarantined, record.Status);
        Assert.Equal(1, quarantinedPublisher.CallCount);
        Assert.Equal(record.RecordId, quarantinedPublisher.LastRecordId);
        Assert.Equal("ingestion rate anomaly", quarantinedPublisher.LastReason);
    }

    [Fact]
    public async Task OnLessonLearnedAsync_PropagatesPersistenceFailures_AndPublishesNoEvent()
    {
        var quarantinedPublisher = new NeverCalledLessonQuarantinedEventPublisher();
        var (ingestion, _, _) = CreateIngestion(
            store: new ThrowingOnInsertPipelineRecordStore(), rateGuardStore: new FixedCountIngestionRateGuardStore(101),
            thresholdCount: 100, quarantinedPublisher: quarantinedPublisher);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ingestion.OnLessonLearnedAsync(Guid.NewGuid(), "test-source", CancellationToken.None));
    }
}
