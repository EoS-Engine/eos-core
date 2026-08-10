namespace EOS.Learning;

/// <summary>WP-027: <see cref="FitnessMonitor"/>'s per-Fitness-Function evaluation outcome.</summary>
public sealed record FitnessCheckResult(string FitnessFunctionId, double ObservedValue, double Threshold, bool Violated);
