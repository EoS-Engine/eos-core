namespace EOS.Knowledge;

/// <summary>
/// Memory-Management-Specification-v1.0 §18's per-memory-type expiration table, as a pure
/// computation with no I/O. Only <see cref="MemoryType.ShortTerm"/> and
/// <see cref="MemoryType.Session"/> resolve to a real <see cref="TimeSpan"/> — the value a
/// caller passes as the <c>timeToLive</c> argument to <c>RedisMemoryStore.SetAsync</c>
/// (<c>EOS.Infrastructure</c>, already real since WP-014), whose native TTL mechanism performs
/// the actual unattended expiration. Every other <see cref="MemoryType"/> returns
/// <see langword="null"/> per §18's own table: <see cref="MemoryType.Working"/> is never
/// persisted (no TTL applies); Episodic/Semantic/Long-term never expire automatically; Project
/// is not a stored entity.
/// </summary>
public sealed class MemoryExpirationPolicy(int shortTermExpirationSeconds, int sessionIdleTimeoutSeconds)
{
    public TimeSpan? GetExpiration(MemoryType memoryType)
    {
        return memoryType switch
        {
            MemoryType.ShortTerm => TimeSpan.FromSeconds(shortTermExpirationSeconds),
            MemoryType.Session => TimeSpan.FromSeconds(sessionIdleTimeoutSeconds),
            MemoryType.Working => null,
            MemoryType.Episodic => null,
            MemoryType.Semantic => null,
            MemoryType.LongTerm => null,
            MemoryType.Project => null,
            _ => throw new ArgumentOutOfRangeException(nameof(memoryType), memoryType, "Unrecognized MemoryType."),
        };
    }
}
