namespace EOS.Contracts;

public sealed record Explanation(
    string Why,
    string[] EvidenceUsed,
    string[] Assumptions,
    (string Hypothesis, string Reason)[] AlternativesRejected,
    string ConfidenceRationale,
    string[] Risks);
