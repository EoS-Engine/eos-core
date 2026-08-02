namespace EOS.KnowledgeGraph;

/// <summary>
/// Knowledge-Management-Specification-v1.0 §13's Verification Status quality attribute —
/// "Learning Engine's Reality Validation outcome... recorded as a status enum."
/// </summary>
public enum VerificationStatus
{
    Unverified,
    Verified,
    Contested,
}
