namespace EOS.Learning;

/// <summary>
/// Learning-Engine-Specification-v1.1 §22's six Fitness Functions — real, directly-callable,
/// deterministic, fully-testable code; invocation cadence ("same Sprint-cycle cadence as the
/// Stall Sweep") is out of scope, matching every other sweep-shaped component in this codebase
/// (no scheduler/timer/host exists anywhere). WP-027 Decision 4 (locked): the six threshold
/// values are used exactly as literals from §22's table — never externalized to
/// <c>Thresholds.json</c>, never made configurable.
///
/// None of this codebase's existing components track the raw observations these functions need
/// (e.g. how many Active records stalled, how many Sprint cycles the Stall Sweep actually ran
/// in) — no frozen document defines an automated source for that operational telemetry either.
/// Rather than inventing one, every method here takes the raw observation as an explicit,
/// caller-supplied parameter and evaluates it against §22's fixed threshold — mirroring the same
/// "explicit input contract, not a fabricated data source" pattern WP-027 Decision 1 established
/// for the ROI Gate.
/// </summary>
public sealed class FitnessMonitor(IFitnessFunctionViolatedEventPublisher fitnessFunctionViolatedEventPublisher)
{
    /// <summary>LF-1: % of Active records with zero stage advancement for &gt;2 consecutive Sprint cycles | &lt; 15%.</summary>
    public FitnessCheckResult EvaluateStallRate(int activeRecordCount, int stalledActiveRecordCount)
    {
        var observed = activeRecordCount == 0 ? 0.0 : (double)stalledActiveRecordCount / activeRecordCount * 100.0;
        return Evaluate("LF-1", observed, threshold: 15.0, violated: observed >= 15.0);
    }

    /// <summary>LF-2: Stall Sweep completed within its performance target every cycle | 100% of cycles.</summary>
    public FitnessCheckResult EvaluateStallSweepCompliance(int cyclesCompletedOnTarget, int totalCycles)
    {
        var observed = totalCycles == 0 ? 100.0 : (double)cyclesCompletedOnTarget / totalCycles * 100.0;
        return Evaluate("LF-2", observed, threshold: 100.0, violated: observed < 100.0);
    }

    /// <summary>LF-3: % of promotions with confidence_score below the high-confidence band | &lt; 10% of promotions in a cycle.</summary>
    public FitnessCheckResult EvaluateConfidenceBand(int promotionCount, int nearThresholdPromotionCount)
    {
        var observed = promotionCount == 0 ? 0.0 : (double)nearThresholdPromotionCount / promotionCount * 100.0;
        return Evaluate("LF-3", observed, threshold: 10.0, violated: observed >= 10.0);
    }

    /// <summary>LF-4: count of DataIntegrityViolationDetected events per cycle | 0.</summary>
    public FitnessCheckResult EvaluateIntegrityViolations(int violationCount) =>
        Evaluate("LF-4", violationCount, threshold: 0.0, violated: violationCount > 0);

    /// <summary>
    /// LF-5: count of records Quarantined vs. cleared-as-false-positive, ratio | "Tracked as a
    /// trend" — §22 assigns no pass/fail threshold, so this returns the raw ratio only, never
    /// publishing FitnessFunctionViolated (there is nothing to violate).
    /// </summary>
    public double ComputeQuarantineFalsePositiveRatio(int quarantinedCount, int clearedAsFalsePositiveCount) =>
        quarantinedCount == 0 ? 0.0 : (double)clearedAsFalsePositiveCount / quarantinedCount;

    /// <summary>LF-6: Automation ROI realized vs. ROIEvaluation.score projected, deviation | &lt; 25% average deviation.</summary>
    public FitnessCheckResult EvaluateRoiDeviation(double projectedScore, double realizedScore)
    {
        var observed = projectedScore == 0.0 ? 0.0 : Math.Abs(realizedScore - projectedScore) / Math.Abs(projectedScore) * 100.0;
        return Evaluate("LF-6", observed, threshold: 25.0, violated: observed >= 25.0);
    }

    private FitnessCheckResult Evaluate(string fitnessFunctionId, double observedValue, double threshold, bool violated)
    {
        if (violated)
        {
            fitnessFunctionViolatedEventPublisher.PublishFitnessFunctionViolated(fitnessFunctionId, observedValue, threshold);
        }

        return new FitnessCheckResult(fitnessFunctionId, observedValue, threshold, violated);
    }
}
