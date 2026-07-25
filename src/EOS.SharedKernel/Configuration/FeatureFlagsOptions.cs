namespace EOS.SharedKernel.Configuration;

public sealed record FeatureFlagsOptions
{
    public required bool EnableAutonomousLoop { get; init; }
}
