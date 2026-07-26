using EOS.SDK;

namespace EOS.AIProvider;

public sealed class AIProviderManager(
    InferenceRouter router,
    HealthMonitor healthMonitor,
    IReadOnlyDictionary<(string ProviderName, string ModelName), IAIProviderClient> adapters,
    IProviderEventLogger logger) : IAIProviderClient
{
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
