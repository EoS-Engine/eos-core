using EOS.Infrastructure;
using StackExchange.Redis;

namespace EOS.Infrastructure.Tests;

public class RedisConnectivityTests
{
    private readonly DataStoreConnectionOptions _connectionOptions;

    public RedisConnectivityTests()
    {
        EnvFileLoader.EnsureLoaded();
        _connectionOptions = DataStoreConnectionOptions.FromEnvironment();
    }

    [Fact]
    public async Task CheckRedisAsync_SucceedsAgainstTheRunningContainer()
    {
        var checker = new DataStoreHealthChecker(_connectionOptions, Path.GetTempPath());

        var result = await checker.CheckRedisAsync(CancellationToken.None);

        Assert.True(result.Healthy, result.Error);
    }

    [Fact]
    public async Task CheckRedisAsync_ReturnsUnhealthy_ForAnUnreachableEndpoint()
    {
        var invalidOptions = _connectionOptions with { RedisConnectionString = "localhost:1" };
        var checker = new DataStoreHealthChecker(invalidOptions, Path.GetTempPath());

        var result = await checker.CheckRedisAsync(CancellationToken.None);

        Assert.False(result.Healthy);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task SetGetDelete_RoundTripsOneTestKey()
    {
        await using var connection = await ConnectionMultiplexer.ConnectAsync(_connectionOptions.RedisConnectionString);
        var database = connection.GetDatabase();
        var key = $"eos:wp004:smoke-test:{Guid.NewGuid()}";

        await database.StringSetAsync(key, "hello");
        var value = await database.StringGetAsync(key);
        await database.KeyDeleteAsync(key);
        var afterDelete = await database.KeyExistsAsync(key);

        Assert.Equal("hello", value);
        Assert.False(afterDelete);
    }
}
