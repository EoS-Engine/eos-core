namespace EOS.Contracts;

public sealed record ValidationResult(
    ProtectionVerdict Verdict,
    RiskTier Tier,
    string? Reason);
