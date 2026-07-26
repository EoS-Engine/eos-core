namespace EOS.AIProvider;

public sealed record RoutingCandidate(ProviderProfile Provider, ModelProfile Model);

public sealed class InferenceRouter(ProviderRegistry registry, HealthMonitor healthMonitor)
{
    public IReadOnlyList<RoutingCandidate> Route(string capabilityRequired)
    {
        return registry.FindByCapability(capabilityRequired)
            .Where(provider => healthMonitor.IsAvailable(provider.Name))
            .OrderBy(provider => provider.Priority)
            .Select(provider => new RoutingCandidate(
                provider,
                provider.Models.First(model => model.Capabilities.Contains(capabilityRequired, StringComparer.OrdinalIgnoreCase))))
            .ToList();
    }
}
