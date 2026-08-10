using EOS.Contracts;
using EOS.Knowledge;
using EOS.KnowledgeGraph;
using EOS.Learning;

namespace EOS.Learning.Tests;

internal static class TestConnectionString
{
    public static string SqlServer =>
        Environment.GetEnvironmentVariable("EOS_SQLSERVER_CONNECTION_STRING")
        ?? throw new InvalidOperationException("EOS_SQLSERVER_CONNECTION_STRING is not set.");

    public static string ChromaDbEndpoint =>
        Environment.GetEnvironmentVariable("EOS_CHROMADB_ENDPOINT")
        ?? throw new InvalidOperationException("EOS_CHROMADB_ENDPOINT is not set.");
}

internal sealed class InMemoryPipelineRecordStore : IPipelineRecordStore
{
    private readonly List<PipelineRecord> _records = [];
    public int InsertCallCount { get; private set; }

    public Task EnsureTableExistsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task InsertAsync(PipelineRecord record, CancellationToken cancellationToken = default)
    {
        InsertCallCount++;
        _records.Add(record);
        return Task.CompletedTask;
    }

    public Task<PipelineRecord?> GetBySourceLessonIdAsync(Guid episodicEntryId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_records.FirstOrDefault(record => record.SourceLessonIds.Contains(episodicEntryId)));

    public Task<IReadOnlyList<PipelineRecord>> GetByKnowledgeGraphRefsAsync(
        IEnumerable<Guid> knowledgeGraphRefs, CancellationToken cancellationToken = default)
    {
        var refs = knowledgeGraphRefs.ToHashSet();
        IReadOnlyList<PipelineRecord> result = _records.Where(record => refs.Contains(record.KnowledgeGraphRef)).ToList();
        return Task.FromResult(result);
    }

    public Task<PipelineRecord?> GetByIdAsync(Guid recordId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Find(recordId));

    public Task<IReadOnlyList<PipelineRecord>> GetByStatusAsync(
        PipelineRecordStatus status, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PipelineRecord> result = _records.Where(record => record.Status == status).ToList();
        return Task.FromResult(result);
    }

    public Task UpdateStageAsync(
        Guid recordId, PipelineStage stage, PipelineRecordStatus status, double confidenceScore, CancellationToken cancellationToken = default)
    {
        var index = _records.FindIndex(record => record.RecordId == recordId);
        if (index >= 0)
        {
            _records[index] = _records[index] with { Stage = stage, Status = status, ConfidenceScore = confidenceScore };
        }

        return Task.CompletedTask;
    }

    public Task UpdateApprovalRefsAsync(Guid recordId, string[] approvalRefs, CancellationToken cancellationToken = default)
    {
        var index = _records.FindIndex(record => record.RecordId == recordId);
        if (index >= 0)
        {
            _records[index] = _records[index] with { ApprovalRefs = approvalRefs };
        }

        return Task.CompletedTask;
    }

    public Task UpdateRoiEvaluationRefAsync(Guid recordId, string roiEvaluationRef, CancellationToken cancellationToken = default)
    {
        var index = _records.FindIndex(record => record.RecordId == recordId);
        if (index >= 0)
        {
            _records[index] = _records[index] with { RoiEvaluationRef = roiEvaluationRef };
        }

        return Task.CompletedTask;
    }

    public PipelineRecord? Find(Guid recordId) => _records.FirstOrDefault(record => record.RecordId == recordId);
}

internal sealed class ThrowingOnInsertPipelineRecordStore : IPipelineRecordStore
{
    public Task EnsureTableExistsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task InsertAsync(PipelineRecord record, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Simulated persistence failure.");

    public Task<PipelineRecord?> GetBySourceLessonIdAsync(Guid episodicEntryId, CancellationToken cancellationToken = default) =>
        Task.FromResult<PipelineRecord?>(null);

    public Task<IReadOnlyList<PipelineRecord>> GetByKnowledgeGraphRefsAsync(
        IEnumerable<Guid> knowledgeGraphRefs, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PipelineRecord>>([]);

    public Task<PipelineRecord?> GetByIdAsync(Guid recordId, CancellationToken cancellationToken = default) =>
        Task.FromResult<PipelineRecord?>(null);

    public Task<IReadOnlyList<PipelineRecord>> GetByStatusAsync(
        PipelineRecordStatus status, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PipelineRecord>>([]);

    public Task UpdateStageAsync(
        Guid recordId, PipelineStage stage, PipelineRecordStatus status, double confidenceScore, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Simulated persistence failure.");

    public Task UpdateApprovalRefsAsync(Guid recordId, string[] approvalRefs, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Simulated persistence failure.");

    public Task UpdateRoiEvaluationRefAsync(Guid recordId, string roiEvaluationRef, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Simulated persistence failure.");
}

internal sealed class InMemoryTransitionRecordStore : ITransitionRecordStore
{
    private readonly List<TransitionRecord> _records = [];

    public Task EnsureTableExistsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task InsertAsync(TransitionRecord record, CancellationToken cancellationToken = default)
    {
        _records.Add(record);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TransitionRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TransitionRecord> result = _records.ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<TransitionRecord>> GetByRecordIdAsync(Guid recordId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TransitionRecord> result = _records.Where(r => r.RecordId == recordId).ToList();
        return Task.FromResult(result);
    }

    /// <summary>Test-only mutation hook, used to simulate tampering/corruption for IntegrityChecker tests.</summary>
    public void ReplaceLast(TransitionRecord replacement)
    {
        _records[^1] = replacement;
    }
}

internal sealed class RecordingBestPracticeRatifiedEventPublisher : IBestPracticeRatifiedEventPublisher
{
    public int CallCount { get; private set; }
    public void PublishBestPracticeRatified(Guid recordId) => CallCount++;
}

internal sealed class RecordingPrincipleGeneralizedEventPublisher : IPrincipleGeneralizedEventPublisher
{
    public int CallCount { get; private set; }
    public void PublishPrincipleGeneralized(Guid recordId) => CallCount++;
}

internal sealed class RecordingGoldenPathCodifiedEventPublisher : IGoldenPathCodifiedEventPublisher
{
    public int CallCount { get; private set; }
    public void PublishGoldenPathCodified(Guid recordId) => CallCount++;
}

internal sealed class RecordingPlatformCapabilityPipelineAdvancedEventPublisher : IPlatformCapabilityPipelineAdvancedEventPublisher
{
    public int CallCount { get; private set; }
    public void PublishPlatformCapabilityPipelineAdvanced(Guid recordId) => CallCount++;
}

internal sealed class RecordingLessonQuarantineClearedEventPublisher : ILessonQuarantineClearedEventPublisher
{
    public int CallCount { get; private set; }
    public Guid LastRecordId { get; private set; }
    public string? LastClearingRole { get; private set; }
    public string? LastJustification { get; private set; }

    public void PublishLessonQuarantineCleared(Guid recordId, string clearingRole, string justification)
    {
        CallCount++;
        LastRecordId = recordId;
        LastClearingRole = clearingRole;
        LastJustification = justification;
    }
}

internal sealed class RecordingDataIntegrityViolationDetectedEventPublisher : IDataIntegrityViolationDetectedEventPublisher
{
    public int CallCount { get; private set; }
    public void PublishDataIntegrityViolationDetected(Guid recordId, PipelineStage fromStage, PipelineStage toStage) => CallCount++;
}

internal sealed class RecordingFitnessFunctionViolatedEventPublisher : IFitnessFunctionViolatedEventPublisher
{
    public int CallCount { get; private set; }
    public string? LastFitnessFunctionId { get; private set; }

    public void PublishFitnessFunctionViolated(string fitnessFunctionId, double observedValue, double threshold)
    {
        CallCount++;
        LastFitnessFunctionId = fitnessFunctionId;
    }
}

internal sealed class RecordingLessonStalledEventPublisher : ILessonStalledEventPublisher
{
    public int CallCount { get; private set; }
    public void PublishLessonStalled(Guid recordId) => CallCount++;
}

internal sealed class RecordingSelfReferentialOutcomeFlaggedEventPublisher : ISelfReferentialOutcomeFlaggedEventPublisher
{
    public int CallCount { get; private set; }
    public void PublishSelfReferentialOutcomeFlagged(Guid recordId, Guid taskId) => CallCount++;
}

internal sealed class FixedTaskProvenanceQueryClient : ITaskProvenanceQueryClient
{
    public IReadOnlyList<Guid> SelfReferentialTaskIds { get; set; } = [];

    public Task<IReadOnlyList<Guid>> GetSelfReferentialTaskIdsAsync(Guid knowledgeGraphRef, CancellationToken cancellationToken = default) =>
        Task.FromResult(SelfReferentialTaskIds);
}

internal sealed class InMemoryIngestionRateGuardStore : IIngestionRateGuardStore
{
    private readonly Dictionary<(string ProducerRole, DateTimeOffset WindowStart), int> _counts = [];

    public Task EnsureTableExistsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<int> IncrementAndGetCountAsync(
        string producerRole, DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken cancellationToken = default)
    {
        var key = (producerRole, windowStart);
        _counts[key] = _counts.GetValueOrDefault(key) + 1;
        return Task.FromResult(_counts[key]);
    }
}

/// <summary>Always reports a fixed count, ignoring the wall-clock bucket — isolates the guard's threshold comparison.</summary>
internal sealed class FixedCountIngestionRateGuardStore(int fixedCount) : IIngestionRateGuardStore
{
    public Task EnsureTableExistsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<int> IncrementAndGetCountAsync(
        string producerRole, DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken cancellationToken = default) =>
        Task.FromResult(fixedCount);
}

internal sealed class RecordingLessonPromotedEventPublisher : ILessonPromotedEventPublisher
{
    public int CallCount { get; private set; }
    public Guid LastRecordId { get; private set; }
    public Guid LastPatternRecordId { get; private set; }

    public void PublishLessonPromoted(Guid recordId, Guid patternRecordId)
    {
        CallCount++;
        LastRecordId = recordId;
        LastPatternRecordId = patternRecordId;
    }
}

internal sealed class RecordingLessonQuarantinedEventPublisher : ILessonQuarantinedEventPublisher
{
    public int CallCount { get; private set; }
    public Guid LastRecordId { get; private set; }
    public string? LastReason { get; private set; }

    public void PublishLessonQuarantined(Guid recordId, string reason)
    {
        CallCount++;
        LastRecordId = recordId;
        LastReason = reason;
    }
}

internal sealed class NeverCalledLessonPromotedEventPublisher : ILessonPromotedEventPublisher
{
    public void PublishLessonPromoted(Guid recordId, Guid patternRecordId) =>
        throw new InvalidOperationException("PublishLessonPromoted must not be called in this scenario.");
}

internal sealed class NeverCalledLessonQuarantinedEventPublisher : ILessonQuarantinedEventPublisher
{
    public void PublishLessonQuarantined(Guid recordId, string reason) =>
        throw new InvalidOperationException("PublishLessonQuarantined must not be called in this scenario.");
}

/// <summary>Configurable stand-in for IReasoningEngineClient — only CompareAsync/GetTrustSignalAsync are used by WP-026.</summary>
internal sealed class FixedReasoningEngineClient : IReasoningEngineClient
{
    public Func<PipelineRecord, IEnumerable<PipelineRecord>, ConfidenceGuardResult>? CompareResult { get; set; }
    public Exception? CompareThrows { get; set; }
    public TrustSignal? TrustSignalResult { get; set; }
    public Exception? TrustSignalThrows { get; set; }

    public Task<Decision[]> ReasonAsync(ReasoningRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not used by WP-026.");

    public Task<ConfidenceGuardResult> CompareAsync(
        PipelineRecord subject, IEnumerable<PipelineRecord> candidates, CancellationToken cancellationToken = default)
    {
        if (CompareThrows is not null)
        {
            throw CompareThrows;
        }

        return Task.FromResult(CompareResult!(subject, candidates));
    }

    public Task<TrustSignal> GetTrustSignalAsync(string sourceRole, CancellationToken cancellationToken = default)
    {
        if (TrustSignalThrows is not null)
        {
            throw TrustSignalThrows;
        }

        return Task.FromResult(TrustSignalResult ?? new TrustSignal(sourceRole, 0.5, "no-history-available"));
    }

    public Task<Summary> SummarizeAsync(string content, int? sizeBudget = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not used by WP-026.");
}

/// <summary>Configurable stand-in for IKnowledgeClient — only QueryAsync/QuerySimilarAsync are used by WP-026.</summary>
internal sealed class FixedKnowledgeClient : IKnowledgeClient
{
    public IReadOnlyList<KnowledgeNode> EpisodicNodes { get; set; } = [];
    public IReadOnlyList<KnowledgeNode> SimilarNodes { get; set; } = [];

    public Task UpdateAsync(
        Guid nodeId, KnowledgeNodeType nodeType, string content, string[] domainTags, string[] evidenceRefs,
        KnowledgeMetadata? metadata = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not used by WP-026.");

    public Task<IEnumerable<KnowledgeNode>> QueryAsync(
        MemoryType? type, string[]? domainTags, DateRange? range, CancellationToken cancellationToken = default) =>
        Task.FromResult<IEnumerable<KnowledgeNode>>(EpisodicNodes);

    public Task<IEnumerable<KnowledgeNode>> QuerySimilarAsync(Guid nodeId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IEnumerable<KnowledgeNode>>(SimilarNodes);

    public Task<ContextPayload> AssembleContextAsync(ContextRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not used by WP-026.");

    public Task<Guid> ConsolidateAsync(
        MemoryRef source, string reason, string[] evidenceRefs, bool suppressLessonLearned = false,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not used by WP-026.");
}

internal static class TestRecords
{
    public static PipelineRecord Lesson(
        Guid? recordId = null,
        Guid? knowledgeGraphRef = null,
        Guid[]? sourceLessonIds = null,
        string[]? domainTags = null,
        double trustScore = 0.5,
        double confidenceScore = 0.0,
        PipelineRecordStatus status = PipelineRecordStatus.Active,
        PipelineStage stage = PipelineStage.Lesson)
    {
        var id = recordId ?? Guid.NewGuid();
        var knowledgeRef = knowledgeGraphRef ?? Guid.NewGuid();
        return new PipelineRecord(
            RecordId: id,
            Stage: stage,
            KnowledgeGraphRef: knowledgeRef,
            SourceLessonIds: sourceLessonIds ?? [knowledgeRef],
            DomainTags: domainTags ?? [],
            CreatedAt: DateTimeOffset.UtcNow,
            LastAdvancedAt: DateTimeOffset.UtcNow,
            ApprovalRefs: [],
            RoiEvaluationRef: null,
            TrustScore: trustScore,
            ConfidenceScore: confidenceScore,
            Status: status);
    }

    public static KnowledgeNode Node(Guid? nodeId = null, string[]? domainTags = null) => new(
        NodeId: nodeId ?? Guid.NewGuid(),
        NodeType: KnowledgeNodeType.Lesson,
        Content: "test content",
        DomainTags: domainTags ?? [],
        EvidenceRefs: [],
        CreatedAt: DateTimeOffset.UtcNow);
}
