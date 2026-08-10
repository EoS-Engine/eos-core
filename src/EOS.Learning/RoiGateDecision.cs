namespace EOS.Learning;

/// <summary>
/// WP-027 Decision 1 (locked): the ROI Gate's only two reachable outcomes. There is
/// deliberately no "Approved"/"Passed" value — nothing in this codebase produces one, since the
/// numeric <c>roi_minimum</c> threshold (Constitution §0.16.2) exists in no frozen document or
/// configuration anywhere in this repository. Promotion past GoldenPath can never occur through
/// this gate until that threshold is supplied.
/// </summary>
public enum RoiGateDecision
{
    /// <summary>One or more required <see cref="RoiEvaluationInput"/> values are missing or invalid — fail-closed, promotion denied.</summary>
    Denied,

    /// <summary>All required inputs are present and valid, and a raw ROI score was computed, but no <c>roi_minimum</c> exists to compare it against — the comparison itself is unimplemented by explicit architecture decision, not a fabricated pass.</summary>
    ThresholdUnavailable,
}
