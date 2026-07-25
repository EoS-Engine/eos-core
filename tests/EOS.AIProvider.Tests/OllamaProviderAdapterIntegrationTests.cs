using EOS.AIProvider;
using EOS.SDK;

namespace EOS.AIProvider.Tests;

public class OllamaProviderAdapterIntegrationTests
{
    [Fact]
    public async Task InferAsync_ReturnsRealModelOutput_FromTheRunningOllamaInstance()
    {
        using var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:11434") };
        var adapter = new OllamaProviderAdapter(httpClient, "qwen2.5-coder:7b", maxTokens: 16, temperature: 0.2);
        var request = new InferenceRequest(
            RequestId: Guid.NewGuid(),
            CorrelationId: Guid.NewGuid(),
            CapabilityRequired: "Chat",
            Payload: "Reply with exactly the word OK and nothing else.",
            ContextPayloadRef: null,
            TokenBudgetEstimate: 8,
            Priority: 0,
            Caller: "EOS.Reasoning");

        var result = await adapter.InferAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.False(string.IsNullOrWhiteSpace(result.Output));
        Assert.Equal("qwen2.5-coder:7b", result.Model);
        Assert.NotNull(result.Latency);
    }
}
