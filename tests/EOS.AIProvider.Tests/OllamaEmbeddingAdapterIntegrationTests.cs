using EOS.AIProvider;

namespace EOS.AIProvider.Tests;

public class OllamaEmbeddingAdapterIntegrationTests
{
    [Fact]
    public async Task EmbedAsync_ReturnsARealVector_FromTheRunningOllamaInstance()
    {
        using var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:11434") };
        var adapter = new OllamaEmbeddingAdapter(httpClient, "nomic-embed-text");

        var result = await adapter.EmbedAsync("test content");

        Assert.Equal(768, result.Values.Count);
        Assert.Contains(result.Values, value => value != 0f);
    }
}
