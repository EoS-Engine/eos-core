using System.ComponentModel.DataAnnotations;

namespace EOS.SharedKernel.Configuration;

public sealed record ProviderEntry
{
    [Required, MinLength(1)]
    public required string Name { get; init; }

    [Required, Url]
    public required string Endpoint { get; init; }

    [Range(1, int.MaxValue)]
    public required int Priority { get; init; }
}

public sealed record ProvidersOptions
{
    [Required]
    public required IReadOnlyList<ProviderEntry> Providers { get; init; }
}
