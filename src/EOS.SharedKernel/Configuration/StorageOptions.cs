using System.ComponentModel.DataAnnotations;

namespace EOS.SharedKernel.Configuration;

public sealed record StorageOptions
{
    [Required, MinLength(1)]
    public required string DataDirectory { get; init; }
}
