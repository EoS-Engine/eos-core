namespace EOS.Learning.Tests;

public class PipelineStageStoreAdapterTests
{
    [Fact]
    public async Task HasReachedPatternStageOrBeyondAsync_ReturnsFalse_BeforePromotion()
    {
        var store = new InMemoryPipelineRecordStore();
        var episodicEntryId = Guid.NewGuid();
        await store.InsertAsync(TestRecords.Lesson(sourceLessonIds: [episodicEntryId], stage: EOS.Contracts.PipelineStage.Lesson), CancellationToken.None);
        var adapter = new PipelineStageStoreAdapter(store);

        var result = await adapter.HasReachedPatternStageOrBeyondAsync(episodicEntryId, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task HasReachedPatternStageOrBeyondAsync_ReturnsTrue_AfterPromotion()
    {
        var store = new InMemoryPipelineRecordStore();
        var episodicEntryId = Guid.NewGuid();
        var record = TestRecords.Lesson(sourceLessonIds: [episodicEntryId], stage: EOS.Contracts.PipelineStage.Lesson);
        await store.InsertAsync(record, CancellationToken.None);
        await store.UpdateStageAsync(record.RecordId, EOS.Contracts.PipelineStage.Pattern, EOS.Contracts.PipelineRecordStatus.Active, 0.9, CancellationToken.None);
        var adapter = new PipelineStageStoreAdapter(store);

        var result = await adapter.HasReachedPatternStageOrBeyondAsync(episodicEntryId, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task HasReachedPatternStageOrBeyondAsync_ReturnsFalse_WhenNoRecordExistsForTheEpisodicEntry()
    {
        var store = new InMemoryPipelineRecordStore();
        var adapter = new PipelineStageStoreAdapter(store);

        var result = await adapter.HasReachedPatternStageOrBeyondAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result);
    }
}
