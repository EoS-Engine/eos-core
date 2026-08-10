using EOS.Contracts;

namespace EOS.Learning.Tests;

public class PipelineRecordStoreTests
{
    private static async Task<PipelineRecordStore> CreateStoreAsync()
    {
        var store = new PipelineRecordStore(TestConnectionString.SqlServer);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        return store;
    }

    [Fact]
    public async Task InsertAsync_ThenGetBySourceLessonIdAsync_RoundTripsTheRecord()
    {
        var store = await CreateStoreAsync();
        var episodicEntryId = Guid.NewGuid();
        var record = TestRecords.Lesson(sourceLessonIds: [episodicEntryId], domainTags: ["backend", "logging"]);
        await store.InsertAsync(record, CancellationToken.None);

        var persisted = await store.GetBySourceLessonIdAsync(episodicEntryId, CancellationToken.None);

        Assert.NotNull(persisted);
        Assert.Equal(record.RecordId, persisted.RecordId);
        Assert.Equal(record.KnowledgeGraphRef, persisted.KnowledgeGraphRef);
        Assert.Equal(record.SourceLessonIds, persisted.SourceLessonIds);
        Assert.Equal(record.DomainTags, persisted.DomainTags);
        Assert.Equal(PipelineStage.Lesson, persisted.Stage);
        Assert.Equal(PipelineRecordStatus.Active, persisted.Status);
    }

    [Fact]
    public async Task GetBySourceLessonIdAsync_ReturnsNull_WhenNoRecordExists()
    {
        var store = await CreateStoreAsync();

        var result = await store.GetBySourceLessonIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByKnowledgeGraphRefsAsync_ResolvesOnlyMatchingRecords()
    {
        var store = await CreateStoreAsync();
        var matching = TestRecords.Lesson();
        var nonMatching = TestRecords.Lesson();
        await store.InsertAsync(matching, CancellationToken.None);
        await store.InsertAsync(nonMatching, CancellationToken.None);

        var resolved = await store.GetByKnowledgeGraphRefsAsync([matching.KnowledgeGraphRef, Guid.NewGuid()], CancellationToken.None);

        Assert.Contains(resolved, r => r.RecordId == matching.RecordId);
        Assert.DoesNotContain(resolved, r => r.RecordId == nonMatching.RecordId);
    }

    [Fact]
    public async Task UpdateStageAsync_PromotesToPattern_AndPersistsConfidence()
    {
        var store = await CreateStoreAsync();
        var record = TestRecords.Lesson();
        await store.InsertAsync(record, CancellationToken.None);

        await store.UpdateStageAsync(record.RecordId, PipelineStage.Pattern, PipelineRecordStatus.Active, 0.75, CancellationToken.None);

        var persisted = await store.GetBySourceLessonIdAsync(record.SourceLessonIds[0], CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal(PipelineStage.Pattern, persisted.Stage);
        Assert.Equal(0.75, persisted.ConfidenceScore, precision: 10);
    }

    [Fact]
    public async Task UpdateStageAsync_CanQuarantineARecord_WithoutChangingItsStage()
    {
        var store = await CreateStoreAsync();
        var record = TestRecords.Lesson();
        await store.InsertAsync(record, CancellationToken.None);

        await store.UpdateStageAsync(record.RecordId, PipelineStage.Lesson, PipelineRecordStatus.Quarantined, 0.0, CancellationToken.None);

        var persisted = await store.GetBySourceLessonIdAsync(record.SourceLessonIds[0], CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal(PipelineStage.Lesson, persisted.Stage);
        Assert.Equal(PipelineRecordStatus.Quarantined, persisted.Status);
    }
}
