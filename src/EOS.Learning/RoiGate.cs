namespace EOS.Learning;

/// <summary>
/// Learning-Engine-Specification-v1.1 §11.3's ROI Gate ("unchanged from v1.0 §12.3 — already
/// fail-closed, ADR-L003"). WP-027 Decision 1 (locked): <c>Learning-Engine-Specification-v1.0.md</c>
/// (ADR-L003's own text) does not exist anywhere in this repository, and no document, sequence
/// diagram, or configuration file supplies a numeric <c>roi_minimum</c> — the sequence diagram
/// (§17.2) shows <c>ROI-->>Learning: ROIEvaluation{score}</c> and <c>ROI->>Config: read
/// roi_minimum</c>, confirming the architecture anticipates a config-sourced threshold, but no
/// value exists to read. Per explicit instruction, the pass/fail comparison itself is therefore
/// never implemented here — this class only validates inputs (fail-closed) and computes the raw
/// score (Constitution §0.16.2), never comparing it to anything. There is no code path in this
/// class, or anywhere that calls it, that can result in a promotion.
/// </summary>
public sealed class RoiGate
{
    public RoiGateResult Evaluate(RoiEvaluationInput input)
    {
        if (input.ManualCostSavedPerInvocation is not { } manualCostSaved || manualCostSaved < 0)
        {
            return new RoiGateResult(RoiGateDecision.Denied, Score: null, "ManualCostSavedPerInvocation is missing or negative.");
        }

        if (input.ProjectedInvocationFrequency is not { } invocationFrequency || invocationFrequency < 0)
        {
            return new RoiGateResult(RoiGateDecision.Denied, Score: null, "ProjectedInvocationFrequency is missing or negative.");
        }

        if (input.BuildAndMaintenanceCost is not { } buildAndMaintenanceCost || buildAndMaintenanceCost < 0)
        {
            return new RoiGateResult(RoiGateDecision.Denied, Score: null, "BuildAndMaintenanceCost is missing or negative.");
        }

        // Constitution §0.16.2: (manual cost saved per invocation) x (projected invocation
        // frequency) - (build + maintenance cost). All inputs are valid, so the score is real —
        // but WP-027 Decision 1 forbids comparing it to any threshold, since roi_minimum does
        // not exist anywhere in this repository.
        var score = (manualCostSaved * invocationFrequency) - buildAndMaintenanceCost;

        return new RoiGateResult(
            RoiGateDecision.ThresholdUnavailable,
            score,
            "roi_minimum is not defined by any frozen specification or configuration (WP-027 Decision 1) — " +
            "the ROI pass/fail comparison is not implemented. Promotion cannot proceed via the ROI Gate until " +
            "this value is supplied.");
    }
}
