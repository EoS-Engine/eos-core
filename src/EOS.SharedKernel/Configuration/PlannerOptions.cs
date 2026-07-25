using System.ComponentModel.DataAnnotations;

namespace EOS.SharedKernel.Configuration;

public sealed record PlannerOptions
{
    [Range(0, 100)]
    public required int DefaultRiskTolerance { get; init; }

    [Range(1, int.MaxValue)]
    public required int ReplanningCadenceMinutes { get; init; }
}
