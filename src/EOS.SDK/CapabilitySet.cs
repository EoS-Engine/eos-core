namespace EOS.SDK;

public sealed record CapabilityEntry(string ProviderName, string ModelName, IReadOnlyList<string> Capabilities);

public sealed record CapabilitySet(IReadOnlyList<CapabilityEntry> Entries);
