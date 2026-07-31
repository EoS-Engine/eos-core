using EOS.Infrastructure;
using EOS.Knowledge;

namespace EOS.Runner.Tests;

public class MemoryExpirationIntegrationTests
{
    private static string RedisConnectionString =>
        Environment.GetEnvironmentVariable("EOS_REDIS_CONNECTION_STRING")
        ?? throw new InvalidOperationException("EOS_REDIS_CONNECTION_STRING is not set.");

    [Fact]
    public async Task ShortTermMemory_ExpiresOnSchedule_WithoutManualIntervention()
    {
        var policy = new MemoryExpirationPolicy(shortTermExpirationSeconds: 2, sessionIdleTimeoutSeconds: 1800);
        var store = new RedisMemoryStore(RedisConnectionString);
        var key = $"eos:wp016:shortterm:{Guid.NewGuid()}";
        var ttl = policy.GetExpiration(MemoryType.ShortTerm);

        await store.SetAsync(key, "short-term content", ttl);
        var beforeExpiry = await store.GetAsync(key);

        string? afterExpiry = null;
        var deadline = DateTimeOffset.UtcNow + ttl!.Value + TimeSpan.FromSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            afterExpiry = await store.GetAsync(key);
            if (afterExpiry is null)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        Assert.Equal("short-term content", beforeExpiry);
        Assert.Null(afterExpiry);
    }

    [Fact]
    public async Task SessionMemory_ExpiresOnSchedule_WithoutManualIntervention()
    {
        var policy = new MemoryExpirationPolicy(shortTermExpirationSeconds: 3600, sessionIdleTimeoutSeconds: 2);
        var store = new RedisMemoryStore(RedisConnectionString);
        var key = $"eos:wp016:session:{Guid.NewGuid()}";
        var ttl = policy.GetExpiration(MemoryType.Session);

        await store.SetAsync(key, "session content", ttl);
        var beforeExpiry = await store.GetAsync(key);

        string? afterExpiry = null;
        var deadline = DateTimeOffset.UtcNow + ttl!.Value + TimeSpan.FromSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            afterExpiry = await store.GetAsync(key);
            if (afterExpiry is null)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        Assert.Equal("session content", beforeExpiry);
        Assert.Null(afterExpiry);
    }
}
