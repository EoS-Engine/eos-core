using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using EOS.SDK;

namespace EOS.AIProvider;

public sealed class OllamaProviderAdapter : IAIProviderClient
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly int _maxTokens;
    private readonly double _temperature;

    public OllamaProviderAdapter(HttpClient httpClient, string model, int maxTokens, double temperature)
    {
        _httpClient = httpClient;
        _model = model;
        _maxTokens = maxTokens;
        _temperature = temperature;
    }

    public async Task<InferenceResult> InferAsync(InferenceRequest request, CancellationToken cancellationToken = default)
    {
        if (request.TokenBudgetEstimate > _maxTokens)
        {
            return new InferenceResult(
                Success: false,
                Output: null,
                Model: null,
                PromptTokens: null,
                CompletionTokens: null,
                Latency: null,
                ErrorType: InferenceErrorType.ContextTooLarge,
                ErrorMessage: $"Estimated token budget {request.TokenBudgetEstimate} exceeds the configured ceiling of {_maxTokens}.");
        }

        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;

        try
        {
            response = await _httpClient.PostAsJsonAsync(
                "api/generate",
                new OllamaGenerateRequest(_model, request.Payload, false, new OllamaGenerateOptions(_temperature, _maxTokens)),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return Failure(InferenceErrorType.Timeout, "Request to Ollama timed out.", stopwatch.Elapsed);
        }
        catch (HttpRequestException ex)
        {
            return Failure(InferenceErrorType.ProviderUnavailable, ex.Message, stopwatch.Elapsed);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return Failure(InferenceErrorType.ProviderUnavailable, $"Ollama returned HTTP status {(int)response.StatusCode}.", stopwatch.Elapsed);
            }

            OllamaGenerateResponse? parsed;
            try
            {
                parsed = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(cancellationToken);
            }
            catch (JsonException ex)
            {
                return Failure(InferenceErrorType.MalformedResponse, ex.Message, stopwatch.Elapsed);
            }

            stopwatch.Stop();

            if (parsed is null || string.IsNullOrEmpty(parsed.Response) || !parsed.Done)
            {
                return Failure(InferenceErrorType.MalformedResponse, "Ollama response was empty, malformed, or incomplete.", stopwatch.Elapsed);
            }

            return new InferenceResult(
                Success: true,
                Output: parsed.Response,
                Model: parsed.Model,
                PromptTokens: parsed.PromptEvalCount,
                CompletionTokens: parsed.EvalCount,
                Latency: stopwatch.Elapsed,
                ErrorType: null,
                ErrorMessage: null);
        }
    }

    private static InferenceResult Failure(InferenceErrorType errorType, string message, TimeSpan latency)
    {
        return new InferenceResult(
            Success: false,
            Output: null,
            Model: null,
            PromptTokens: null,
            CompletionTokens: null,
            Latency: latency,
            ErrorType: errorType,
            ErrorMessage: message);
    }

    private sealed record OllamaGenerateRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("options")] OllamaGenerateOptions Options);

    private sealed record OllamaGenerateOptions(
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("num_predict")] int NumPredict);

    private sealed record OllamaGenerateResponse(
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("response")] string? Response,
        [property: JsonPropertyName("done")] bool Done,
        [property: JsonPropertyName("prompt_eval_count")] int? PromptEvalCount,
        [property: JsonPropertyName("eval_count")] int? EvalCount);
}
