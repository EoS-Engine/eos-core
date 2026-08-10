using EOS.Contracts;
using EOS.Knowledge;
using EOS.KnowledgeGraph;
using EOS.Reasoning;
using EOS.SDK;
using EOS.VectorStore;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;

namespace EOS.Learning.Tests;

/// <summary>
/// WP-026 roadmap acceptance scenario, driven against real infrastructure (SQL Server) and real
/// production components (<see cref="KnowledgeClient"/>, <see cref="ReasoningEngine"/>,
/// <see cref="PipelineRecordStore"/>, <see cref="IngestionRateGuardStore"/>) — no mocks on the
/// path under test.
///
/// The roadmap describes this as "three similar test Lessons... promote to a Pattern." The
/// locked WP-026 Implementation Authorization decision is "acceptedMatches &gt;= 3" — read
/// literally against §11.2's own pseudocode, <c>accepted_matches</c> excludes the subject being
/// evaluated. A cluster of exactly 3 total Lessons therefore gives the 3rd Lesson's own
/// evaluation only 2 prior candidates (QuerySimilarAsync excludes the querying node itself),
/// which is insufficient under the locked ">= 3" rule. This test uses 4 similar Lessons so that
/// the 4th Lesson's evaluation has exactly 3 prior candidates, satisfying the locked rule exactly
/// as authorized rather than silently reinterpreting it — disclosed here rather than silently
/// changed.
///
/// Independent WP-026 verification reclassified an earlier "always skipped" version of this
/// test: the observed confidence dilution was dominated by this test's OWN prior, un-cleaned-up
/// executions leaving real <c>PipelineRecord</c>/<c>KnowledgeNode</c> rows in the shared database
/// (verified via direct SQL: of 1648 accumulated Lesson-type <c>KnowledgeNode</c> rows, only the
/// ~20 with a matching <c>PipelineRecord</c> can ever surface as a <c>ClusterTrigger</c>
/// candidate — and those were this test's own repeated-run residue, not unrelated feature
/// pollution). This test now cleans up exactly the rows it itself creates (scoped by this run's
/// own freshly-generated GUIDs, captured incrementally so a mid-run failure still cleans up
/// whatever was created before the failure) in a <c>finally</c> block, so repeated executions no
/// longer dilute each other. A residual, smaller, genuine forward-looking concern remains and is
/// intentionally NOT addressed here (Board-level, outside this file's authority): in long-running
/// production use, <c>CompareAsync</c>'s ratio-based confidence is bounded by the backlog of
/// not-yet-clustered <c>EOS.Learning</c> records sharing a node type, which could still dilute a
/// genuine cluster if that backlog grows large — this fix only removes this TEST's own
/// self-inflicted contribution to that pool, it does not bound production's.
/// </summary>
public class LearningEngineAcceptanceTests
{
    [Fact]
    public async Task FourSimilarLessons_ClusterAndPromoteToAPattern_WithARealLessonPromotedEvent()
    {
        var knowledgeGraphStore = new KnowledgeGraphStore(TestConnectionString.SqlServer);
        await knowledgeGraphStore.EnsureTableExistsAsync(CancellationToken.None);
        var rankingWeights = new RankingWeights(VectorSimilarity: 0.4, Recency: 0.3, DomainMatch: 0.2, AccessFrequency: 0.1);
        var memorySourceStore = new InMemoryTestMemorySourceStore();
        var knowledgeClient = new KnowledgeClient(
            knowledgeGraphStore, rankingWeights, new ChromaVectorStore(TestConnectionString.ChromaDbEndpoint), memorySourceStore);

        var reasoningEngine = new ReasoningEngine(
            NeverCalledAIProviderClient.Instance,
            NeverCalledContextAcquisitionProvider.Instance,
            new ReasoningEngineOptions(ContextExpansionCap: 1, LowConfidenceFloor: 0.3),
            NoOpDecisionMadeEventPublisher.Instance,
            NoOpLowConfidenceDecisionFlaggedEventPublisher.Instance,
            NoOpContextExpansionRequestedEventPublisher.Instance,
            NullLogger<ReasoningEngine>.Instance);

        var pipelineRecordStore = new PipelineRecordStore(TestConnectionString.SqlServer);
        await pipelineRecordStore.EnsureTableExistsAsync(CancellationToken.None);
        var ingestionRateGuardStore = new IngestionRateGuardStore(TestConnectionString.SqlServer);
        await ingestionRateGuardStore.EnsureTableExistsAsync(CancellationToken.None);
        var ingestionRateGuard = new IngestionRateGuard(ingestionRateGuardStore, windowSeconds: 3600, thresholdCount: 1000);
        var lessonPromotedPublisher = new RecordingLessonPromotedEventPublisher();
        var clusterTrigger = new ClusterTrigger(
            knowledgeClient, reasoningEngine, pipelineRecordStore, new ConfidenceGuard(), lessonPromotedPublisher, 0.5);
        var ingestion = new Ingestion(
            knowledgeClient, reasoningEngine, pipelineRecordStore, ingestionRateGuard, clusterTrigger,
            new RecordingLessonQuarantinedEventPublisher());

        var sharedDomainTag = $"acceptance-{Guid.NewGuid()}";
        // Captured incrementally (not just at the end) so a failure partway through the loop or
        // the assertions below still leaves this `finally` able to clean up whatever this run
        // actually created — never more, never less, and never anything from another run (every
        // ID here is this run's own freshly-generated Guid, so collision with any other run's or
        // any other test's data is not possible).
        var episodicEntryIds = new List<Guid>();
        try
        {
            for (var i = 0; i < 4; i++)
            {
                var key = $"wp026-acceptance:{Guid.NewGuid()}";
                memorySourceStore.Seed(key, $"a deliberately similar test lesson, number {i}");
                var source = new MemoryRef(MemoryType.Working, key);
                var episodicEntryId = await knowledgeClient.ConsolidateAsync(
                    source, "worth remembering", ["artifact://evidence"], suppressLessonLearned: true, cancellationToken: CancellationToken.None);
                episodicEntryIds.Add(episodicEntryId);

                // Give every Lesson node the same domain tag so ReasoningEngine.CompareAsync's
                // structural match (DomainTags overlap) accepts them — real infrastructure, no
                // mocked similarity judgment.
                var node = await knowledgeGraphStore.GetByIdAsync(episodicEntryId, CancellationToken.None);
                Assert.NotNull(node);
                await knowledgeGraphStore.UpsertAsync(node with { DomainTags = [sharedDomainTag] }, CancellationToken.None);

                await ingestion.OnLessonLearnedAsync(episodicEntryId, key, CancellationToken.None);
            }

            var firstThreeStayAsLessons = await Task.WhenAll(
                episodicEntryIds.Take(3).Select(id => pipelineRecordStore.GetBySourceLessonIdAsync(id, CancellationToken.None)));
            Assert.All(firstThreeStayAsLessons, record => Assert.Equal(PipelineStage.Lesson, record!.Stage));

            var fourthRecord = await pipelineRecordStore.GetBySourceLessonIdAsync(episodicEntryIds[3], CancellationToken.None);
            Assert.NotNull(fourthRecord);
            Assert.Equal(PipelineStage.Pattern, fourthRecord.Stage);
            Assert.Equal(1, lessonPromotedPublisher.CallCount);
            Assert.Equal(fourthRecord.RecordId, lessonPromotedPublisher.LastRecordId);
        }
        finally
        {
            await CleanUpAsync(episodicEntryIds);
        }
    }

    /// <summary>
    /// Deletes exactly the rows this test run created, scoped by the run's own freshly-generated
    /// <paramref name="episodicEntryIds"/> (each is a fresh <see cref="Guid"/>, so this can never
    /// match any other run's or any other test's data) — so repeated executions of this test no
    /// longer dilute each other's <c>ClusterTrigger</c> candidate pool. Neither
    /// <c>PipelineRecordStore</c> nor <c>KnowledgeGraphStore</c> expose a delete method (by
    /// design — production code has no reason to delete pipeline/knowledge history), so this
    /// uses a direct, narrowly-scoped SQL statement, test-only, matching this repository's
    /// existing SQL Server test infrastructure (no new database technology).
    /// </summary>
    private static async Task CleanUpAsync(IReadOnlyCollection<Guid> episodicEntryIds)
    {
        if (episodicEntryIds.Count == 0)
        {
            return;
        }

        await using var connection = new SqlConnection(TestConnectionString.SqlServer);
        await connection.OpenAsync(CancellationToken.None);

        var parameterNames = episodicEntryIds.Select((_, index) => $"@Id{index}").ToArray();

        await using (var deletePipelineRecords = connection.CreateCommand())
        {
            deletePipelineRecords.CommandText =
                $"DELETE FROM PipelineRecord WHERE KnowledgeGraphRef IN ({string.Join(", ", parameterNames)})";
            AddIdParameters(deletePipelineRecords, parameterNames, episodicEntryIds);
            await deletePipelineRecords.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await using (var deleteKnowledgeNodes = connection.CreateCommand())
        {
            deleteKnowledgeNodes.CommandText = $"DELETE FROM KnowledgeNode WHERE NodeId IN ({string.Join(", ", parameterNames)})";
            AddIdParameters(deleteKnowledgeNodes, parameterNames, episodicEntryIds);
            await deleteKnowledgeNodes.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private static void AddIdParameters(SqlCommand command, IReadOnlyList<string> parameterNames, IReadOnlyCollection<Guid> ids)
    {
        var idArray = ids.ToArray();
        for (var index = 0; index < idArray.Length; index++)
        {
            command.Parameters.AddWithValue(parameterNames[index], idArray[index]);
        }
    }
}

internal sealed class InMemoryTestMemorySourceStore : IMemorySourceStore
{
    private readonly Dictionary<string, string> _content = [];
    private readonly HashSet<string> _consolidated = [];

    public void Seed(string key, string content) => _content[key] = content;

    public Task<string?> GetContentAsync(MemoryRef source, CancellationToken cancellationToken = default) =>
        Task.FromResult(_content.GetValueOrDefault(source.Key));

    public Task<bool> IsConsolidatedAsync(MemoryRef source, CancellationToken cancellationToken = default) =>
        Task.FromResult(_consolidated.Contains(source.Key));

    public Task MarkConsolidatedAsync(MemoryRef source, CancellationToken cancellationToken = default)
    {
        _consolidated.Add(source.Key);
        return Task.CompletedTask;
    }
}

internal sealed class NeverCalledAIProviderClient : IAIProviderClient
{
    public static readonly NeverCalledAIProviderClient Instance = new();

    public Task<InferenceResult> InferAsync(InferenceRequest request, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Must not be called — CompareAsync/GetTrustSignalAsync never invoke the AI Provider.");
}

internal sealed class NeverCalledContextAcquisitionProvider : IContextAcquisitionProvider
{
    public static readonly NeverCalledContextAcquisitionProvider Instance = new();

    public Task<AcquiredContext> AcquireContextAsync(ReasoningContextScope scope, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Must not be called — CompareAsync/GetTrustSignalAsync never acquire context.");
}

internal sealed class NoOpDecisionMadeEventPublisher : IDecisionMadeEventPublisher
{
    public static readonly NoOpDecisionMadeEventPublisher Instance = new();

    public void PublishDecisionMade(Guid decisionId, Guid requestId, double confidence, double riskScore, ReasoningType reasoningTypeApplied) { }
}

internal sealed class NoOpLowConfidenceDecisionFlaggedEventPublisher : ILowConfidenceDecisionFlaggedEventPublisher
{
    public static readonly NoOpLowConfidenceDecisionFlaggedEventPublisher Instance = new();

    public void PublishLowConfidenceDecisionFlagged(Guid decisionId, Guid correlationId, double confidence, double threshold) { }
}

internal sealed class NoOpContextExpansionRequestedEventPublisher : IContextExpansionRequestedEventPublisher
{
    public static readonly NoOpContextExpansionRequestedEventPublisher Instance = new();

    public void PublishContextExpansionRequested(Guid requestId, ReasoningContextScope originalScope, ReasoningContextScope expandedScope) { }
}
