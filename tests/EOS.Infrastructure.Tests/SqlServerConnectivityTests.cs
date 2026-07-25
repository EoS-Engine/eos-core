using EOS.Infrastructure;

namespace EOS.Infrastructure.Tests;

public class SqlServerConnectivityTests
{
    private readonly DataStoreConnectionOptions _connectionOptions;

    public SqlServerConnectivityTests()
    {
        EnvFileLoader.EnsureLoaded();
        _connectionOptions = DataStoreConnectionOptions.FromEnvironment();
    }

    [Fact]
    public async Task CheckSqlServerAsync_SucceedsAgainstTheRunningContainer()
    {
        var checker = new DataStoreHealthChecker(_connectionOptions, Path.GetTempPath());

        var result = await checker.CheckSqlServerAsync(CancellationToken.None);

        Assert.True(result.Healthy, result.Error);
    }

    [Fact]
    public async Task CheckSqlServerAsync_ReturnsUnhealthy_ForAnInvalidConnectionString()
    {
        var invalidOptions = _connectionOptions with
        {
            SqlServerConnectionString = "Server=localhost,1433;Database=master;User Id=sa;Password=WrongPassword123!;TrustServerCertificate=True;Connect Timeout=3",
        };
        var checker = new DataStoreHealthChecker(invalidOptions, Path.GetTempPath());

        var result = await checker.CheckSqlServerAsync(CancellationToken.None);

        Assert.False(result.Healthy);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task SqlEventStore_AppendsAndReadsBackAnEventWithMatchingValues()
    {
        var store = new SqlEventStore(_connectionOptions.SqlServerConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);

        var storedEvent = new StoredEvent(
            EventId: Guid.NewGuid(),
            EventType: "WP004.SmokeTest",
            Version: "v1",
            Producer: "EOS.Infrastructure.Tests",
            CorrelationId: Guid.NewGuid(),
            CausationId: null,
            OccurredAt: DateTimeOffset.UtcNow,
            PayloadJson: """{"message":"hello"}""");

        await store.AppendAsync(storedEvent, CancellationToken.None);
        var readBack = await store.ReadByIdAsync(storedEvent.EventId, CancellationToken.None);

        Assert.NotNull(readBack);
        Assert.Equal(storedEvent.EventId, readBack.EventId);
        Assert.Equal(storedEvent.EventType, readBack.EventType);
        Assert.Equal(storedEvent.Version, readBack.Version);
        Assert.Equal(storedEvent.Producer, readBack.Producer);
        Assert.Equal(storedEvent.CorrelationId, readBack.CorrelationId);
        Assert.Equal(storedEvent.CausationId, readBack.CausationId);
        Assert.Equal(storedEvent.OccurredAt, readBack.OccurredAt);
        Assert.Equal(storedEvent.PayloadJson, readBack.PayloadJson);
    }
}
