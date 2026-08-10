namespace EOS.Learning.Tests;

public class FitnessMonitorTests
{
    [Fact]
    public void EvaluateStallRate_Passes_WhenBelowFifteenPercent()
    {
        var publisher = new RecordingFitnessFunctionViolatedEventPublisher();
        var monitor = new FitnessMonitor(publisher);

        var result = monitor.EvaluateStallRate(activeRecordCount: 100, stalledActiveRecordCount: 14);

        Assert.False(result.Violated);
        Assert.Equal(0, publisher.CallCount);
    }

    [Fact]
    public void EvaluateStallRate_Violates_AtExactlyFifteenPercent()
    {
        var publisher = new RecordingFitnessFunctionViolatedEventPublisher();
        var monitor = new FitnessMonitor(publisher);

        var result = monitor.EvaluateStallRate(activeRecordCount: 100, stalledActiveRecordCount: 15);

        Assert.True(result.Violated);
        Assert.Equal(1, publisher.CallCount);
        Assert.Equal("LF-1", publisher.LastFitnessFunctionId);
    }

    [Fact]
    public void EvaluateStallSweepCompliance_Passes_AtOneHundredPercent()
    {
        var monitor = new FitnessMonitor(new RecordingFitnessFunctionViolatedEventPublisher());

        var result = monitor.EvaluateStallSweepCompliance(cyclesCompletedOnTarget: 4, totalCycles: 4);

        Assert.False(result.Violated);
    }

    [Fact]
    public void EvaluateStallSweepCompliance_Violates_WhenAnyCycleMissed()
    {
        var publisher = new RecordingFitnessFunctionViolatedEventPublisher();
        var monitor = new FitnessMonitor(publisher);

        var result = monitor.EvaluateStallSweepCompliance(cyclesCompletedOnTarget: 3, totalCycles: 4);

        Assert.True(result.Violated);
        Assert.Equal("LF-2", publisher.LastFitnessFunctionId);
    }

    [Fact]
    public void EvaluateConfidenceBand_Passes_BelowTenPercent()
    {
        var monitor = new FitnessMonitor(new RecordingFitnessFunctionViolatedEventPublisher());

        var result = monitor.EvaluateConfidenceBand(promotionCount: 100, nearThresholdPromotionCount: 9);

        Assert.False(result.Violated);
    }

    [Fact]
    public void EvaluateConfidenceBand_Violates_AtExactlyTenPercent()
    {
        var publisher = new RecordingFitnessFunctionViolatedEventPublisher();
        var monitor = new FitnessMonitor(publisher);

        var result = monitor.EvaluateConfidenceBand(promotionCount: 100, nearThresholdPromotionCount: 10);

        Assert.True(result.Violated);
        Assert.Equal("LF-3", publisher.LastFitnessFunctionId);
    }

    [Fact]
    public void EvaluateIntegrityViolations_Passes_WhenZero()
    {
        var monitor = new FitnessMonitor(new RecordingFitnessFunctionViolatedEventPublisher());

        var result = monitor.EvaluateIntegrityViolations(0);

        Assert.False(result.Violated);
    }

    [Fact]
    public void EvaluateIntegrityViolations_Violates_ForAnyNonZeroCount()
    {
        var publisher = new RecordingFitnessFunctionViolatedEventPublisher();
        var monitor = new FitnessMonitor(publisher);

        var result = monitor.EvaluateIntegrityViolations(1);

        Assert.True(result.Violated);
        Assert.Equal("LF-4", publisher.LastFitnessFunctionId);
    }

    [Fact]
    public void ComputeQuarantineFalsePositiveRatio_NeverPublishes_SinceLF5HasNoThreshold()
    {
        var publisher = new RecordingFitnessFunctionViolatedEventPublisher();
        var monitor = new FitnessMonitor(publisher);

        var ratio = monitor.ComputeQuarantineFalsePositiveRatio(quarantinedCount: 10, clearedAsFalsePositiveCount: 3);

        Assert.Equal(0.3, ratio, precision: 10);
        Assert.Equal(0, publisher.CallCount);
    }

    [Fact]
    public void ComputeQuarantineFalsePositiveRatio_ReturnsZero_WhenNothingWasQuarantined()
    {
        var monitor = new FitnessMonitor(new RecordingFitnessFunctionViolatedEventPublisher());

        var ratio = monitor.ComputeQuarantineFalsePositiveRatio(quarantinedCount: 0, clearedAsFalsePositiveCount: 0);

        Assert.Equal(0.0, ratio, precision: 10);
    }

    [Fact]
    public void EvaluateRoiDeviation_Passes_BelowTwentyFivePercent()
    {
        var monitor = new FitnessMonitor(new RecordingFitnessFunctionViolatedEventPublisher());

        var result = monitor.EvaluateRoiDeviation(projectedScore: 100, realizedScore: 110);

        Assert.False(result.Violated);
    }

    [Fact]
    public void EvaluateRoiDeviation_Violates_AtExactlyTwentyFivePercent()
    {
        var publisher = new RecordingFitnessFunctionViolatedEventPublisher();
        var monitor = new FitnessMonitor(publisher);

        var result = monitor.EvaluateRoiDeviation(projectedScore: 100, realizedScore: 125);

        Assert.True(result.Violated);
        Assert.Equal("LF-6", publisher.LastFitnessFunctionId);
    }

    [Fact]
    public void EvaluateRoiDeviation_DoesNotViolate_WhenProjectedAndRealizedAreBothZero()
    {
        // CodeRabbit R1 finding #1: a zero/zero baseline is a genuine zero deviation, not a gap.
        var monitor = new FitnessMonitor(new RecordingFitnessFunctionViolatedEventPublisher());

        var result = monitor.EvaluateRoiDeviation(projectedScore: 0, realizedScore: 0);

        Assert.False(result.Violated);
        Assert.Equal(0.0, result.ObservedValue);
    }

    [Fact]
    public void EvaluateRoiDeviation_Violates_WhenProjectedIsZeroButRealizedIsNot()
    {
        // CodeRabbit R1 finding #1: previously silently reported 0% deviation for this case —
        // projectedScore: 0, realizedScore: 100 must not pass LF-6.
        var publisher = new RecordingFitnessFunctionViolatedEventPublisher();
        var monitor = new FitnessMonitor(publisher);

        var result = monitor.EvaluateRoiDeviation(projectedScore: 0, realizedScore: 100);

        Assert.True(result.Violated);
        Assert.Equal("LF-6", publisher.LastFitnessFunctionId);
        Assert.Equal(double.PositiveInfinity, result.ObservedValue);
    }
}
