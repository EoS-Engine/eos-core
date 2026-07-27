using System.Net.Http.Json;
using System.Text.Json.Serialization;
using EOS.SDK;

namespace EOS.AIProvider;

public sealed class OllamaEmbeddingAdapter : IEmbeddingProviderClient
{
    private readonly HttpClient _httpClient;
    private readonly string _model;

    public OllamaEmbeddingAdapter(HttpClient httpClient, string model)
    {
        _httpClient = httpClient;
        _model = model;
    }

    public async Task<Vector> EmbedAsync(string content, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "api/embeddings", new OllamaEmbeddingsRequest(_model, content), cancellationToken);

        response.EnsureSuccessStatusCode();

        var parsed = await response.Content.ReadFromJsonAsync<OllamaEmbeddingsResponse>(cancellationToken);

        if (parsed is null || parsed.Embedding is null || parsed.Embedding.Count == 0)
        {
            throw new InvalidOperationException("Ollama embeddings response was empty or malformed.");
        }

        return new Vector(parsed.Embedding);
    }

    private sealed record OllamaEmbeddingsRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt);

    private sealed record OllamaEmbeddingsResponse(
        [property: JsonPropertyName("embedding")] IReadOnlyList<float>? Embedding);
}
