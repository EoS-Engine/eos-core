using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EOS.AIProvider;

namespace EOS.AIProvider.Tests;

public class OllamaEmbeddingAdapterTests
{
    [Fact]
    public async Task EmbedAsync_NormalizesRealShapedResponse_IntoAVector()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { embedding = new[] { 0.1f, 0.2f, 0.3f } }),
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var adapter = new OllamaEmbeddingAdapter(httpClient, "nomic-embed-text");

        var result = await adapter.EmbedAsync("test content");

        Assert.Equal(3, result.Values.Count);
        Assert.Equal(0.1f, result.Values[0]);
    }

    [Fact]
    public async Task EmbedAsync_Throws_WhenProviderReturnsNonJsonBody()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json"),
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var adapter = new OllamaEmbeddingAdapter(httpClient, "nomic-embed-text");

        await Assert.ThrowsAsync<JsonException>(() => adapter.EmbedAsync("test content"));
    }

    [Fact]
    public async Task EmbedAsync_Throws_WhenHttpStatusIsNotSuccess()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var adapter = new OllamaEmbeddingAdapter(httpClient, "nomic-embed-text");

        await Assert.ThrowsAsync<HttpRequestException>(() => adapter.EmbedAsync("test content"));
    }

    [Fact]
    public async Task EmbedAsync_Throws_WhenEndpointIsUnreachable()
    {
        var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:1") };
        var adapter = new OllamaEmbeddingAdapter(httpClient, "nomic-embed-text");

        await Assert.ThrowsAsync<HttpRequestException>(() => adapter.EmbedAsync("test content"));
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(respond(request));
        }
    }
}
