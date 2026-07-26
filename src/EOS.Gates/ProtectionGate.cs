using EOS.Contracts;
using Microsoft.Extensions.Logging;

namespace EOS.Gates;

public sealed class ProtectionGate(ILogger<ProtectionGate> logger) : IProtectionClient
{
    private const int LowTierMaxRiskScore = 30;
    private const int MediumTierMaxRiskScore = 70;

    public ValidationResult Validate(ActionRequest action)
    {
        var tier = ClassifyTier(action.RiskScore);
        var result = tier switch
        {
            RiskTier.Low => new ValidationResult(ProtectionVerdict.Allow, tier, Reason: null),
            RiskTier.Medium => new ValidationResult(
                ProtectionVerdict.Deny,
                tier,
                "Medium-tier validation requires the Permission Model and Resource Protection (deferred to WP-012); failing closed."),
            RiskTier.High => new ValidationResult(
                ProtectionVerdict.Deny,
                tier,
                "High-tier validation requires the full Validation Pipeline (deferred to WP-012); failing closed."),
            _ => throw new ArgumentOutOfRangeException(nameof(action), tier, "Unrecognized risk tier."),
        };

        logger.LogInformation(
            "Protection validate: ActionId={ActionId} ActionType={ActionType} Actor={Actor} RiskScore={RiskScore} Tier={Tier} Verdict={Verdict}",
            action.ActionId, action.ActionType, action.Actor, action.RiskScore, result.Tier, result.Verdict);

        return result;
    }

    private static RiskTier ClassifyTier(int riskScore)
    {
        if (riskScore <= LowTierMaxRiskScore)
        {
            return RiskTier.Low;
        }

        return riskScore <= MediumTierMaxRiskScore ? RiskTier.Medium : RiskTier.High;
    }
}
