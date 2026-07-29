using StackExchange.Redis;

namespace EOS.Infrastructure.Tests;

public class RedisMemoryStoreTests
{
    private readonly string _connectionString;

    public RedisMemoryStoreTests()
    {
        EnvFileLoader.EnsureLoaded();
        _connectionString = DataStoreConnectionOptions.FromEnvironment().RedisConnectionString;
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_ForAnUnseenKey()
    {
        var store = new RedisMemoryStore(_connectionString);

        var result = await store.GetAsync($"eos:wp014:unseen:{Guid.NewGuid()}");

        Assert.Null(result);
    }

    [Fact]
    public async Task SetAsync_ThenGetAsync_RoundTripsTheValue()
    {
        var store = new RedisMemoryStore(_connectionString);
        var key = $"eos:wp014:roundtrip:{Guid.NewGuid()}";

        try
        {
            await store.SetAsync(key, "hello", timeToLive: null);
            var result = await store.GetAsync(key);

            Assert.Equal("hello", result);
        }
        finally
        {
            await DeleteKeyAsync(key);
        }
    }

    [Fact]
    public async Task SetAsync_WithTimeToLive_ExpiresTheKey()
    {
        var store = new RedisMemoryStore(_connectionString);
        var key = $"eos:wp014:ttl:{Guid.NewGuid()}";
        var ttl = TimeSpan.FromSeconds(2);

        try
        {
            await store.SetAsync(key, "hello", ttl);
            var beforeExpiry = await store.GetAsync(key);

            string? afterExpiry = null;
            var deadline = DateTimeOffset.UtcNow + ttl + TimeSpan.FromSeconds(5);
            while (DateTimeOffset.UtcNow < deadline)
            {
                afterExpiry = await store.GetAsync(key);
                if (afterExpiry is null)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(200));
            }

            Assert.Equal("hello", beforeExpiry);
            Assert.Null(afterExpiry);
        }
        finally
        {
            await DeleteKeyAsync(key);
        }
    }

    private async Task DeleteKeyAsync(string key)
    {
        await using var connection = await ConnectionMultiplexer.ConnectAsync(_connectionString);
        await connection.GetDatabase().KeyDeleteAsync(key);
    }
}
