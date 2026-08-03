namespace EOS.Contracts;

/// <summary>
/// Resource-Management-Specification-v1.0 §17.1–§17.4's four capacity tiers, in ascending order
/// of severity.
/// </summary>
public enum CapacityTier
{
    Safe,
    Warning,
    Critical,
    Emergency,
}
