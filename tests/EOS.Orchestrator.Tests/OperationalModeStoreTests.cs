using EOS.Contracts;
using Microsoft.Data.SqlClient;

namespace EOS.Orchestrator.Tests;

public class OperationalModeStoreTests
{
    private static async Task<OperationalModeStore> CreateStoreAsync()
    {
        var store = new OperationalModeStore(TestConnectionString.SqlServer);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        await ResetTableAsync();
        return store;
    }

    // §19.2's single current-value row has no natural per-test isolation key (unlike
    // LoopIteration's Guid-per-row); each test explicitly resets the row first so it observes
    // only its own writes, matching TransitionRecordStoreTests' own DELETE-FROM precedent.
    private static async Task ResetTableAsync()
    {
        await using var connection = new SqlConnection(TestConnectionString.SqlServer);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM OperationalModeState";
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task GetCurrentModeAsync_ReturnsAssisted_WhenNoModeHasEverBeenPersisted()
    {
        var store = await CreateStoreAsync();

        var mode = await store.GetCurrentModeAsync(CancellationToken.None);

        // §22.2: Assisted is the Loop's default mode in the absence of an explicit selection.
        Assert.Equal(OperationalMode.Assisted, mode);
    }

    [Fact]
    public async Task SetCurrentModeAsync_ThenGetCurrentModeAsync_RoundTripsTheValue()
    {
        var store = await CreateStoreAsync();

        await store.SetCurrentModeAsync(OperationalMode.Autonomous, CancellationToken.None);
        var mode = await store.GetCurrentModeAsync(CancellationToken.None);

        Assert.Equal(OperationalMode.Autonomous, mode);
    }

    [Fact]
    public async Task SetCurrentModeAsync_CalledTwice_OverwritesThePreviousValue()
    {
        var store = await CreateStoreAsync();

        await store.SetCurrentModeAsync(OperationalMode.Safe, CancellationToken.None);
        await store.SetCurrentModeAsync(OperationalMode.Recovery, CancellationToken.None);
        var mode = await store.GetCurrentModeAsync(CancellationToken.None);

        Assert.Equal(OperationalMode.Recovery, mode);
    }

    [Theory]
    [InlineData(OperationalMode.Manual)]
    [InlineData(OperationalMode.Assisted)]
    [InlineData(OperationalMode.SemiAutonomous)]
    [InlineData(OperationalMode.Autonomous)]
    [InlineData(OperationalMode.Safe)]
    [InlineData(OperationalMode.Recovery)]
    [InlineData(OperationalMode.Learning)]
    [InlineData(OperationalMode.Maintenance)]
    public async Task SetCurrentModeAsync_RoundTripsEveryOperationalModeValue(OperationalMode mode)
    {
        var store = await CreateStoreAsync();

        await store.SetCurrentModeAsync(mode, CancellationToken.None);
        var persisted = await store.GetCurrentModeAsync(CancellationToken.None);

        Assert.Equal(mode, persisted);
    }

    [Fact]
    public async Task SetCurrentModeAsync_ReturnsThePreviousMode_AtomicallyWithTheWrite()
    {
        // CodeRabbit pre-merge P1 finding #2: the previous mode must come from the same atomic
        // write, never a separately-read (and therefore possibly stale) value.
        var store = await CreateStoreAsync();

        var firstPrevious = await store.SetCurrentModeAsync(OperationalMode.Autonomous, CancellationToken.None);
        var secondPrevious = await store.SetCurrentModeAsync(OperationalMode.Safe, CancellationToken.None);

        Assert.Equal(OperationalMode.Assisted, firstPrevious); // §22.2 default, before any row exists.
        Assert.Equal(OperationalMode.Autonomous, secondPrevious);
    }

    [Fact]
    public async Task SetCurrentModeAsync_ConcurrentCalls_EachReturnsTheAuthoritativePreviousMode()
    {
        // Genuine overlap (Task.WhenAll) — proves the UPDATE ... OUTPUT mechanism sources each
        // call's returned previous-mode value from the database's own prior row state at the
        // moment of that call's atomic write, never from a separately-read value that could be
        // stale under a real race. Non-brittle: regardless of which call's write the database
        // actually serializes first, exactly one of the two must observe the true initial default
        // (Assisted) and the other must observe whichever mode the first call wrote.
        var store = await CreateStoreAsync();

        var autonomousTask = store.SetCurrentModeAsync(OperationalMode.Autonomous, CancellationToken.None);
        var safeTask = store.SetCurrentModeAsync(OperationalMode.Safe, CancellationToken.None);
        await Task.WhenAll(autonomousTask, safeTask);

        var autonomousPrevious = await autonomousTask;
        var safePrevious = await safeTask;

        var validOutcome =
            (autonomousPrevious == OperationalMode.Assisted && safePrevious == OperationalMode.Autonomous)
            || (safePrevious == OperationalMode.Assisted && autonomousPrevious == OperationalMode.Safe);
        Assert.True(
            validOutcome,
            $"Unexpected previous-mode pair: autonomousPrevious={autonomousPrevious}, safePrevious={safePrevious}");
    }

    [Fact]
    public async Task SetCurrentModeAsync_ConcurrentCalls_LeaveTheStoreInADeterministicSingleValuedState()
    {
        var store = await CreateStoreAsync();

        // Genuine overlap (Task.WhenAll), not sequential — proves the UPDATE-then-INSERT-if-absent
        // upsert never leaves two rows or throws an unhandled duplicate-key exception under a real
        // race, matching WP-028's own concurrency-verification precedent.
        await Task.WhenAll(
            store.SetCurrentModeAsync(OperationalMode.Autonomous, CancellationToken.None),
            store.SetCurrentModeAsync(OperationalMode.Safe, CancellationToken.None));

        var mode = await store.GetCurrentModeAsync(CancellationToken.None);
        Assert.True(mode is OperationalMode.Autonomous or OperationalMode.Safe);
    }
}
