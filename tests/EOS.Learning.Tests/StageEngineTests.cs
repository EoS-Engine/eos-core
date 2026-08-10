using EOS.Contracts;

namespace EOS.Learning.Tests;

public class StageEngineTests
{
    private static (StageEngine Engine, InMemoryPipelineRecordStore Records, InMemoryTransitionRecordStore Transitions,
        RecordingBestPracticeRatifiedEventPublisher BestPracticeRatified, RecordingPrincipleGeneralizedEventPublisher PrincipleGeneralized,
        RecordingGoldenPathCodifiedEventPublisher GoldenPathCodified,
        RecordingPlatformCapabilityPipelineAdvancedEventPublisher PlatformCapabilityPipelineAdvanced)
        CreateEngine(int domainGeneralizationMinimumCount = 2)
    {
        var records = new InMemoryPipelineRecordStore();
        var transitions = new InMemoryTransitionRecordStore();
        var bestPracticeRatified = new RecordingBestPracticeRatifiedEventPublisher();
        var principleGeneralized = new RecordingPrincipleGeneralizedEventPublisher();
        var goldenPathCodified = new RecordingGoldenPathCodifiedEventPublisher();
        var platformCapabilityPipelineAdvanced = new RecordingPlatformCapabilityPipelineAdvancedEventPublisher();
        var engine = new StageEngine(
            records, transitions, new RoiGate(), bestPracticeRatified, principleGeneralized, goldenPathCodified,
            platformCapabilityPipelineAdvanced, domainGeneralizationMinimumCount);

        return (engine, records, transitions, bestPracticeRatified, principleGeneralized, goldenPathCodified, platformCapabilityPipelineAdvanced);
    }

    [Fact]
    public async Task PromoteToBestPracticeAsync_PromotesAndPersistsTransitionRecord()
    {
        var (engine, records, transitions, bestPracticeRatified, _, _, _) = CreateEngine();
        var record = TestRecords.Lesson(stage: PipelineStage.Pattern);
        await records.InsertAsync(record, CancellationToken.None);

        var result = await engine.PromoteToBestPracticeAsync(record.RecordId, "ADR-999", "PrincipalEngineer", CancellationToken.None);

        Assert.True(result.Promoted);
        Assert.Equal(PipelineStage.BestPractice, records.Find(record.RecordId)!.Stage);
        Assert.Contains("ADR-999", records.Find(record.RecordId)!.ApprovalRefs);
        Assert.Equal(1, bestPracticeRatified.CallCount);
        var stored = await transitions.GetByRecordIdAsync(record.RecordId, CancellationToken.None);
        Assert.Single(stored);
        Assert.Equal(PipelineStage.Pattern, stored[0].FromStage);
        Assert.Equal(PipelineStage.BestPractice, stored[0].ToStage);
        Assert.Equal(IntegrityHashCalculator.Compute(stored[0]), stored[0].IntegrityHash);
    }

    // The four cases explicitly required for RequireStageAsync's idempotency guard:
    // 1) current == target -> no-op; 2) current > target -> no-op; 3) current < target but the
    // wrong predecessor -> rejection; 4) a normal valid transition still succeeds (covered by
    // PromoteToBestPracticeAsync_PromotesAndPersistsTransitionRecord above).

    [Fact]
    public async Task PromoteToBestPracticeAsync_IsIdempotent_WhenAlreadyExactlyAtTheTargetStage()
    {
        var (engine, records, _, bestPracticeRatified, _, _, _) = CreateEngine();
        var record = TestRecords.Lesson(stage: PipelineStage.BestPractice);
        await records.InsertAsync(record, CancellationToken.None);

        var result = await engine.PromoteToBestPracticeAsync(record.RecordId, "ADR-999", "PrincipalEngineer", CancellationToken.None);

        Assert.False(result.Promoted);
        Assert.Equal(0, bestPracticeRatified.CallCount);
        Assert.Equal(PipelineStage.BestPractice, records.Find(record.RecordId)!.Stage);
    }

    [Theory]
    [InlineData(PipelineStage.Principle)]
    [InlineData(PipelineStage.GoldenPath)]
    [InlineData(PipelineStage.Automation)]
    [InlineData(PipelineStage.ReusableComponent)]
    [InlineData(PipelineStage.PlatformCapability)]
    public async Task PromoteToBestPracticeAsync_IsIdempotent_WhenAlreadyBeyondTheTargetStage(PipelineStage stageBeyondTarget)
    {
        // The class's own doc comment claims "already-at-or-beyond target stage is a no-op" —
        // this proves "beyond" specifically, not merely "==", for every later stage.
        var (engine, records, _, bestPracticeRatified, _, _, _) = CreateEngine();
        var record = TestRecords.Lesson(stage: stageBeyondTarget);
        await records.InsertAsync(record, CancellationToken.None);

        var result = await engine.PromoteToBestPracticeAsync(record.RecordId, "ADR-999", "PrincipalEngineer", CancellationToken.None);

        Assert.False(result.Promoted);
        Assert.Equal(0, bestPracticeRatified.CallCount);
        Assert.Equal(stageBeyondTarget, records.Find(record.RecordId)!.Stage);
    }

    [Fact]
    public async Task PromoteToBestPracticeAsync_Throws_WhenRecordIsBeforeTargetButAtTheWrongPredecessor()
    {
        var (engine, records, _, _, _, _, _) = CreateEngine();
        var record = TestRecords.Lesson(stage: PipelineStage.Lesson);
        await records.InsertAsync(record, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => engine.PromoteToBestPracticeAsync(record.RecordId, "ADR-999", "PrincipalEngineer", CancellationToken.None));
    }

    [Fact]
    public async Task PromoteToPrincipleAsync_Promotes_WhenAtLeastTwoDistinctDomains()
    {
        var (engine, records, _, _, principleGeneralized, _, _) = CreateEngine();
        var record = TestRecords.Lesson(stage: PipelineStage.BestPractice);
        await records.InsertAsync(record, CancellationToken.None);

        var result = await engine.PromoteToPrincipleAsync(record.RecordId, ["backend", "mobile"], CancellationToken.None);

        Assert.True(result.Promoted);
        Assert.Equal(PipelineStage.Principle, records.Find(record.RecordId)!.Stage);
        Assert.Equal(1, principleGeneralized.CallCount);
    }

    [Fact]
    public async Task PromoteToPrincipleAsync_DoesNotPromote_WhenFewerThanTwoDistinctDomains()
    {
        var (engine, records, _, _, principleGeneralized, _, _) = CreateEngine();
        var record = TestRecords.Lesson(stage: PipelineStage.BestPractice);
        await records.InsertAsync(record, CancellationToken.None);

        var result = await engine.PromoteToPrincipleAsync(record.RecordId, ["backend"], CancellationToken.None);

        Assert.False(result.Promoted);
        Assert.Equal(PipelineStage.BestPractice, records.Find(record.RecordId)!.Stage);
        Assert.Equal(0, principleGeneralized.CallCount);
    }

    [Fact]
    public async Task PromoteToPrincipleAsync_TreatsCaseInsensitiveDuplicateDomainsAsOne()
    {
        var (engine, records, _, _, _, _, _) = CreateEngine();
        var record = TestRecords.Lesson(stage: PipelineStage.BestPractice);
        await records.InsertAsync(record, CancellationToken.None);

        var result = await engine.PromoteToPrincipleAsync(record.RecordId, ["Backend", "backend"], CancellationToken.None);

        Assert.False(result.Promoted);
    }

    [Fact]
    public async Task PromoteToPrincipleAsync_TrimsSurroundingWhitespace_BeforeCountingDistinctDomains()
    {
        // CodeRabbit R1 finding #7: "backend" and " backend " must count as the same domain.
        var (engine, records, _, _, _, _, _) = CreateEngine();
        var record = TestRecords.Lesson(stage: PipelineStage.BestPractice);
        await records.InsertAsync(record, CancellationToken.None);

        var result = await engine.PromoteToPrincipleAsync(record.RecordId, ["backend", " backend "], CancellationToken.None);

        Assert.False(result.Promoted);
    }

    [Fact]
    public async Task PromoteToPrincipleAsync_IgnoresBlankAndWhitespaceOnlyEntries()
    {
        // CodeRabbit R1 finding #7: blank/whitespace-only entries must not count as real domains.
        var (engine, records, _, _, _, _, _) = CreateEngine();
        var record = TestRecords.Lesson(stage: PipelineStage.BestPractice);
        await records.InsertAsync(record, CancellationToken.None);

        var result = await engine.PromoteToPrincipleAsync(record.RecordId, ["backend", "", "   "], CancellationToken.None);

        Assert.False(result.Promoted);
    }

    [Fact]
    public async Task PromoteToPrincipleAsync_Promotes_WithLegitimateDistinctDomains_AfterTrimming()
    {
        var (engine, records, _, _, principleGeneralized, _, _) = CreateEngine();
        var record = TestRecords.Lesson(stage: PipelineStage.BestPractice);
        await records.InsertAsync(record, CancellationToken.None);

        var result = await engine.PromoteToPrincipleAsync(record.RecordId, [" backend ", "mobile"], CancellationToken.None);

        Assert.True(result.Promoted);
        Assert.Equal(1, principleGeneralized.CallCount);
    }

    [Fact]
    public async Task PromoteToBestPracticeAsync_DoesNotAdvanceTheStage_WhenTransitionRecordPersistenceFails()
    {
        // CodeRabbit R1 finding #8: the TransitionRecord must be persisted before the stage
        // write — a failed insert must never leave the record promoted with no evidence trail.
        var records = new InMemoryPipelineRecordStore();
        var throwingTransitions = new ThrowingOnInsertTransitionRecordStore();
        var engine = new StageEngine(
            records, throwingTransitions, new RoiGate(), new RecordingBestPracticeRatifiedEventPublisher(),
            new RecordingPrincipleGeneralizedEventPublisher(), new RecordingGoldenPathCodifiedEventPublisher(),
            new RecordingPlatformCapabilityPipelineAdvancedEventPublisher(), domainGeneralizationMinimumCount: 2);
        var record = TestRecords.Lesson(stage: PipelineStage.Pattern);
        await records.InsertAsync(record, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => engine.PromoteToBestPracticeAsync(record.RecordId, "ADR-999", "PrincipalEngineer", CancellationToken.None));

        Assert.Equal(PipelineStage.Pattern, records.Find(record.RecordId)!.Stage);
    }

    [Fact]
    public async Task PromoteToPrincipleAsync_UsesTheConfiguredMinimumDomainCount()
    {
        var (engine, records, _, _, _, _, _) = CreateEngine(domainGeneralizationMinimumCount: 3);
        var record = TestRecords.Lesson(stage: PipelineStage.BestPractice);
        await records.InsertAsync(record, CancellationToken.None);

        var result = await engine.PromoteToPrincipleAsync(record.RecordId, ["backend", "mobile"], CancellationToken.None);

        Assert.False(result.Promoted);
    }

    [Fact]
    public async Task PromoteToGoldenPathAsync_Promotes()
    {
        var (engine, records, _, _, _, goldenPathCodified, _) = CreateEngine();
        var record = TestRecords.Lesson(stage: PipelineStage.Principle);
        await records.InsertAsync(record, CancellationToken.None);

        var result = await engine.PromoteToGoldenPathAsync(record.RecordId, "tools://template/1", CancellationToken.None);

        Assert.True(result.Promoted);
        Assert.Equal(PipelineStage.GoldenPath, records.Find(record.RecordId)!.Stage);
        Assert.Equal(1, goldenPathCodified.CallCount);
    }

    [Fact]
    public async Task PromoteToAutomationAsync_NeverPromotes_RegardlessOfInputValidity()
    {
        var (engine, records, transitions, _, _, _, _) = CreateEngine();
        var record = TestRecords.Lesson(stage: PipelineStage.GoldenPath);
        await records.InsertAsync(record, CancellationToken.None);

        var result = await engine.PromoteToAutomationAsync(
            record.RecordId, new RoiEvaluationInput(50, 10, 100), CancellationToken.None);

        Assert.False(result.Promoted);
        Assert.Null(result.ResultingStage);
        Assert.Equal(PipelineStage.GoldenPath, records.Find(record.RecordId)!.Stage);
        Assert.Contains("roi_minimum", result.Reason);
        // No TransitionRecord is created for a non-promotion.
        Assert.Empty(await transitions.GetByRecordIdAsync(record.RecordId, CancellationToken.None));
    }

    [Fact]
    public async Task PromoteToAutomationAsync_StillPersistsARoiEvaluationRef_EvenThoughItNeverPromotes()
    {
        var (engine, records, _, _, _, _, _) = CreateEngine();
        var record = TestRecords.Lesson(stage: PipelineStage.GoldenPath);
        await records.InsertAsync(record, CancellationToken.None);

        await engine.PromoteToAutomationAsync(record.RecordId, new RoiEvaluationInput(50, 10, 100), CancellationToken.None);

        Assert.NotNull(records.Find(record.RecordId)!.RoiEvaluationRef);
    }

    [Fact]
    public async Task PromoteToAutomationAsync_DeniesOnInvalidInput_WithoutPromoting()
    {
        var (engine, records, _, _, _, _, _) = CreateEngine();
        var record = TestRecords.Lesson(stage: PipelineStage.GoldenPath);
        await records.InsertAsync(record, CancellationToken.None);

        var result = await engine.PromoteToAutomationAsync(
            record.RecordId, new RoiEvaluationInput(null, null, null), CancellationToken.None);

        Assert.False(result.Promoted);
        Assert.Equal(PipelineStage.GoldenPath, records.Find(record.RecordId)!.Stage);
    }

    [Fact]
    public async Task PromoteToReusableComponentAsync_IsUnreachableThroughTheFullPipeline_ButDirectlyTestable()
    {
        // Automation is never actually reached through the pipeline (PromoteToAutomationAsync
        // always denies), but this transition's own logic is real and independently correct —
        // proven here by constructing a record already at Automation directly.
        var (engine, records, _, _, _, _, _) = CreateEngine();
        var record = TestRecords.Lesson(stage: PipelineStage.Automation);
        await records.InsertAsync(record, CancellationToken.None);

        var result = await engine.PromoteToReusableComponentAsync(record.RecordId, "EOS.SDK/v1", CancellationToken.None);

        Assert.True(result.Promoted);
        Assert.Equal(PipelineStage.ReusableComponent, records.Find(record.RecordId)!.Stage);
    }

    [Fact]
    public async Task PromoteToPlatformCapabilityAsync_PublishesPlatformCapabilityPipelineAdvanced()
    {
        var (engine, records, _, _, _, _, platformCapabilityPipelineAdvanced) = CreateEngine();
        var record = TestRecords.Lesson(stage: PipelineStage.ReusableComponent);
        await records.InsertAsync(record, CancellationToken.None);

        var result = await engine.PromoteToPlatformCapabilityAsync(record.RecordId, "capability://x", CancellationToken.None);

        Assert.True(result.Promoted);
        Assert.Equal(PipelineStage.PlatformCapability, records.Find(record.RecordId)!.Stage);
        Assert.Equal(1, platformCapabilityPipelineAdvanced.CallCount);
    }

    [Fact]
    public async Task FullPipeline_Pattern_Through_GoldenPath_AdvancesCorrectly_ThenBlocksAtAutomation()
    {
        var (engine, records, transitions, _, _, _, _) = CreateEngine();
        var record = TestRecords.Lesson(stage: PipelineStage.Pattern);
        await records.InsertAsync(record, CancellationToken.None);

        await engine.PromoteToBestPracticeAsync(record.RecordId, "ADR-1", "PrincipalEngineer", CancellationToken.None);
        await engine.PromoteToPrincipleAsync(record.RecordId, ["backend", "mobile"], CancellationToken.None);
        await engine.PromoteToGoldenPathAsync(record.RecordId, "tools://template/1", CancellationToken.None);
        var automationResult = await engine.PromoteToAutomationAsync(
            record.RecordId, new RoiEvaluationInput(50, 10, 100), CancellationToken.None);

        Assert.Equal(PipelineStage.GoldenPath, records.Find(record.RecordId)!.Stage);
        Assert.False(automationResult.Promoted);
        Assert.Equal(3, (await transitions.GetByRecordIdAsync(record.RecordId, CancellationToken.None)).Count);
    }
}
