namespace EOS.Learning;

/// <summary>
/// WP-027 Decision 1 (locked). <see cref="Score"/> is the raw Constitution §0.16.2 formula
/// output — populated whenever all inputs are valid (i.e. whenever <see cref="Decision"/> is
/// <see cref="RoiGateDecision.ThresholdUnavailable"/>), <see langword="null"/> when inputs were
/// invalid/missing (<see cref="RoiGateDecision.Denied"/>), since no meaningful score can be
/// computed from incomplete data.
/// </summary>
public sealed record RoiGateResult(RoiGateDecision Decision, double? Score, string Reason);
