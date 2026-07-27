using EOS.AIProvider;

namespace EOS.Knowledge.Tests;

public class EmbeddingChannelStructuralEnforcementTests
{
    [Fact]
    public async Task EOSKnowledge_CanReachTheEmbeddingChannel_AndReceiveARealVector()
    {
        using var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:11434") };
        var adapter = new OllamaEmbeddingAdapter(httpClient, "nomic-embed-text");

        var result = await adapter.EmbedAsync("test content");

        Assert.Equal(768, result.Values.Count);
    }
}
