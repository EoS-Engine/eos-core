using EOS.AIProvider;

namespace EOS.Knowledge.Tests;

public class EmbeddingGeneratorIntegrationTests
{
    [Fact]
    public async Task EmbedAsync_RoundTripsRealContent_ThroughTheRealEmbeddingChannel()
    {
        using var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:11434") };
        IEmbeddingGenerator generator = new OllamaEmbeddingGenerator(httpClient);

        var result = await generator.EmbedAsync("test content");

        Assert.Equal(768, result.Count);
    }

    private sealed class OllamaEmbeddingGenerator(HttpClient httpClient) : IEmbeddingGenerator
    {
        private readonly OllamaEmbeddingAdapter _adapter = new(httpClient, "nomic-embed-text");

        public async Task<IReadOnlyList<float>> EmbedAsync(string content, CancellationToken cancellationToken = default)
        {
            var vector = await _adapter.EmbedAsync(content, cancellationToken);
            return vector.Values;
        }
    }
}
