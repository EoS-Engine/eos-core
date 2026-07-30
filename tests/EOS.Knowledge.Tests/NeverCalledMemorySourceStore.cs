namespace EOS.Knowledge.Tests;

internal sealed class NeverCalledMemorySourceStore : IMemorySourceStore
{
    public static readonly NeverCalledMemorySourceStore Instance = new();

    public Task<string?> GetContentAsync(MemoryRef source, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Should not be called by tests that do not exercise ConsolidateAsync.");

    public Task<bool> IsConsolidatedAsync(MemoryRef source, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Should not be called by tests that do not exercise ConsolidateAsync.");

    public Task MarkConsolidatedAsync(MemoryRef source, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Should not be called by tests that do not exercise ConsolidateAsync.");
}
