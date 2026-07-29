using StackExchange.Redis;

namespace EOS.Infrastructure;

/// <summary>
/// Minimal Redis key-value wrapper realizing the Working/Short-term/Session Memory storage
/// strategy (Memory-Management-Specification-v1.0 §10.1/§10.2/§10.7, §12). This class has no
/// production caller in WP-014 — its retrieval surface belongs to future runtime components
/// that own ephemeral execution state (Reasoning/Orchestrator/Context Assembly), not to
/// <c>IKnowledgeClient</c> (documented architectural limitation, WP-014). It is real,
/// independently tested infrastructure, matching the same "built ahead of its production
/// caller" precedent as <c>AIProviderManager.EmbedAsync</c> (WP-011).
/// </summary>
public sealed class RedisMemoryStore(string connectionString)
{
    public async Task SetAsync(
        string key, string value, TimeSpan? timeToLive, CancellationToken cancellationToken = default)
    {
        await using var connection = await ConnectionMultiplexer.ConnectAsync(connectionString).WaitAsync(cancellationToken);
        var database = connection.GetDatabase();

        if (timeToLive.HasValue)
        {
            await database.StringSetAsync(key, value, timeToLive.Value).WaitAsync(cancellationToken);
        }
        else
        {
            await database.StringSetAsync(key, value).WaitAsync(cancellationToken);
        }
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var connection = await ConnectionMultiplexer.ConnectAsync(connectionString).WaitAsync(cancellationToken);
        var value = await connection.GetDatabase().StringGetAsync(key).WaitAsync(cancellationToken);
        return value.HasValue ? value.ToString() : null;
    }
}
