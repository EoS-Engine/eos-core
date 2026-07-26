namespace EOS.AIProvider;

public sealed record ProviderProfile(string Name, string Endpoint, int Priority, IReadOnlyList<ModelProfile> Models);
