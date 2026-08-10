using EOS.Contracts;

namespace EOS.Learning.Tests;

public class QuarantineClearingServiceTests
{
    [Fact]
    public async Task ClearAsync_RestoresTheRecordsOwnActualStage_AndEmitsLessonQuarantineCleared()
    {
        var records = new InMemoryPipelineRecordStore();
        var record = TestRecords.Lesson(stage: PipelineStage.BestPractice, status: PipelineRecordStatus.Quarantined);
        await records.InsertAsync(record, CancellationToken.None);
        var publisher = new RecordingLessonQuarantineClearedEventPublisher();
        var service = new QuarantineClearingService(records, publisher);

        await service.ClearAsync(record.RecordId, "PrincipalEngineer", "false positive", CancellationToken.None);

        var updated = records.Find(record.RecordId)!;
        Assert.Equal(PipelineRecordStatus.Active, updated.Status);
        Assert.Equal(PipelineStage.BestPractice, updated.Stage);
        Assert.Equal(1, publisher.CallCount);
        Assert.Equal(record.RecordId, publisher.LastRecordId);
        Assert.Equal("PrincipalEngineer", publisher.LastClearingRole);
        Assert.Equal("false positive", publisher.LastJustification);
    }

    [Theory]
    [InlineData(PipelineStage.Pattern)]
    [InlineData(PipelineStage.Principle)]
    [InlineData(PipelineStage.GoldenPath)]
    [InlineData(PipelineStage.PlatformCapability)]
    public async Task ClearAsync_AlwaysRestoresTheRecordsOwnStage_RegardlessOfWhatItWasBeforeQuarantine(PipelineStage actualStageAtQuarantineTime)
    {
        // ClearAsync has no priorStage parameter at all — this proves, for several different
        // actual stages, that the record is always restored to exactly its own Stage field
        // (the only value Quarantine ever leaves behind, per IntegrityChecker/Ingestion's own
        // precedent of never touching Stage when quarantining) — a caller has no way to make it
        // land anywhere else.
        var records = new InMemoryPipelineRecordStore();
        var record = TestRecords.Lesson(stage: actualStageAtQuarantineTime, status: PipelineRecordStatus.Quarantined);
        await records.InsertAsync(record, CancellationToken.None);
        var service = new QuarantineClearingService(records, new RecordingLessonQuarantineClearedEventPublisher());

        await service.ClearAsync(record.RecordId, "PrincipalEngineer", "false positive", CancellationToken.None);

        Assert.Equal(actualStageAtQuarantineTime, records.Find(record.RecordId)!.Stage);
    }

    [Fact]
    public async Task ClearAsync_Throws_WhenRecordIsNotQuarantined()
    {
        var records = new InMemoryPipelineRecordStore();
        var record = TestRecords.Lesson(stage: PipelineStage.BestPractice, status: PipelineRecordStatus.Active);
        await records.InsertAsync(record, CancellationToken.None);
        var service = new QuarantineClearingService(records, new RecordingLessonQuarantineClearedEventPublisher());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ClearAsync(record.RecordId, "PrincipalEngineer", "reason", CancellationToken.None));
    }

    [Theory]
    [InlineData("", "justification")]
    [InlineData("   ", "justification")]
    [InlineData("PrincipalEngineer", "")]
    [InlineData("PrincipalEngineer", "   ")]
    public async Task ClearAsync_Throws_WhenClearingRoleOrJustificationIsMissing(string clearingRole, string justification)
    {
        var records = new InMemoryPipelineRecordStore();
        var record = TestRecords.Lesson(stage: PipelineStage.BestPractice, status: PipelineRecordStatus.Quarantined);
        await records.InsertAsync(record, CancellationToken.None);
        var service = new QuarantineClearingService(records, new RecordingLessonQuarantineClearedEventPublisher());

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ClearAsync(record.RecordId, clearingRole, justification, CancellationToken.None));
    }

    [Fact]
    public async Task ClearAsync_NeverFiresAutonomously_RequiresAnExplicitCall()
    {
        // There is no code path anywhere that calls QuarantineClearingService except an explicit
        // test/caller invocation — this test documents that invariant by construction: the
        // publisher only ever records a call count that matches explicit ClearAsync calls, never
        // more.
        var records = new InMemoryPipelineRecordStore();
        var record = TestRecords.Lesson(stage: PipelineStage.BestPractice, status: PipelineRecordStatus.Quarantined);
        await records.InsertAsync(record, CancellationToken.None);
        var publisher = new RecordingLessonQuarantineClearedEventPublisher();
        var service = new QuarantineClearingService(records, publisher);

        Assert.Equal(0, publisher.CallCount);
        await service.ClearAsync(record.RecordId, "PrincipalEngineer", "reason", CancellationToken.None);
        Assert.Equal(1, publisher.CallCount);
    }

    [Fact]
    public async Task ArchiveAsync_ArchivesAQuarantinedRecord()
    {
        var records = new InMemoryPipelineRecordStore();
        var record = TestRecords.Lesson(stage: PipelineStage.BestPractice, status: PipelineRecordStatus.Quarantined);
        await records.InsertAsync(record, CancellationToken.None);
        var service = new QuarantineClearingService(records, new RecordingLessonQuarantineClearedEventPublisher());

        await service.ArchiveAsync(record.RecordId, "PrincipalEngineer", "confirmed poisoning", CancellationToken.None);

        Assert.Equal(PipelineRecordStatus.Archived, records.Find(record.RecordId)!.Status);
    }

    [Fact]
    public async Task ArchiveAsync_Throws_WhenRecordIsNotQuarantined()
    {
        var records = new InMemoryPipelineRecordStore();
        var record = TestRecords.Lesson(stage: PipelineStage.BestPractice, status: PipelineRecordStatus.Active);
        await records.InsertAsync(record, CancellationToken.None);
        var service = new QuarantineClearingService(records, new RecordingLessonQuarantineClearedEventPublisher());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ArchiveAsync(record.RecordId, "PrincipalEngineer", "reason", CancellationToken.None));
    }
}
