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

    // CodeRabbit PR #23 finding #3: UpdateStageAsync must treat zero affected rows as a
    // persistence failure, not silent success — a promotion targeting a RecordId that no longer
    // resolves to a row must never be indistinguishable from a genuinely persisted promotion,
    // since ClusterTrigger only publishes LessonPromoted after this call returns successfully.
    [Fact]
    public async Task UpdateStageAsync_ThrowsInvalidOperationException_WhenRecordIdDoesNotExist()
    {
        var store = await CreateStoreAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.UpdateStageAsync(Guid.NewGuid(), PipelineStage.Pattern, PipelineRecordStatus.Active, 0.9, CancellationToken.None));
    }

    // CodeRabbit PR #23 finding #2: KnowledgeGraphRef is semantically 1:1 with the originating
    // Lesson (Ingestion.NewRecord sets it to the LessonLearned episodicEntryId, and
    // UpdateStageAsync never mutates it afterward) — the UNIQUE index added to
    // EnsureTableExistsAsync backstops the application-level idempotency check in
    // Ingestion.OnLessonLearnedAsync as defense-in-depth.
    [Fact]
    public async Task InsertAsync_ThrowsOnDuplicateKnowledgeGraphRef()
    {
        var store = await CreateStoreAsync();
        var knowledgeGraphRef = Guid.NewGuid();
        await store.InsertAsync(TestRecords.Lesson(knowledgeGraphRef: knowledgeGraphRef), CancellationToken.None);

        await Assert.ThrowsAnyAsync<Exception>(
            () => store.InsertAsync(TestRecords.Lesson(knowledgeGraphRef: knowledgeGraphRef), CancellationToken.None));
    }

    // CodeRabbit PR #23 finding #4: GetByKnowledgeGraphRefsAsync must return [] for an empty
    // input rather than issuing a malformed "WHERE KnowledgeGraphRef IN ()" statement.
    [Fact]
    public async Task GetByKnowledgeGraphRefsAsync_ReturnsEmpty_ForEmptyInput()
    {
        var store = await CreateStoreAsync();

        var resolved = await store.GetByKnowledgeGraphRefsAsync([], CancellationToken.None);

        Assert.Empty(resolved);
    }
}
