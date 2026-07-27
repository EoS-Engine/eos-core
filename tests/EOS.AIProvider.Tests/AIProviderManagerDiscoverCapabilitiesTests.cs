using EOS.AIProvider;
using EOS.SDK;

namespace EOS.AIProvider.Tests;

public class AIProviderManagerDiscoverCapabilitiesTests
{
    private static AIProviderManager CreateManager(ProviderRegistry registry)
    {
        var healthMonitor = new HealthMonitor(new HealthThresholds(3, TimeSpan.FromSeconds(30)), new NoOpProviderEventLogger());
        var router = new InferenceRouter(registry, healthMonitor);
        var adapters = new Dictionary<(string, string), IAIProviderClient>();

        return new AIProviderManager(router, healthMonitor, adapters, new NoOpProviderEventLogger(), providerRegistry: registry);
    }

    [Fact]
    public void DiscoverCapabilities_ReturnsAllEntries_WhenFilterIsNull()
    {
        var provider = new ProviderProfile("ollama", "http://localhost:11434", 1, [
            new ModelProfile("qwen2.5-coder:7b", ["Chat"]),
            new ModelProfile("nomic-embed-text", ["Embeddings"]),
        ]);
        var manager = CreateManager(new ProviderRegistry([provider]));

        var result = manager.DiscoverCapabilities(null);

        Assert.Equal(2, result.Entries.Count);
    }

    [Fact]
    public void DiscoverCapabilities_FiltersByCapability_WhenFilterIsProvided()
    {
        var provider = new ProviderProfile("ollama", "http://localhost:11434", 1, [
            new ModelProfile("qwen2.5-coder:7b", ["Chat"]),
            new ModelProfile("nomic-embed-text", ["Embeddings"]),
        ]);
        var manager = CreateManager(new ProviderRegistry([provider]));

        var result = manager.DiscoverCapabilities("Embeddings");

        var entry = Assert.Single(result.Entries);
        Assert.Equal("nomic-embed-text", entry.ModelName);
    }

    [Fact]
    public void DiscoverCapabilities_ReturnsEmpty_WhenNoRegistryWasProvided()
    {
        var healthMonitor = new HealthMonitor(new HealthThresholds(3, TimeSpan.FromSeconds(30)), new NoOpProviderEventLogger());
        var router = new InferenceRouter(new ProviderRegistry([]), healthMonitor);
        var adapters = new Dictionary<(string, string), IAIProviderClient>();
        var manager = new AIProviderManager(router, healthMonitor, adapters, new NoOpProviderEventLogger());

        var result = manager.DiscoverCapabilities(null);

        Assert.Empty(result.Entries);
    }
}
