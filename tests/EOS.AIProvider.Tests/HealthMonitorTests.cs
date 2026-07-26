using EOS.AIProvider;

namespace EOS.AIProvider.Tests;

public class HealthMonitorTests
{
    private static HealthMonitor CreateMonitor(int failureThreshold = 3, int recoveryProbeIntervalSeconds = 30)
    {
        var thresholds = new HealthThresholds(failureThreshold, TimeSpan.FromSeconds(recoveryProbeIntervalSeconds));
        return new HealthMonitor(thresholds, new NoOpProviderEventLogger());
    }

    [Fact]
    public void IsAvailable_ReturnsTrue_ForAProviderWithNoRecordedHistory()
    {
        var monitor = CreateMonitor();

        Assert.True(monitor.IsAvailable("ollama"));
    }

    [Fact]
    public void IsAvailable_StaysTrue_WhileFailuresRemainBelowTheThreshold()
    {
        var monitor = CreateMonitor(failureThreshold: 3);

        monitor.RecordFailure("ollama");
        monitor.RecordFailure("ollama");

        Assert.True(monitor.IsAvailable("ollama"));
    }

    [Fact]
    public void IsAvailable_ReturnsFalse_OnceFailuresReachTheThreshold()
    {
        var monitor = CreateMonitor(failureThreshold: 2);

        monitor.RecordFailure("ollama");
        monitor.RecordFailure("ollama");

        Assert.False(monitor.IsAvailable("ollama"));
    }

    [Fact]
    public void IsAvailable_ReturnsTrue_OnceTheRecoveryProbeIntervalHasElapsed()
    {
        var monitor = CreateMonitor(failureThreshold: 1, recoveryProbeIntervalSeconds: 0);

        monitor.RecordFailure("ollama");

        Assert.True(monitor.IsAvailable("ollama"));
    }

    [Fact]
    public void RecordSuccess_ResetsTheFailureCountAndAvailability()
    {
        var monitor = CreateMonitor(failureThreshold: 2);

        monitor.RecordFailure("ollama");
        monitor.RecordFailure("ollama");
        Assert.False(monitor.IsAvailable("ollama"));

        monitor.RecordSuccess("ollama");

        Assert.True(monitor.IsAvailable("ollama"));
    }
}
