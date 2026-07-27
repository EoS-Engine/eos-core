using EOS.SDK;

namespace EOS.AIProvider;

public sealed class AIProviderManager(
    InferenceRouter router,
    HealthMonitor healthMonitor,
    IReadOnlyDictionary<(string ProviderName, string ModelName), IAIProviderClient> adapters,
    IProviderEventLogger logger,
    IReadOnlyDictionary<(string ProviderName, string ModelName), IEmbeddingProviderClient>? embeddingAdapters = null,
    ProviderRegistry? providerRegistry = null) : IAIProviderClient, IEmbeddingProviderClient
{
    private readonly IReadOnlyDictionary<(string ProviderName, string ModelName), IEmbeddingProviderClient> _embeddingAdapters =
        embeddingAdapters ?? new Dictionary<(string, string), IEmbeddingProviderClient>();

    public async Task<InferenceResult> InferAsync(InferenceRequest request, CancellationToken cancellationToken = default)
    {
        var candidates = router.Route(request.CapabilityRequired);

        if (candidates.Count == 0)
        {
            logger.LogWarning($"RoutingDenied: no available provider supports capability {request.CapabilityRequired}.");
            return Failure(
                InferenceErrorType.CapabilityUnsupported,
                $"No available provider supports capability '{request.CapabilityRequired}'.");
        }

        InferenceResult? lastFailure = null;

        foreach (var candidate in candidates)
        {
            var adapter = adapters[(candidate.Provider.Name, candidate.Model.Name)];

            logger.LogEvent(
                $"InferenceRouted: {candidate.Provider.Name}/{candidate.Model.Name} (correlationId={request.CorrelationId})");

            var result = await adapter.InferAsync(request, cancellationToken);

            if (result.Success)
            {
                healthMonitor.RecordSuccess(candidate.Provider.Name, result.Latency);
                logger.LogEvent(
                    $"InferenceCompleted: {candidate.Provider.Name}/{candidate.Model.Name} " +
                    $"({result.Latency?.TotalMilliseconds:F0}ms, correlationId={request.CorrelationId})");
                return result;
            }

            logger.LogWarning(
                $"InferenceAttemptFailed: {candidate.Provider.Name}/{candidate.Model.Name} " +
                $"correlationId={request.CorrelationId}: {result.ErrorMessage}");
            healthMonitor.RecordFailure(candidate.Provider.Name, result.Latency);
            lastFailure = result;
        }

        return lastFailure ?? Failure(
            InferenceErrorType.ProviderUnavailable, "No configured adapter was available for any ranked candidate.");
    }

    public CapabilitySet DiscoverCapabilities(string? capabilityFilter)
    {
        var providers = providerRegistry?.Providers ?? [];

        var entries = providers
            .SelectMany(provider => provider.Models.Select(model => new { provider, model }))
            .Where(x => capabilityFilter is null || x.model.Capabilities.Contains(capabilityFilter, StringComparer.OrdinalIgnoreCase))
            .Select(x => new CapabilityEntry(x.provider.Name, x.model.Name, x.model.Capabilities))
            .ToList();

        return new CapabilitySet(entries);
    }

    public async Task<Vector> EmbedAsync(string content, CancellationToken cancellationToken = default)
    {
        var candidates = router.Route("Embeddings");

        foreach (var candidate in candidates)
        {
            if (!_embeddingAdapters.TryGetValue((candidate.Provider.Name, candidate.Model.Name), out var adapter))
            {
                continue;
            }

            logger.LogEvent($"EmbeddingRouted: {candidate.Provider.Name}/{candidate.Model.Name}");

            var vector = await adapter.EmbedAsync(content, cancellationToken);

            healthMonitor.RecordSuccess(candidate.Provider.Name);
            logger.LogEvent($"EmbeddingCompleted: {candidate.Provider.Name}/{candidate.Model.Name}");

            return vector;
        }

        throw new InvalidOperationException("No available provider supports the 'Embeddings' capability.");
    }

    private static InferenceResult Failure(InferenceErrorType errorType, string message)
    {
        return new InferenceResult(
            Success: false,
            Output: null,
            Model: null,
            PromptTokens: null,
            CompletionTokens: null,
            Latency: null,
            ErrorType: errorType,
            ErrorMessage: message);
    }
}
