using System.ComponentModel.DataAnnotations;

namespace EOS.SharedKernel.Configuration;

public sealed record DashboardOptions
{
    [Required, MinLength(1)]
    public required string Title { get; init; }
}
