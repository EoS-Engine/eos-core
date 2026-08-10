using EOS.Contracts;
using Microsoft.Data.SqlClient;

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
    public async Task GetByRecordIdAsync_ReturnsTransitionsInDeterministicOccurredAtOrder()
    {
        // CodeRabbit R1 finding #12: no ORDER BY previously meant arbitrary row order.
        var store = await CreateStoreAsync();
        var recordId = Guid.NewGuid();
        var earlier = DateTimeOffset.UtcNow.AddMinutes(-10);
        var later = DateTimeOffset.UtcNow;

        var laterTransition = new TransitionRecord(
            Guid.NewGuid(), recordId, PipelineStage.Principle, PipelineStage.GoldenPath, "StageEngine",
            [], IntegrityHashCalculator.Compute(recordId, PipelineStage.Principle, PipelineStage.GoldenPath, "StageEngine", [], later), later);
        var earlierTransition = new TransitionRecord(
            Guid.NewGuid(), recordId, PipelineStage.Pattern, PipelineStage.BestPractice, "PrincipalEngineer",
            [], IntegrityHashCalculator.Compute(recordId, PipelineStage.Pattern, PipelineStage.BestPractice, "PrincipalEngineer", [], earlier), earlier);

        // Inserted out of chronological order deliberately.
        await store.InsertAsync(laterTransition, CancellationToken.None);
        await store.InsertAsync(earlierTransition, CancellationToken.None);

        var persisted = await store.GetByRecordIdAsync(recordId, CancellationToken.None);

        Assert.Equal(2, persisted.Count);
        Assert.Equal(earlierTransition.TransitionId, persisted[0].TransitionId);
        Assert.Equal(laterTransition.TransitionId, persisted[1].TransitionId);
    }

    [Fact]
    public async Task GetByRecordIdAsync_UsesTransitionIdAsATieBreaker_WhenOccurredAtIsIdentical()
    {
        // CodeRabbit R2 finding #2: the prior ordering test only ever used distinct OccurredAt
        // values, so it could pass even if the TransitionId tie-breaker were removed entirely.
        // These two ids differ only in their final byte (Data4's last element), which every
        // uniqueidentifier/Guid ordering — SQL Server's and .NET's alike — compares byte-for-byte
        // with no endianness reordering, so both are guaranteed to agree on which sorts first.
        var store = await CreateStoreAsync();
        var recordId = Guid.NewGuid();
        var sharedOccurredAt = DateTimeOffset.UtcNow;
        var smallerTransitionId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var largerTransitionId = Guid.Parse("00000000-0000-0000-0000-000000000002");

        var largerTransition = new TransitionRecord(
            largerTransitionId, recordId, PipelineStage.Principle, PipelineStage.GoldenPath, "StageEngine",
            [], IntegrityHashCalculator.Compute(
                recordId, PipelineStage.Principle, PipelineStage.GoldenPath, "StageEngine", [], sharedOccurredAt), sharedOccurredAt);
        var smallerTransition = new TransitionRecord(
            smallerTransitionId, recordId, PipelineStage.Pattern, PipelineStage.BestPractice, "PrincipalEngineer",
            [], IntegrityHashCalculator.Compute(
                recordId, PipelineStage.Pattern, PipelineStage.BestPractice, "PrincipalEngineer", [], sharedOccurredAt), sharedOccurredAt);

        try
        {
            // Inserted in descending TransitionId order deliberately.
            await store.InsertAsync(largerTransition, CancellationToken.None);
            await store.InsertAsync(smallerTransition, CancellationToken.None);

            var persisted = await store.GetByRecordIdAsync(recordId, CancellationToken.None);

            Assert.Equal(2, persisted.Count);
            Assert.Equal(smallerTransitionId, persisted[0].TransitionId);
            Assert.Equal(largerTransitionId, persisted[1].TransitionId);
        }
        finally
        {
            // Fixed TransitionIds (needed for a cross-system-guaranteed sort order — both SQL
            // Server's and .NET's uniqueidentifier/Guid comparers agree on Data4's trailing byte)
            // must not leak into the shared database and collide with a later run.
            await using var connection = new SqlConnection(TestConnectionString.SqlServer);
            await connection.OpenAsync(CancellationToken.None);
            await using var deleteCommand = connection.CreateCommand();
            deleteCommand.CommandText = "DELETE FROM TransitionRecord WHERE TransitionId IN (@Smaller, @Larger)";
            deleteCommand.Parameters.AddWithValue("@Smaller", smallerTransitionId);
            deleteCommand.Parameters.AddWithValue("@Larger", largerTransitionId);
            await deleteCommand.ExecuteNonQueryAsync(CancellationToken.None);
        }
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
