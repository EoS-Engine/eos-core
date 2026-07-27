using EOS.AIProvider;
using EOS.SDK;

namespace EOS.AIProvider.Tests;

public class AIProviderManagerEmbedTests
{
    [Fact]
    public async Task EmbedAsync_RoutesToTheHigherPriorityRankedCandidate_AndReturnsItsVector()
    {
        var higherPriorityProvider = new ProviderProfile("primary", "http://localhost:11434", 1, [new ModelProfile("nomic-embed-text", ["Embeddings"])]);
        var lowerPriorityProvider = new ProviderProfile("secondary", "http://localhost:11434", 2, [new ModelProfile("nomic-embed-text", ["Embeddings"])]);
        var registry = new ProviderRegistry([lowerPriorityProvider, higherPriorityProvider]);
        var healthMonitor = new HealthMonitor(new HealthThresholds(3, TimeSpan.FromSeconds(30)), new NoOpProviderEventLogger());
        var router = new InferenceRouter(registry, healthMonitor);
        var adapters = new Dictionary<(string, string), IAIProviderClient>();
        var expectedVector = new Vector([0.1f, 0.2f]);
        var unexpectedVector = new Vector([0.9f, 0.9f]);
        var embeddingAdapters = new Dictionary<(string, string), IEmbeddingProviderClient>
        {
            [("primary", "nomic-embed-text")] = new StubEmbeddingClient(expectedVector),
            [("secondary", "nomic-embed-text")] = new StubEmbeddingClient(unexpectedVector),
        };

        var manager = new AIProviderManager(
            router, healthMonitor, adapters, new NoOpProviderEventLogger(), embeddingAdapters, registry);

        var result = await manager.EmbedAsync("test content");

        Assert.Same(expectedVector, result);
    }

    [Fact]
    public async Task EmbedAsync_Throws_WhenNoProviderSupportsTheEmbeddingsCapability()
    {
        var provider = new ProviderProfile("ollama", "http://localhost:11434", 1, [new ModelProfile("qwen2.5-coder:7b", ["Chat"])]);
        var registry = new ProviderRegistry([provider]);
        var healthMonitor = new HealthMonitor(new HealthThresholds(3, TimeSpan.FromSeconds(30)), new NoOpProviderEventLogger());
        var router = new InferenceRouter(registry, healthMonitor);
        var adapters = new Dictionary<(string, string), IAIProviderClient>();
        var manager = new AIProviderManager(router, healthMonitor, adapters, new NoOpProviderEventLogger());

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.EmbedAsync("test content"));
    }

    private sealed class StubEmbeddingClient(Vector vector) : IEmbeddingProviderClient
    {
        public Task<Vector> EmbedAsync(string content, CancellationToken cancellationToken = default) => Task.FromResult(vector);
    }
}
