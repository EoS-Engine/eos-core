namespace EOS.Learning.Tests;

public class IngestionRateGuardStoreTests
{
    [Fact]
    public async Task IncrementAndGetCountAsync_AccumulatesWithinTheSameWindow()
    {
        var store = new IngestionRateGuardStore(TestConnectionString.SqlServer);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        var role = $"role-{Guid.NewGuid()}";
        var windowStart = DateTimeOffset.UtcNow;
        var windowEnd = windowStart.AddSeconds(3600);

        var first = await store.IncrementAndGetCountAsync(role, windowStart, windowEnd, CancellationToken.None);
        var second = await store.IncrementAndGetCountAsync(role, windowStart, windowEnd, CancellationToken.None);
        var third = await store.IncrementAndGetCountAsync(role, windowStart, windowEnd, CancellationToken.None);

        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.Equal(3, third);
    }

    [Fact]
    public async Task IncrementAndGetCountAsync_TreatsADifferentWindowStartAsASeparateBucket()
    {
        var store = new IngestionRateGuardStore(TestConnectionString.SqlServer);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        var role = $"role-{Guid.NewGuid()}";
        var firstWindowStart = DateTimeOffset.UtcNow;
        var secondWindowStart = firstWindowStart.AddSeconds(3600);

        await store.IncrementAndGetCountAsync(role, firstWindowStart, firstWindowStart.AddSeconds(3600), CancellationToken.None);
        await store.IncrementAndGetCountAsync(role, firstWindowStart, firstWindowStart.AddSeconds(3600), CancellationToken.None);
        var secondWindowCount = await store.IncrementAndGetCountAsync(
            role, secondWindowStart, secondWindowStart.AddSeconds(3600), CancellationToken.None);

        Assert.Equal(1, secondWindowCount);
    }

    [Fact]
    public async Task IncrementAndGetCountAsync_PersistsAcrossFreshStoreInstances_SimulatingARestart()
    {
        var role = $"role-{Guid.NewGuid()}";
        var windowStart = DateTimeOffset.UtcNow;
        var windowEnd = windowStart.AddSeconds(3600);

        var firstProcessStore = new IngestionRateGuardStore(TestConnectionString.SqlServer);
        await firstProcessStore.EnsureTableExistsAsync(CancellationToken.None);
        await firstProcessStore.IncrementAndGetCountAsync(role, windowStart, windowEnd, CancellationToken.None);

        // A brand-new instance, as if the process had restarted — persisted state (not
        // in-memory) must be what the count resumes from.
        var afterRestartStore = new IngestionRateGuardStore(TestConnectionString.SqlServer);
        var countAfterRestart = await afterRestartStore.IncrementAndGetCountAsync(role, windowStart, windowEnd, CancellationToken.None);

        Assert.Equal(2, countAfterRestart);
    }

    // CodeRabbit PR #23 finding #1: concurrent "first events" for a brand-new
    // (ProducerRole, WindowStart) bucket must not race past each other and produce an undercount
    // or an unhandled PK violation — the final EventCount must equal the number of successful
    // increments, driven against real concurrent SQL Server connections, not simulated.
    [Fact]
    public async Task IncrementAndGetCountAsync_ProducesTheCorrectTotal_WhenCalledConcurrentlyForANewBucket()
    {
        var role = $"role-{Guid.NewGuid()}";
        var windowStart = DateTimeOffset.UtcNow;
        var windowEnd = windowStart.AddSeconds(3600);
        const int concurrentCallCount = 10;

        var stores = Enumerable.Range(0, concurrentCallCount)
            .Select(_ => new IngestionRateGuardStore(TestConnectionString.SqlServer))
            .ToArray();
        await stores[0].EnsureTableExistsAsync(CancellationToken.None);

        var counts = await Task.WhenAll(
            stores.Select(store => store.IncrementAndGetCountAsync(role, windowStart, windowEnd, CancellationToken.None)));

        Assert.Equal(concurrentCallCount, counts.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, concurrentCallCount), counts.OrderBy(count => count));
    }
}
