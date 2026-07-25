using System.ComponentModel.DataAnnotations;

namespace EOS.SharedKernel.Configuration;

public sealed record SecurityOptions
{
    [Required, MinLength(1)]
    public required string SecretsProvider { get; init; }
}
