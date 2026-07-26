namespace EOS.AIProvider;

public sealed class HealthMonitor(HealthThresholds thresholds, IProviderEventLogger logger)
{
    private readonly Dictionary<string, ProviderHealthState> _state = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public bool IsAvailable(string providerName)
    {
        lock (_lock)
        {
            if (!_state.TryGetValue(providerName, out var state) || !state.Unavailable)
            {
                return true;
            }

            return DateTimeOffset.UtcNow - state.MarkedUnavailableAt >= thresholds.RecoveryProbeInterval;
        }
    }

    public void RecordSuccess(string providerName, TimeSpan? latency = null)
    {
        lock (_lock)
        {
            var wasUnavailable = _state.TryGetValue(providerName, out var existing) && existing.Unavailable;
            _state[providerName] = new ProviderHealthState(FailureCount: 0, Unavailable: false, MarkedUnavailableAt: default, LastLatency: latency);

            if (wasUnavailable)
            {
                logger.LogEvent($"ProviderRecovered: {providerName}");
            }
        }
    }

    public void RecordFailure(string providerName, TimeSpan? latency = null)
    {
        lock (_lock)
        {
            _state.TryGetValue(providerName, out var existing);
            var failureCount = existing.FailureCount + 1;

            if (failureCount >= thresholds.FailureThreshold)
            {
                _state[providerName] = new ProviderHealthState(failureCount, Unavailable: true, DateTimeOffset.UtcNow, latency);
                logger.LogWarning(
                    $"ProviderMarkedUnavailable: {providerName} after {failureCount} consecutive failures.");
            }
            else
            {
                _state[providerName] = new ProviderHealthState(failureCount, Unavailable: false, MarkedUnavailableAt: default, latency);
            }
        }
    }

    private readonly record struct ProviderHealthState(int FailureCount, bool Unavailable, DateTimeOffset MarkedUnavailableAt, TimeSpan? LastLatency);
}
