namespace EOS.AIProvider;

public sealed record HealthThresholds(int FailureThreshold, TimeSpan RecoveryProbeInterval);
