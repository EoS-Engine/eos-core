using System.ComponentModel.DataAnnotations;

namespace EOS.SharedKernel.Configuration;

public sealed record ThresholdsOptions
{
    [Range(0, 100)]
    public required int ResourceWarningPercent { get; init; }

    [Range(0, 100)]
    public required int ResourceCriticalPercent { get; init; }

    [Range(1, int.MaxValue)]
    public required int ProviderFailureThreshold { get; init; }

    [Range(1, int.MaxValue)]
    public required int ProviderRecoveryProbeIntervalSeconds { get; init; }

    [Range(1, 2_147_483)]
    public required int InferenceTimeoutSeconds { get; init; }
}
