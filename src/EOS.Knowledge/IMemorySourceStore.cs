namespace EOS.Knowledge;

/// <summary>
/// Memory-Management-Specification-v1.0 §16.2/§20.1/§25's dereferencing of a <see
/// cref="MemoryRef"/> against its backing store, per ADR-015-004's Consequences:
/// "Content/Origin are not stored directly on MemoryRef itself — they must be resolved by
/// dereferencing the (MemoryType, key) pair against the appropriate backing store at
/// consolidate() call time." Per the Composition Root Adapter Pattern (ADR-015-001/ADR-015-003
/// precedent): <c>EOS.Knowledge</c> defines this small, BCL-typed interface; <c>EOS.Runner</c>'s
/// <c>Program.cs</c> supplies the concrete adapter wrapping <c>RedisMemoryStore</c>
/// (<c>EOS.Infrastructure</c>), which <c>EOS.Knowledge</c> has no legal dependency path to reach
/// directly (Constitution Part 1 §1.2).
/// </summary>
public interface IMemorySourceStore
{
    Task<string?> GetContentAsync(MemoryRef source, CancellationToken cancellationToken = default);

    Task<bool> IsConsolidatedAsync(MemoryRef source, CancellationToken cancellationToken = default);

    Task MarkConsolidatedAsync(MemoryRef source, CancellationToken cancellationToken = default);
}
