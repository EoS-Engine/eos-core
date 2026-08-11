namespace EOS.Orchestrator.Tests;

public class LoopIterationStoreTests
{
    private static async Task<LoopIterationStore> CreateStoreAsync()
    {
        var store = new LoopIterationStore(TestConnectionString.SqlServer);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        return store;
    }

    private static LoopIteration NewIteration(string triggerSource = "UserRequest", int entryStep = 2) => new(
        IterationId: Guid.NewGuid(),
        TriggerSource: triggerSource,
        EntryStep: entryStep,
        State: "Triggered",
        StepsTraversed: [],
        Outcome: null,
        StartedAt: DateTimeOffset.UtcNow,
        CompletedAt: null);

    [Fact]
    public async Task InsertAsync_ThenGetByIdAsync_RoundTripsTheRecord()
    {
        var store = await CreateStoreAsync();
        var iteration = NewIteration();

        await store.InsertAsync(iteration, CancellationToken.None);

        var persisted = await store.GetByIdAsync(iteration.IterationId, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal(iteration.TriggerSource, persisted!.TriggerSource);
        Assert.Equal(iteration.EntryStep, persisted.EntryStep);
        Assert.Equal("Triggered", persisted.State);
        Assert.Empty(persisted.StepsTraversed);
        Assert.Null(persisted.Outcome);
        Assert.Null(persisted.CompletedAt);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNoIterationExists()
    {
        var store = await CreateStoreAsync();

        var result = await store.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateStateAsync_UpdatesStateAndStepsTraversed()
    {
        var store = await CreateStoreAsync();
        var iteration = NewIteration();
        await store.InsertAsync(iteration, CancellationToken.None);

        await store.UpdateStateAsync(iteration.IterationId, "Deciding", [2, 3, 4], CancellationToken.None);

        var persisted = await store.GetByIdAsync(iteration.IterationId, CancellationToken.None);
        Assert.Equal("Deciding", persisted!.State);
        Assert.Equal([2, 3, 4], persisted.StepsTraversed);
    }

    [Fact]
    public async Task UpdateStateAsync_Throws_WhenIterationDoesNotExist()
    {
        var store = await CreateStoreAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.UpdateStateAsync(Guid.NewGuid(), "Deciding", [2], CancellationToken.None));
    }

    [Fact]
    public async Task CompleteAsync_SetsStateOutcomeAndCompletedAt_ForTheSuccessfulTerminalCase()
    {
        var store = await CreateStoreAsync();
        var iteration = NewIteration();
        await store.InsertAsync(iteration, CancellationToken.None);

        await store.CompleteAsync(
            iteration.IterationId, "Completed", "Completed", [2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15], CancellationToken.None);

        var persisted = await store.GetByIdAsync(iteration.IterationId, CancellationToken.None);
        Assert.Equal("Completed", persisted!.State);
        Assert.Equal("Completed", persisted.Outcome);
        Assert.Equal([2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15], persisted.StepsTraversed);
        Assert.NotNull(persisted.CompletedAt);
    }

    [Fact]
    public async Task CompleteAsync_SetsStateOutcomeAndCompletedAt_ForTheFailedTerminalCase()
    {
        // CodeRabbit R1 finding #2: prior to the fix, no store method could persist State =
        // "Failed" together with Outcome and CompletedAt in one write — this proves the unified
        // terminal operation now can.
        var store = await CreateStoreAsync();
        var iteration = NewIteration();
        await store.InsertAsync(iteration, CancellationToken.None);

        await store.CompleteAsync(iteration.IterationId, "Failed", "Failed", [2, 3, 4], CancellationToken.None);

        var persisted = await store.GetByIdAsync(iteration.IterationId, CancellationToken.None);
        Assert.Equal("Failed", persisted!.State);
        Assert.Equal("Failed", persisted.Outcome);
        Assert.Equal([2, 3, 4], persisted.StepsTraversed);
        Assert.NotNull(persisted.CompletedAt);
    }

    [Fact]
    public async Task CompleteAsync_Throws_WhenIterationDoesNotExist()
    {
        var store = await CreateStoreAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.CompleteAsync(Guid.NewGuid(), "Completed", "Completed", [2], CancellationToken.None));
    }

    [Fact]
    public async Task GetLatestAsync_ReturnsTheMostRecentlyStartedIteration()
    {
        var store = await CreateStoreAsync();
        var earlier = NewIteration() with { StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10) };
        var later = NewIteration() with { StartedAt = DateTimeOffset.UtcNow };

        // Inserted out of chronological order deliberately.
        await store.InsertAsync(later, CancellationToken.None);
        await store.InsertAsync(earlier, CancellationToken.None);

        var latest = await store.GetLatestAsync(CancellationToken.None);

        Assert.NotNull(latest);
        Assert.Equal(later.IterationId, latest!.IterationId);
    }
}
