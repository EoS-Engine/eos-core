using System.ComponentModel.DataAnnotations;

namespace EOS.SharedKernel.Configuration;

public sealed record PolicyEntry
{
    [Required, MinLength(1)]
    public required string ActionType { get; init; }

    [Required, MinLength(1)]
    public required string Verdict { get; init; }

    [Required, MinLength(1)]
    public required string Reason { get; init; }
}

public sealed record SecurityOptions
{
    [Required, MinLength(1)]
    public required string SecretsProvider { get; init; }

    [Required]
    public required IReadOnlyList<PolicyEntry> GlobalPolicies { get; init; }

    [Required]
    public required IReadOnlyList<PolicyEntry> ProjectPolicies { get; init; }

    [Required]
    public required IReadOnlyList<PolicyEntry> UserPolicies { get; init; }

    [Required]
    public required IReadOnlyList<PolicyEntry> RuntimePolicies { get; init; }
}
