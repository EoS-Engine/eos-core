namespace EOS.Infrastructure.Tests;

public class SqlEventStoreTests
{
    private readonly DataStoreConnectionOptions _connectionOptions;

    public SqlEventStoreTests()
    {
        EnvFileLoader.EnsureLoaded();
        _connectionOptions = DataStoreConnectionOptions.FromEnvironment();
    }

    private static StoredEvent NewEvent(string eventType, DateTimeOffset occurredAt) => new(
        EventId: Guid.NewGuid(),
        EventType: eventType,
        Version: "v1",
        Producer: "EOS.Infrastructure.Tests",
        CorrelationId: Guid.NewGuid(),
        CausationId: null,
        OccurredAt: occurredAt,
        PayloadJson: """{"test":true}""");

    /// <summary>
    /// <see cref="SqlEventStore"/>'s own <c>EventStore</c> table is shared and never truncated
    /// (matching every other store in this codebase's own established, deliberate precedent —
    /// no test-only cleanup mechanism exists, and none is added here). Rather than relying on
    /// fabricated far-future timestamps to "outrank" whatever else the table may contain (which
    /// itself pollutes the shared table for every future test run), each test here inserts
    /// events at the real current time and requests a generously large <paramref name="count"/>
    /// (2000), then filters the result down to only the <see cref="StoredEvent.EventId"/> values
    /// it itself inserted before asserting relative order — correct regardless of how much other
    /// data the table has ever accumulated, and never leaves stale future-dated rows behind.
    /// </summary>
    private static async Task<IReadOnlyList<StoredEvent>> GetOwnRecentEventsInOrderAsync(
        SqlEventStore store, IReadOnlyCollection<Guid> ownEventIds)
    {
        var recent = await store.GetRecentAsync(2000, CancellationToken.None);
        return [.. recent.Where(e => ownEventIds.Contains(e.EventId))];
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsEventsMostRecentFirst()
    {
        var store = new SqlEventStore(_connectionOptions.SqlServerConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);

        var baseline = DateTimeOffset.UtcNow;
        var oldest = NewEvent("WP030.Test.Oldest", baseline);
        var middle = NewEvent("WP030.Test.Middle", baseline.AddSeconds(1));
        var newest = NewEvent("WP030.Test.Newest", baseline.AddSeconds(2));

        await store.AppendAsync(oldest, CancellationToken.None);
        await store.AppendAsync(middle, CancellationToken.None);
        await store.AppendAsync(newest, CancellationToken.None);

        var ownRecent = await GetOwnRecentEventsInOrderAsync(store, [oldest.EventId, middle.EventId, newest.EventId]);

        Assert.Equal(3, ownRecent.Count);
        Assert.Equal(newest.EventId, ownRecent[0].EventId);
        Assert.Equal(middle.EventId, ownRecent[1].EventId);
        Assert.Equal(oldest.EventId, ownRecent[2].EventId);
    }

    [Fact]
    public async Task GetRecentAsync_RespectsTheRequestedCount()
    {
        var store = new SqlEventStore(_connectionOptions.SqlServerConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);

        var baseline = DateTimeOffset.UtcNow;
        var first = NewEvent("WP030.Test.Count.First", baseline);
        var second = NewEvent("WP030.Test.Count.Second", baseline.AddSeconds(1));

        await store.AppendAsync(first, CancellationToken.None);
        await store.AppendAsync(second, CancellationToken.None);

        // GetRecentAsync(1) against the real, shared, ever-growing table will almost always
        // return some other, even-more-recent row inserted by other tests/processes — this test
        // only asserts the *count* contract (exactly one row back), not which row, since only
        // GetOwnRecentEventsInOrderAsync's large-count-then-filter pattern can safely identify
        // this test's own rows without assuming table isolation.
        var recent = await store.GetRecentAsync(1, CancellationToken.None);
        Assert.Single(recent);

        var ownRecent = await GetOwnRecentEventsInOrderAsync(store, [first.EventId, second.EventId]);
        Assert.Equal(2, ownRecent.Count);
        Assert.Equal(second.EventId, ownRecent[0].EventId);
        Assert.Equal(first.EventId, ownRecent[1].EventId);
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsDistinctEventTypes_WithCorrectValues()
    {
        var store = new SqlEventStore(_connectionOptions.SqlServerConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);

        var baseline = DateTimeOffset.UtcNow;
        var loopEvent = NewEvent("LoopIterationStarted", baseline);
        var modeEvent = NewEvent("OperationalModeChanged", baseline.AddSeconds(1));

        await store.AppendAsync(loopEvent, CancellationToken.None);
        await store.AppendAsync(modeEvent, CancellationToken.None);

        var ownRecent = await GetOwnRecentEventsInOrderAsync(store, [loopEvent.EventId, modeEvent.EventId]);

        Assert.Equal(2, ownRecent.Count);
        Assert.Contains(ownRecent, e => e.EventId == loopEvent.EventId && e.EventType == "LoopIterationStarted");
        Assert.Contains(ownRecent, e => e.EventId == modeEvent.EventId && e.EventType == "OperationalModeChanged");
    }
}
