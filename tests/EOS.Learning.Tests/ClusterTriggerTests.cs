using EOS.Contracts;

namespace EOS.Learning.Tests;

public class ClusterTriggerTests
{
    private static ClusterTrigger CreateTrigger(
        InMemoryPipelineRecordStore store,
        FixedKnowledgeClient knowledgeClient,
        FixedReasoningEngineClient reasoningEngineClient,
        RecordingLessonPromotedEventPublisher? publisher = null,
        double clusteringConfidenceMinimum = 0.5)
    {
        return new ClusterTrigger(
            knowledgeClient, reasoningEngineClient, store, new ConfidenceGuard(),
            publisher ?? new RecordingLessonPromotedEventPublisher(), clusteringConfidenceMinimum);
    }

    [Fact]
    public async Task EvaluateAsync_DoesNotPromote_WhenThereAreZeroCandidates()
    {
        var store = new InMemoryPipelineRecordStore();
        var record = TestRecords.Lesson(trustScore: 1.0);
        await store.InsertAsync(record, CancellationToken.None);
        var knowledgeClient = new FixedKnowledgeClient { SimilarNodes = [] };
        var reasoningEngineClient = new FixedReasoningEngineClient
        {
            CompareResult = (_, _) => new ConfidenceGuardResult(0.0, [], []),
        };
        var publisher = new NeverCalledLessonPromotedEventPublisher();
        var trigger = new ClusterTrigger(knowledgeClient, reasoningEngineClient, store, new ConfidenceGuard(), publisher, 0.5);

        await trigger.EvaluateAsync(record, CancellationToken.None);

        Assert.Equal(PipelineStage.Lesson, store.Find(record.RecordId)!.Stage);
    }

    [Fact]
    public async Task EvaluateAsync_Promotes_WhenExactlyThreeAcceptedMatchesAndConfidenceAtThreshold()
    {
        var store = new InMemoryPipelineRecordStore();
        var record = TestRecords.Lesson(trustScore: 1.0);
        await store.InsertAsync(record, CancellationToken.None);

        var candidates = Enumerable.Range(0, 3).Select(_ => TestRecords.Lesson()).ToArray();
        foreach (var candidate in candidates)
        {
            await store.InsertAsync(candidate, CancellationToken.None);
        }

        var knowledgeClient = new FixedKnowledgeClient
        {
            SimilarNodes = candidates.Select(c => TestRecords.Node(c.KnowledgeGraphRef)).ToArray(),
        };
        var reasoningEngineClient = new FixedReasoningEngineClient
        {
            CompareResult = (_, candidateList) => new ConfidenceGuardResult(0.5, candidateList.ToArray(), []),
        };
        var publisher = new RecordingLessonPromotedEventPublisher();
        var trigger = CreateTrigger(store, knowledgeClient, reasoningEngineClient, publisher);

        await trigger.EvaluateAsync(record, CancellationToken.None);

        var persisted = store.Find(record.RecordId)!;
        Assert.Equal(PipelineStage.Pattern, persisted.Stage);
        Assert.Equal(1, publisher.CallCount);
        Assert.Equal(record.RecordId, publisher.LastRecordId);
        Assert.Equal(record.RecordId, publisher.LastPatternRecordId);
    }

    [Fact]
    public async Task EvaluateAsync_DoesNotPromote_WhenFewerThanThreeAcceptedMatches()
    {
        var store = new InMemoryPipelineRecordStore();
        var record = TestRecords.Lesson(trustScore: 1.0);
        await store.InsertAsync(record, CancellationToken.None);
        var candidates = Enumerable.Range(0, 2).Select(_ => TestRecords.Lesson()).ToArray();
        foreach (var candidate in candidates)
        {
            await store.InsertAsync(candidate, CancellationToken.None);
        }

        var knowledgeClient = new FixedKnowledgeClient
        {
            SimilarNodes = candidates.Select(c => TestRecords.Node(c.KnowledgeGraphRef)).ToArray(),
        };
        var reasoningEngineClient = new FixedReasoningEngineClient
        {
            CompareResult = (_, candidateList) => new ConfidenceGuardResult(1.0, candidateList.ToArray(), []),
        };
        var publisher = new NeverCalledLessonPromotedEventPublisher();
        var trigger = new ClusterTrigger(knowledgeClient, reasoningEngineClient, store, new ConfidenceGuard(), publisher, 0.5);

        await trigger.EvaluateAsync(record, CancellationToken.None);

        Assert.Equal(PipelineStage.Lesson, store.Find(record.RecordId)!.Stage);
    }

    [Fact]
    public async Task EvaluateAsync_ExcludesQuarantinedAndArchivedCandidates_BeforeCallingCompareAsync()
    {
        var store = new InMemoryPipelineRecordStore();
        var record = TestRecords.Lesson(trustScore: 1.0);
        await store.InsertAsync(record, CancellationToken.None);

        var accepted = Enumerable.Range(0, 3).Select(_ => TestRecords.Lesson()).ToArray();
        var quarantined = TestRecords.Lesson(status: PipelineRecordStatus.Quarantined);
        var archived = TestRecords.Lesson(status: PipelineRecordStatus.Archived);
        foreach (var candidate in accepted.Append(quarantined).Append(archived))
        {
            await store.InsertAsync(candidate, CancellationToken.None);
        }

        var knowledgeClient = new FixedKnowledgeClient
        {
            SimilarNodes = accepted.Append(quarantined).Append(archived).Select(c => TestRecords.Node(c.KnowledgeGraphRef)).ToArray(),
        };
        IReadOnlyList<PipelineRecord>? candidatesSeenByCompare = null;
        var reasoningEngineClient = new FixedReasoningEngineClient
        {
            CompareResult = (_, candidateList) =>
            {
                candidatesSeenByCompare = candidateList.ToArray();
                return new ConfidenceGuardResult(1.0, candidatesSeenByCompare.ToArray(), []);
            },
        };
        var trigger = CreateTrigger(store, knowledgeClient, reasoningEngineClient);

        await trigger.EvaluateAsync(record, CancellationToken.None);

        Assert.NotNull(candidatesSeenByCompare);
        Assert.DoesNotContain(candidatesSeenByCompare, c => c.RecordId == quarantined.RecordId);
        Assert.DoesNotContain(candidatesSeenByCompare, c => c.RecordId == archived.RecordId);
        Assert.Equal(3, candidatesSeenByCompare.Count);
    }

    [Fact]
    public async Task EvaluateAsync_DoesNotPromote_WhenCompareAsyncThrows()
    {
        var store = new InMemoryPipelineRecordStore();
        var record = TestRecords.Lesson(trustScore: 1.0);
        await store.InsertAsync(record, CancellationToken.None);
        var knowledgeClient = new FixedKnowledgeClient { SimilarNodes = [] };
        var reasoningEngineClient = new FixedReasoningEngineClient { CompareThrows = new InvalidOperationException("unavailable") };
        var publisher = new NeverCalledLessonPromotedEventPublisher();
        var trigger = new ClusterTrigger(knowledgeClient, reasoningEngineClient, store, new ConfidenceGuard(), publisher, 0.5);

        await trigger.EvaluateAsync(record, CancellationToken.None);

        var persisted = store.Find(record.RecordId)!;
        Assert.Equal(PipelineStage.Lesson, persisted.Stage);
        Assert.Equal(0.0, persisted.ConfidenceScore, precision: 10);
    }

    [Fact]
    public async Task EvaluateAsync_DoesNotPromote_WhenOverallConfidenceIsBelowThreshold()
    {
        var store = new InMemoryPipelineRecordStore();
        var record = TestRecords.Lesson(trustScore: 0.4); // comparisonConfidence 1.0 * trust 0.4 = 0.4 < 0.5
        await store.InsertAsync(record, CancellationToken.None);
        var candidates = Enumerable.Range(0, 3).Select(_ => TestRecords.Lesson()).ToArray();
        foreach (var candidate in candidates)
        {
            await store.InsertAsync(candidate, CancellationToken.None);
        }

        var knowledgeClient = new FixedKnowledgeClient
        {
            SimilarNodes = candidates.Select(c => TestRecords.Node(c.KnowledgeGraphRef)).ToArray(),
        };
        var reasoningEngineClient = new FixedReasoningEngineClient
        {
            CompareResult = (_, candidateList) => new ConfidenceGuardResult(1.0, candidateList.ToArray(), []),
        };
        var publisher = new NeverCalledLessonPromotedEventPublisher();
        var trigger = new ClusterTrigger(knowledgeClient, reasoningEngineClient, store, new ConfidenceGuard(), publisher, 0.5);

        await trigger.EvaluateAsync(record, CancellationToken.None);

        var persisted = store.Find(record.RecordId)!;
        Assert.Equal(PipelineStage.Lesson, persisted.Stage);
        Assert.Equal(0.4, persisted.ConfidenceScore, precision: 10);
    }

    [Fact]
    public async Task EvaluateAsync_Promotes_WhenOverallConfidenceIsAboveThreshold()
    {
        var store = new InMemoryPipelineRecordStore();
        var record = TestRecords.Lesson(trustScore: 1.0);
        await store.InsertAsync(record, CancellationToken.None);
        var candidates = Enumerable.Range(0, 3).Select(_ => TestRecords.Lesson()).ToArray();
        foreach (var candidate in candidates)
        {
            await store.InsertAsync(candidate, CancellationToken.None);
        }

        var knowledgeClient = new FixedKnowledgeClient
        {
            SimilarNodes = candidates.Select(c => TestRecords.Node(c.KnowledgeGraphRef)).ToArray(),
        };
        var reasoningEngineClient = new FixedReasoningEngineClient
        {
            CompareResult = (_, candidateList) => new ConfidenceGuardResult(0.9, candidateList.ToArray(), []),
        };
        var publisher = new RecordingLessonPromotedEventPublisher();
        var trigger = CreateTrigger(store, knowledgeClient, reasoningEngineClient, publisher);

        await trigger.EvaluateAsync(record, CancellationToken.None);

        Assert.Equal(PipelineStage.Pattern, store.Find(record.RecordId)!.Stage);
        Assert.Equal(1, publisher.CallCount);
    }

    [Fact]
    public async Task EvaluateAsync_IsANoOp_WhenTheRecordHasAlreadyReachedPattern()
    {
        var store = new InMemoryPipelineRecordStore();
        var record = TestRecords.Lesson(stage: PipelineStage.Pattern);
        await store.InsertAsync(record, CancellationToken.None);
        var knowledgeClient = new FixedKnowledgeClient();
        var reasoningEngineClient = new FixedReasoningEngineClient { CompareThrows = new InvalidOperationException("must not be called") };
        var publisher = new NeverCalledLessonPromotedEventPublisher();
        var trigger = new ClusterTrigger(knowledgeClient, reasoningEngineClient, store, new ConfidenceGuard(), publisher, 0.5);

        await trigger.EvaluateAsync(record, CancellationToken.None);

        Assert.Equal(PipelineStage.Pattern, store.Find(record.RecordId)!.Stage);
    }
}
