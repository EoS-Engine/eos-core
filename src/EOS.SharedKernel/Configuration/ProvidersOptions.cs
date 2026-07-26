using System.ComponentModel.DataAnnotations;

namespace EOS.SharedKernel.Configuration;

public sealed record ModelEntry
{
    [Required, MinLength(1)]
    public required string Name { get; init; }

    [Required, MinLength(1)]
    public required IReadOnlyList<string> Capabilities { get; init; }
}

public sealed record ProviderEntry
{
    [Required, MinLength(1)]
    public required string Name { get; init; }

    [Required, Url]
    public required string Endpoint { get; init; }

    [Range(1, int.MaxValue)]
    public required int Priority { get; init; }

    [Required, MinLength(1)]
    public required IReadOnlyList<ModelEntry> Models { get; init; }
}

public sealed record ProvidersOptions
{
    [Required]
    public required IReadOnlyList<ProviderEntry> Providers { get; init; }
}
