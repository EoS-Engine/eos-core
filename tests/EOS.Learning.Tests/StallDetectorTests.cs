using EOS.Contracts;

namespace EOS.Learning.Tests;

public class StallDetectorTests
{
    [Fact]
    public async Task RunAsync_MarksARecordStalled_WhenItHasNotAdvancedWithinTheWindow()
    {
        var records = new InMemoryPipelineRecordStore();
        var record = TestRecords.Lesson(stage: PipelineStage.BestPractice, status: PipelineRecordStatus.Active);
        await records.InsertAsync(record, CancellationToken.None);
        var publisher = new RecordingLessonStalledEventPublisher();
        var detector = new StallDetector(records, publisher);

        var stalledCount = await detector.RunAsync(
            record.CreatedAt.AddDays(30), TimeSpan.FromDays(28), CancellationToken.None);

        Assert.Equal(1, stalledCount);
        Assert.Equal(PipelineRecordStatus.Stalled, records.Find(record.RecordId)!.Status);
        Assert.Equal(1, publisher.CallCount);
    }

    [Fact]
    public async Task RunAsync_DoesNotStall_ARecordWithinTheWindow()
    {
        var records = new InMemoryPipelineRecordStore();
        var record = TestRecords.Lesson(stage: PipelineStage.BestPractice, status: PipelineRecordStatus.Active);
        await records.InsertAsync(record, CancellationToken.None);
        var publisher = new RecordingLessonStalledEventPublisher();
        var detector = new StallDetector(records, publisher);

        var stalledCount = await detector.RunAsync(
            record.CreatedAt.AddDays(1), TimeSpan.FromDays(28), CancellationToken.None);

        Assert.Equal(0, stalledCount);
        Assert.Equal(PipelineRecordStatus.Active, records.Find(record.RecordId)!.Status);
    }

    [Fact]
    public async Task RunAsync_OnlyExaminesActiveRecords()
    {
        var records = new InMemoryPipelineRecordStore();
        var quarantined = TestRecords.Lesson(stage: PipelineStage.BestPractice, status: PipelineRecordStatus.Quarantined);
        await records.InsertAsync(quarantined, CancellationToken.None);
        var detector = new StallDetector(records, new RecordingLessonStalledEventPublisher());

        var stalledCount = await detector.RunAsync(
            quarantined.CreatedAt.AddDays(90), TimeSpan.FromDays(28), CancellationToken.None);

        Assert.Equal(0, stalledCount);
        Assert.Equal(PipelineRecordStatus.Quarantined, records.Find(quarantined.RecordId)!.Status);
    }

    [Fact]
    public async Task RunAsync_PreservesTheRecordsStage_WhenMarkingStalled()
    {
        var records = new InMemoryPipelineRecordStore();
        var record = TestRecords.Lesson(stage: PipelineStage.GoldenPath, status: PipelineRecordStatus.Active);
        await records.InsertAsync(record, CancellationToken.None);
        var detector = new StallDetector(records, new RecordingLessonStalledEventPublisher());

        await detector.RunAsync(record.CreatedAt.AddDays(30), TimeSpan.FromDays(28), CancellationToken.None);

        Assert.Equal(PipelineStage.GoldenPath, records.Find(record.RecordId)!.Stage);
    }
}
