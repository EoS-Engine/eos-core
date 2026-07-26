namespace EOS.AIProvider;

public sealed class ProviderRegistry(IReadOnlyList<ProviderProfile> providers)
{
    public IReadOnlyList<ProviderProfile> Providers { get; } = providers;

    public IReadOnlyList<ProviderProfile> FindByCapability(string capability)
    {
        return Providers
            .Where(provider => provider.Models.Any(model => model.Capabilities.Contains(capability, StringComparer.OrdinalIgnoreCase)))
            .ToList();
    }
}
