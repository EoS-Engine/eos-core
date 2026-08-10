namespace EOS.Learning.Tests;

public class RoiGateTests
{
    [Fact]
    public void Evaluate_Denies_WhenManualCostSavedIsMissing()
    {
        var gate = new RoiGate();

        var result = gate.Evaluate(new RoiEvaluationInput(null, 10, 100));

        Assert.Equal(RoiGateDecision.Denied, result.Decision);
        Assert.Null(result.Score);
    }

    [Fact]
    public void Evaluate_Denies_WhenProjectedInvocationFrequencyIsMissing()
    {
        var gate = new RoiGate();

        var result = gate.Evaluate(new RoiEvaluationInput(50, null, 100));

        Assert.Equal(RoiGateDecision.Denied, result.Decision);
        Assert.Null(result.Score);
    }

    [Fact]
    public void Evaluate_Denies_WhenBuildAndMaintenanceCostIsMissing()
    {
        var gate = new RoiGate();

        var result = gate.Evaluate(new RoiEvaluationInput(50, 10, null));

        Assert.Equal(RoiGateDecision.Denied, result.Decision);
        Assert.Null(result.Score);
    }

    [Theory]
    [InlineData(-1.0, 10.0, 100.0)]
    [InlineData(50.0, -1.0, 100.0)]
    [InlineData(50.0, 10.0, -1.0)]
    public void Evaluate_Denies_WhenAnyInputIsNegative(double? manualCostSaved, double? invocationFrequency, double? buildCost)
    {
        var gate = new RoiGate();

        var result = gate.Evaluate(new RoiEvaluationInput(manualCostSaved, invocationFrequency, buildCost));

        Assert.Equal(RoiGateDecision.Denied, result.Decision);
    }

    [Fact]
    public void Evaluate_NeverFabricatesAPass_AndNeverAutoQuarantines()
    {
        // Fail-closed on missing data means "deny", never a silent pass — and never Quarantine
        // (Quarantine is reserved for poisoning/integrity conditions, per §24.1/§24.8, not
        // business-metric failures). RoiGateDecision structurally has no "Approved"/"Quarantined"
        // value at all, so this is provable by exhaustive enum inspection.
        Assert.Equal(2, Enum.GetValues<RoiGateDecision>().Length);
        Assert.Contains(RoiGateDecision.Denied, Enum.GetValues<RoiGateDecision>());
        Assert.Contains(RoiGateDecision.ThresholdUnavailable, Enum.GetValues<RoiGateDecision>());
    }

    [Fact]
    public void Evaluate_ComputesTheConstitutionFormula_ButReturnsThresholdUnavailable_WhenAllInputsAreValid()
    {
        var gate = new RoiGate();

        // Constitution §0.16.2: (manual cost saved per invocation x projected invocation
        // frequency) - (build + maintenance cost) = (50 x 10) - 100 = 400.
        var result = gate.Evaluate(new RoiEvaluationInput(50, 10, 100));

        Assert.Equal(RoiGateDecision.ThresholdUnavailable, result.Decision);
        Assert.NotNull(result.Score);
        Assert.Equal(400.0, result.Score.Value, precision: 10);
        Assert.Contains("roi_minimum", result.Reason);
    }

    [Fact]
    public void Evaluate_AllowsZeroValuedInputs()
    {
        var gate = new RoiGate();

        var result = gate.Evaluate(new RoiEvaluationInput(0, 0, 0));

        Assert.Equal(RoiGateDecision.ThresholdUnavailable, result.Decision);
        Assert.NotNull(result.Score);
        Assert.Equal(0.0, result.Score.Value, precision: 10);
    }
}
