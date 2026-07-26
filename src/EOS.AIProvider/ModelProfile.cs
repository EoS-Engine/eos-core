namespace EOS.AIProvider;

public sealed record ModelProfile(string Name, IReadOnlyList<string> Capabilities);
