using System.Net;
using System.Net.Http.Json;
using EOS.AIProvider;
using EOS.SDK;

namespace EOS.AIProvider.Tests;

public class OllamaProviderAdapterTests
{
    private static InferenceRequest CreateRequest(string payload = "Say OK", int tokenBudgetEstimate = 16)
    {
        return new InferenceRequest(
            RequestId: Guid.NewGuid(),
            CorrelationId: Guid.NewGuid(),
            CapabilityRequired: "Chat",
            Payload: payload,
            ContextPayloadRef: null,
            TokenBudgetEstimate: tokenBudgetEstimate,
            Priority: 0,
            Caller: "EOS.Reasoning");
    }

    [Fact]
    public async Task InferAsync_ReturnsContextTooLarge_WhenTokenBudgetExceedsConfiguredCeiling()
    {
        var adapter = new OllamaProviderAdapter(new HttpClient(), "qwen2.5-coder:7b", maxTokens: 10, temperature: 0.2);
        var request = CreateRequest(tokenBudgetEstimate: 11);

        var result = await adapter.InferAsync(request);

        Assert.False(result.Success);
        Assert.Equal(InferenceErrorType.ContextTooLarge, result.ErrorType);
    }

    [Fact]
    public async Task InferAsync_ReturnsProviderUnavailable_WhenEndpointIsUnreachable()
    {
        var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:1") };
        var adapter = new OllamaProviderAdapter(httpClient, "qwen2.5-coder:7b", maxTokens: 4096, temperature: 0.2);
        var request = CreateRequest();

        var result = await adapter.InferAsync(request);

        Assert.False(result.Success);
        Assert.Equal(InferenceErrorType.ProviderUnavailable, result.ErrorType);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task InferAsync_ReturnsMalformedResponse_WhenProviderReturnsNonJsonBody()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json"),
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var adapter = new OllamaProviderAdapter(httpClient, "qwen2.5-coder:7b", maxTokens: 4096, temperature: 0.2);
        var request = CreateRequest();

        var result = await adapter.InferAsync(request);

        Assert.False(result.Success);
        Assert.Equal(InferenceErrorType.MalformedResponse, result.ErrorType);
    }

    [Fact]
    public async Task InferAsync_ReturnsProviderUnavailable_WhenHttpStatusIsNotSuccess()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var adapter = new OllamaProviderAdapter(httpClient, "qwen2.5-coder:7b", maxTokens: 4096, temperature: 0.2);
        var request = CreateRequest();

        var result = await adapter.InferAsync(request);

        Assert.False(result.Success);
        Assert.Equal(InferenceErrorType.ProviderUnavailable, result.ErrorType);
    }

    [Fact]
    public async Task InferAsync_NormalizesRealShapedResponse_IntoSuccessfulInferenceResult()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                model = "qwen2.5-coder:7b",
                response = "OK",
                done = true,
                prompt_eval_count = 31,
                eval_count = 2,
            }),
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var adapter = new OllamaProviderAdapter(httpClient, "qwen2.5-coder:7b", maxTokens: 4096, temperature: 0.2);
        var request = CreateRequest();

        var result = await adapter.InferAsync(request);

        Assert.True(result.Success);
        Assert.Equal("OK", result.Output);
        Assert.Equal("qwen2.5-coder:7b", result.Model);
        Assert.Equal(31, result.PromptTokens);
        Assert.Equal(2, result.CompletionTokens);
        Assert.Null(result.ErrorType);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task InferAsync_ReturnsMalformedResponse_WhenResponseIsNonEmptyButNotDone()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                model = "qwen2.5-coder:7b",
                response = "partial",
                done = false,
            }),
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var adapter = new OllamaProviderAdapter(httpClient, "qwen2.5-coder:7b", maxTokens: 4096, temperature: 0.2);
        var request = CreateRequest();

        var result = await adapter.InferAsync(request);

        Assert.False(result.Success);
        Assert.Equal(InferenceErrorType.MalformedResponse, result.ErrorType);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(respond(request));
        }
    }
}
