using System.ComponentModel.DataAnnotations;

namespace EOS.SharedKernel.Configuration;

public sealed record EosOptions
{
    [Required, MinLength(1)]
    public required string SystemName { get; init; }

    [Required, RegularExpression("^(Development|Staging|Production)$")]
    public required string Environment { get; init; }

    [Required, MinLength(1)]
    public required string Version { get; init; }
}
