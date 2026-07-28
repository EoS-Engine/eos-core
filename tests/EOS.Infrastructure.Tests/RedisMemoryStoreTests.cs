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

        await store.SetAsync(key, "hello", timeToLive: null);
        var result = await store.GetAsync(key);

        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task SetAsync_WithTimeToLive_ExpiresTheKey()
    {
        var store = new RedisMemoryStore(_connectionString);
        var key = $"eos:wp014:ttl:{Guid.NewGuid()}";

        await store.SetAsync(key, "hello", TimeSpan.FromMilliseconds(50));
        var beforeExpiry = await store.GetAsync(key);
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        var afterExpiry = await store.GetAsync(key);

        Assert.Equal("hello", beforeExpiry);
        Assert.Null(afterExpiry);
    }
}
