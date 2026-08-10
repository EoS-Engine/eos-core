using EOS.Contracts;

namespace EOS.Learning.Tests;

public class IntegrityCheckerTests
{
    [Fact]
    public async Task RunAsync_FindsNoViolations_ForAnUntamperedValidTransition()
    {
        var records = new InMemoryPipelineRecordStore();
        var transitions = new InMemoryTransitionRecordStore();
        var record = TestRecords.Lesson(stage: PipelineStage.BestPractice);
        await records.InsertAsync(record, CancellationToken.None);
        var occurredAt = DateTimeOffset.UtcNow;
        var transition = new TransitionRecord(
            Guid.NewGuid(), record.RecordId, PipelineStage.Pattern, PipelineStage.BestPractice, "PrincipalEngineer",
            ["adr-1"], "placeholder", occurredAt);
        var realHash = IntegrityHashCalculator.Compute(transition);
        await transitions.InsertAsync(transition with { IntegrityHash = realHash }, CancellationToken.None);
        var publisher = new RecordingDataIntegrityViolationDetectedEventPublisher();
        var checker = new IntegrityChecker(transitions, records, publisher);

        var violationCount = await checker.RunAsync(CancellationToken.None);

        Assert.Equal(0, violationCount);
        Assert.Equal(0, publisher.CallCount);
        Assert.Equal(PipelineRecordStatus.Active, records.Find(record.RecordId)!.Status);
    }

    [Fact]
    public async Task RunAsync_DetectsTampering_EmitsDataIntegrityViolationDetected_AndQuarantines()
    {
        var records = new InMemoryPipelineRecordStore();
        var transitions = new InMemoryTransitionRecordStore();
        var record = TestRecords.Lesson(stage: PipelineStage.BestPractice);
        await records.InsertAsync(record, CancellationToken.None);
        var occurredAt = DateTimeOffset.UtcNow;
        var transition = new TransitionRecord(
            Guid.NewGuid(), record.RecordId, PipelineStage.Pattern, PipelineStage.BestPractice, "PrincipalEngineer",
            ["adr-1"], IntegrityHashCalculator.Compute(
                record.RecordId, PipelineStage.Pattern, PipelineStage.BestPractice, "PrincipalEngineer", ["adr-1"], occurredAt),
            occurredAt);
        await transitions.InsertAsync(transition, CancellationToken.None);

        // Simulate tampering: the stored TriggeredBy changed after the hash was computed.
        transitions.ReplaceLast(transition with { TriggeredBy = "SomeoneElse" });

        var publisher = new RecordingDataIntegrityViolationDetectedEventPublisher();
        var checker = new IntegrityChecker(transitions, records, publisher);

        var violationCount = await checker.RunAsync(CancellationToken.None);

        Assert.Equal(1, violationCount);
        Assert.Equal(1, publisher.CallCount);
        Assert.Equal(PipelineRecordStatus.Quarantined, records.Find(record.RecordId)!.Status);
    }

    [Fact]
    public async Task RunAsync_DetectsAnInvalidEdge_EvenWithACorrectHash()
    {
        var records = new InMemoryPipelineRecordStore();
        var transitions = new InMemoryTransitionRecordStore();
        var record = TestRecords.Lesson(stage: PipelineStage.PlatformCapability);
        await records.InsertAsync(record, CancellationToken.None);
        var occurredAt = DateTimeOffset.UtcNow;
        // Lesson -> PlatformCapability is not a valid edge (skips every intermediate stage).
        var transition = new TransitionRecord(
            Guid.NewGuid(), record.RecordId, PipelineStage.Lesson, PipelineStage.PlatformCapability, "StageEngine",
            [], IntegrityHashCalculator.Compute(
                record.RecordId, PipelineStage.Lesson, PipelineStage.PlatformCapability, "StageEngine", [], occurredAt),
            occurredAt);
        await transitions.InsertAsync(transition, CancellationToken.None);

        var publisher = new RecordingDataIntegrityViolationDetectedEventPublisher();
        var checker = new IntegrityChecker(transitions, records, publisher);

        var violationCount = await checker.RunAsync(CancellationToken.None);

        Assert.Equal(1, violationCount);
        Assert.Equal(PipelineRecordStatus.Quarantined, records.Find(record.RecordId)!.Status);
    }

    [Fact]
    public async Task RunAsync_NeverSilentlyRepairsTheStoredHash()
    {
        var records = new InMemoryPipelineRecordStore();
        var transitions = new InMemoryTransitionRecordStore();
        var record = TestRecords.Lesson(stage: PipelineStage.BestPractice);
        await records.InsertAsync(record, CancellationToken.None);
        var occurredAt = DateTimeOffset.UtcNow;
        var transition = new TransitionRecord(
            Guid.NewGuid(), record.RecordId, PipelineStage.Pattern, PipelineStage.BestPractice, "PrincipalEngineer",
            ["adr-1"], "deliberately-wrong-hash", occurredAt);
        await transitions.InsertAsync(transition, CancellationToken.None);
        var checker = new IntegrityChecker(transitions, records, new RecordingDataIntegrityViolationDetectedEventPublisher());

        await checker.RunAsync(CancellationToken.None);

        var stored = (await transitions.GetByRecordIdAsync(record.RecordId, CancellationToken.None))[0];
        Assert.Equal("deliberately-wrong-hash", stored.IntegrityHash);
    }
}
