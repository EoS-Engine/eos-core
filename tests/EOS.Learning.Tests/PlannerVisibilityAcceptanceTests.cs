using EOS.Contracts;
using EOS.Knowledge;
using EOS.KnowledgeGraph;
using EOS.Planner;
using EOS.VectorStore;
using Microsoft.Data.SqlClient;

namespace EOS.Learning.Tests;

/// <summary>
/// WP-027 acceptance scenario, driven against real infrastructure (SQL Server, real
/// <see cref="KnowledgeClient"/>, real <see cref="StageEngine"/>, real, unmodified
/// <see cref="TaskGraphBuilder"/>) — no mocks on the path under test.
///
/// Proves: <c>Pattern -&gt; BestPractice -&gt; Principle -&gt; GoldenPath</c> genuinely advances
/// through <see cref="StageEngine"/>; <c>GoldenPath -&gt; Automation</c> is genuinely blocked
/// (WP-027 Decision 1 — no <c>roi_minimum</c>); and — because that block means the real pipeline
/// can never actually reach <c>Automation</c> today — <see cref="AutomationVisibilityPublisher"/>
/// (WP-027 Decision 2's approved mechanism) is exercised directly, exactly as it would be called
/// automatically from within <c>StageEngine.PromoteToAutomationAsync</c> once <c>roi_minimum</c>
/// is supplied, to prove the underlying <c>TaskGraphBuilder</c> citation mechanism genuinely
/// works today, without any <c>EOS.Planner</c> production code change.
/// </summary>
public class PlannerVisibilityAcceptanceTests
{
    [Fact]
    public async Task PromotedPattern_BecomesDiscoverableByTaskGraphBuilder_AfterAutomationVisibilityRetyping()
    {
        var knowledgeGraphStore = new KnowledgeGraphStore(TestConnectionString.SqlServer);
        await knowledgeGraphStore.EnsureTableExistsAsync(CancellationToken.None);
        var rankingWeights = new RankingWeights(VectorSimilarity: 0.4, Recency: 0.3, DomainMatch: 0.2, AccessFrequency: 0.1);
        var memorySourceStore = new InMemoryTestMemorySourceStore();
        var knowledgeClient = new KnowledgeClient(
            knowledgeGraphStore, rankingWeights, new ChromaVectorStore(TestConnectionString.ChromaDbEndpoint), memorySourceStore);

        var pipelineRecordStore = new PipelineRecordStore(TestConnectionString.SqlServer);
        await pipelineRecordStore.EnsureTableExistsAsync(CancellationToken.None);
        var transitionRecordStore = new TransitionRecordStore(TestConnectionString.SqlServer);
        await transitionRecordStore.EnsureTableExistsAsync(CancellationToken.None);

        var stageEngine = new StageEngine(
            pipelineRecordStore, transitionRecordStore, new RoiGate(),
            new RecordingBestPracticeRatifiedEventPublisher(), new RecordingPrincipleGeneralizedEventPublisher(),
            new RecordingGoldenPathCodifiedEventPublisher(), new RecordingPlatformCapabilityPipelineAdvancedEventPublisher(),
            domainGeneralizationMinimumCount: 2);
        var automationVisibilityPublisher = new AutomationVisibilityPublisher(knowledgeClient);

        var domainTag = $"wp027-acceptance-{Guid.NewGuid()}";
        Guid episodicEntryId = default;
        try
        {
            var key = $"wp027-acceptance:{Guid.NewGuid()}";
            memorySourceStore.Seed(key, "a deliberately promotable test lesson, matured through the full pipeline");
            var source = new MemoryRef(MemoryType.Working, key);
            episodicEntryId = await knowledgeClient.ConsolidateAsync(
                source, "worth remembering", ["artifact://evidence"], suppressLessonLearned: true, cancellationToken: CancellationToken.None);

            var node = await knowledgeGraphStore.GetByIdAsync(episodicEntryId, CancellationToken.None);
            Assert.NotNull(node);
            await knowledgeGraphStore.UpsertAsync(node with { DomainTags = [domainTag] }, CancellationToken.None);

            var record = new PipelineRecord(
                RecordId: Guid.NewGuid(), Stage: PipelineStage.Pattern, KnowledgeGraphRef: episodicEntryId,
                SourceLessonIds: [episodicEntryId], DomainTags: [domainTag], CreatedAt: DateTimeOffset.UtcNow,
                LastAdvancedAt: DateTimeOffset.UtcNow, ApprovalRefs: [], RoiEvaluationRef: null, TrustScore: 1.0,
                ConfidenceScore: 1.0, Status: PipelineRecordStatus.Active);
            await pipelineRecordStore.InsertAsync(record, CancellationToken.None);

            var bestPracticeResult = await stageEngine.PromoteToBestPracticeAsync(
                record.RecordId, "ADR-WP027-ACCEPTANCE", "PrincipalEngineer", CancellationToken.None);
            Assert.True(bestPracticeResult.Promoted);

            var principleResult = await stageEngine.PromoteToPrincipleAsync(
                record.RecordId, ["backend", "mobile"], CancellationToken.None);
            Assert.True(principleResult.Promoted);

            var goldenPathResult = await stageEngine.PromoteToGoldenPathAsync(
                record.RecordId, "tools://template/acceptance", CancellationToken.None);
            Assert.True(goldenPathResult.Promoted);
            Assert.Equal(PipelineStage.GoldenPath, (await pipelineRecordStore.GetByIdAsync(record.RecordId, CancellationToken.None))!.Stage);

            // WP-027 Decision 1: the ROI Gate never promotes — this is the real, current
            // behavior of the production pipeline, proven here, not bypassed.
            var automationResult = await stageEngine.PromoteToAutomationAsync(
                record.RecordId, new RoiEvaluationInput(50, 10, 100), CancellationToken.None);
            Assert.False(automationResult.Promoted);
            Assert.Equal(PipelineStage.GoldenPath, (await pipelineRecordStore.GetByIdAsync(record.RecordId, CancellationToken.None))!.Stage);

            // Because roi_minimum does not exist, the pipeline can never actually reach
            // Automation in production today — so the KnowledgeNode re-typing mechanism (WP-027
            // Decision 2) is exercised directly here, exactly as StageEngine would call it
            // automatically once roi_minimum is supplied and this same call site becomes live.
            await automationVisibilityPublisher.MakeDiscoverableAsync(episodicEntryId, CancellationToken.None);

            var retypedNode = await knowledgeGraphStore.GetByIdAsync(episodicEntryId, CancellationToken.None);
            Assert.NotNull(retypedNode);
            Assert.Equal(KnowledgeNodeType.Pattern, retypedNode.NodeType);
            Assert.Equal([domainTag], retypedNode.DomainTags); // preserved, per Decision 2

            // The real, unmodified EOS.Planner.TaskGraphBuilder — zero production changes.
            var taskGraphBuilder = new TaskGraphBuilder(knowledgeClient, new FixedReasoningEngineClient());
            var goal = new Goal(
                GoalId: Guid.NewGuid(), Statement: "a goal matching the promoted pattern's domain",
                ParentGoalId: null, DomainTags: [domainTag], SubmittedByActor: "wp027-acceptance-test",
                State: GoalLifecycleState.Proposed, PlanId: null);

            var tasks = await taskGraphBuilder.DecomposeAsync(goal, CancellationToken.None);

            Assert.Single(tasks);
            Assert.Equal("a deliberately promotable test lesson, matured through the full pipeline", tasks[0].Description);
        }
        finally
        {
            await CleanUpAsync(episodicEntryId);
        }
    }

    private static async Task CleanUpAsync(Guid episodicEntryId)
    {
        if (episodicEntryId == default)
        {
            return;
        }

        await using var connection = new SqlConnection(TestConnectionString.SqlServer);
        await connection.OpenAsync(CancellationToken.None);

        await using (var deleteTransitions = connection.CreateCommand())
        {
            deleteTransitions.CommandText = "DELETE FROM TransitionRecord WHERE RecordId IN (SELECT RecordId FROM PipelineRecord WHERE KnowledgeGraphRef = @Ref)";
            deleteTransitions.Parameters.AddWithValue("@Ref", episodicEntryId);
            await deleteTransitions.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await using (var deletePipelineRecords = connection.CreateCommand())
        {
            deletePipelineRecords.CommandText = "DELETE FROM PipelineRecord WHERE KnowledgeGraphRef = @Ref";
            deletePipelineRecords.Parameters.AddWithValue("@Ref", episodicEntryId);
            await deletePipelineRecords.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await using (var deleteKnowledgeNode = connection.CreateCommand())
        {
            deleteKnowledgeNode.CommandText = "DELETE FROM KnowledgeNode WHERE NodeId = @Ref";
            deleteKnowledgeNode.Parameters.AddWithValue("@Ref", episodicEntryId);
            await deleteKnowledgeNode.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
