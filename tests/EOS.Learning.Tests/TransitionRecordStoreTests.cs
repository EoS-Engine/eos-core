using EOS.Contracts;

namespace EOS.Learning.Tests;

public class TransitionRecordStoreTests
{
    private static async Task<TransitionRecordStore> CreateStoreAsync()
    {
        var store = new TransitionRecordStore(TestConnectionString.SqlServer);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        return store;
    }

    [Fact]
    public async Task InsertAsync_ThenGetByRecordIdAsync_RoundTripsTheRecord()
    {
        var store = await CreateStoreAsync();
        var recordId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;
        var transition = new TransitionRecord(
            Guid.NewGuid(), recordId, PipelineStage.Pattern, PipelineStage.BestPractice, "PrincipalEngineer",
            ["adr-1", "adr-2"], IntegrityHashCalculator.Compute(
                recordId, PipelineStage.Pattern, PipelineStage.BestPractice, "PrincipalEngineer", ["adr-1", "adr-2"], occurredAt),
            occurredAt);

        await store.InsertAsync(transition, CancellationToken.None);

        var persisted = await store.GetByRecordIdAsync(recordId, CancellationToken.None);
        Assert.Single(persisted);
        Assert.Equal(transition.TransitionId, persisted[0].TransitionId);
        Assert.Equal(transition.FromStage, persisted[0].FromStage);
        Assert.Equal(transition.ToStage, persisted[0].ToStage);
        Assert.Equal(transition.TriggeredBy, persisted[0].TriggeredBy);
        Assert.Equal(transition.EvidenceRefs, persisted[0].EvidenceRefs);
        Assert.Equal(transition.IntegrityHash, persisted[0].IntegrityHash);
    }

    [Fact]
    public async Task GetByRecordIdAsync_ReturnsEmpty_WhenNoTransitionsExist()
    {
        var store = await CreateStoreAsync();

        var result = await store.GetByRecordIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAllAsync_IncludesTransitionsAcrossDifferentRecords()
    {
        var store = await CreateStoreAsync();
        var firstRecordId = Guid.NewGuid();
        var secondRecordId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;
        await store.InsertAsync(
            new TransitionRecord(Guid.NewGuid(), firstRecordId, PipelineStage.Pattern, PipelineStage.BestPractice, "x", [], "h1", occurredAt),
            CancellationToken.None);
        await store.InsertAsync(
            new TransitionRecord(Guid.NewGuid(), secondRecordId, PipelineStage.Pattern, PipelineStage.BestPractice, "x", [], "h2", occurredAt),
            CancellationToken.None);

        var all = await store.GetAllAsync(CancellationToken.None);

        Assert.Contains(all, t => t.RecordId == firstRecordId);
        Assert.Contains(all, t => t.RecordId == secondRecordId);
    }

    [Fact]
    public async Task InsertAsync_PersistsTheExactStoredHash_ForVerificationLater()
    {
        var store = await CreateStoreAsync();
        var recordId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;
        var transition = new TransitionRecord(
            Guid.NewGuid(), recordId, PipelineStage.Principle, PipelineStage.GoldenPath, "StageEngine",
            ["tools://template/1"], IntegrityHashCalculator.Compute(
                recordId, PipelineStage.Principle, PipelineStage.GoldenPath, "StageEngine", ["tools://template/1"], occurredAt),
            occurredAt);
        await store.InsertAsync(transition, CancellationToken.None);

        var persisted = (await store.GetByRecordIdAsync(recordId, CancellationToken.None))[0];

        // IntegrityChecker's own recomputation must match what was actually stored.
        Assert.Equal(IntegrityHashCalculator.Compute(persisted), persisted.IntegrityHash);
    }
}
