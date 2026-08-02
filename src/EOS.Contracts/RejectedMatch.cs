namespace EOS.Contracts;

/// <summary>
/// Learning-Engine-Specification-v1.1 §12's <c>ConfidenceGuardResult.rejected_matches[]</c>
/// entry — "(with rejection_reason each)".
/// </summary>
public sealed record RejectedMatch(PipelineRecord Record, string RejectionReason);
