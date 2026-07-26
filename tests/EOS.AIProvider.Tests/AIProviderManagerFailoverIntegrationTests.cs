using EOS.AIProvider;
using EOS.SDK;

namespace EOS.AIProvider.Tests;

public class AIProviderManagerFailoverIntegrationTests
{
    private static InferenceRequest CreateRequest()
    {
        return new InferenceRequest(
            RequestId: Guid.NewGuid(),
            CorrelationId: Guid.NewGuid(),
            CapabilityRequired: "Chat",
            Payload: "Reply with exactly the word OK and nothing else.",
            ContextPayloadRef: null,
            TokenBudgetEstimate: 8,
            Priority: 0,
            Caller: "EOS.Reasoning");
    }

    [Fact]
    public async Task InferAsync_FailsOverToTheNextRankedCandidate_WhenTheHigherPriorityProviderIsUnreachable()
    {
        var unreachableProvider = new ProviderProfile(
            "unreachable", "http://localhost:1", 1, [new ModelProfile("unreachable-model", ["Chat"])]);
        var realProvider = new ProviderProfile(
            "ollama", "http://localhost:11434", 2, [new ModelProfile("qwen2.5-coder:7b", ["Chat"])]);
        var registry = new ProviderRegistry([unreachableProvider, realProvider]);
        var healthMonitor = new HealthMonitor(new HealthThresholds(3, TimeSpan.FromSeconds(30)), new NoOpProviderEventLogger());
        var router = new InferenceRouter(registry, healthMonitor);

        using var unreachableHttpClient = new HttpClient { BaseAddress = new Uri(unreachableProvider.Endpoint) };
        using var realHttpClient = new HttpClient { BaseAddress = new Uri(realProvider.Endpoint) };

        var adapters = new Dictionary<string, IAIProviderClient>(StringComparer.Ordinal)
        {
            ["unreachable"] = new OllamaProviderAdapter(unreachableHttpClient, "unreachable-model", maxTokens: 16, temperature: 0.2),
            ["ollama"] = new OllamaProviderAdapter(realHttpClient, "qwen2.5-coder:7b", maxTokens: 16, temperature: 0.2),
        };

        var manager = new AIProviderManager(router, healthMonitor, adapters, new NoOpProviderEventLogger());

        var result = await manager.InferAsync(CreateRequest());

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("qwen2.5-coder:7b", result.Model);
    }

    [Fact]
    public async Task InferAsync_ReturnsCapabilityUnsupported_WhenNoRegisteredProviderSupportsTheCapability()
    {
        var provider = new ProviderProfile("ollama", "http://localhost:11434", 1, [new ModelProfile("qwen2.5-coder:7b", ["Vision"])]);
        var registry = new ProviderRegistry([provider]);
        var healthMonitor = new HealthMonitor(new HealthThresholds(3, TimeSpan.FromSeconds(30)), new NoOpProviderEventLogger());
        var router = new InferenceRouter(registry, healthMonitor);
        var adapters = new Dictionary<string, IAIProviderClient>(StringComparer.Ordinal);
        var manager = new AIProviderManager(router, healthMonitor, adapters, new NoOpProviderEventLogger());

        var result = await manager.InferAsync(CreateRequest());

        Assert.False(result.Success);
        Assert.Equal(InferenceErrorType.CapabilityUnsupported, result.ErrorType);
    }
}
