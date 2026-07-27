using EOS.Contracts;

namespace EOS.Gates;

public sealed record RiskAssessment(RiskTier Tier, bool Escalated);

/// <summary>
/// Consumes ActionRequest.RiskScore per FR-P6 (Protection-Layer-Specification-v1.0) — never
/// recomputes a score already owned by the acting subsystem. Tracks per-actor/action-type
/// consecutive Medium-tier denials (§13.5), folding the narrow Trust Evaluation concern (§10.6 —
/// "actor's track record of past Protection outcomes") into this escalation state rather than a
/// separate class, per the WP-012 roadmap's explicit instruction.
/// </summary>
public sealed class RiskEngine
{
    private const int LowTierMaxRiskScore = 30;
    private const int MediumTierMaxRiskScore = 70;
    private const int ConsecutiveMediumDenialsBeforeEscalation = 2;

    private readonly Dictionary<(string Actor, string ActionType), int> _consecutiveMediumDenials = new();
    private readonly object _lock = new();

    public RiskAssessment Assess(string actor, string actionType, int riskScore)
    {
        var tier = ClassifyTier(riskScore);

        lock (_lock)
        {
            var priorDenials = _consecutiveMediumDenials.GetValueOrDefault((actor, actionType));

            if (tier == RiskTier.Medium && priorDenials >= ConsecutiveMediumDenialsBeforeEscalation)
            {
                return new RiskAssessment(RiskTier.High, Escalated: true);
            }
        }

        return new RiskAssessment(tier, Escalated: false);
    }

    public void RecordMediumTierDenial(string actor, string actionType)
    {
        lock (_lock)
        {
            var key = (actor, actionType);
            _consecutiveMediumDenials[key] = _consecutiveMediumDenials.GetValueOrDefault(key) + 1;
        }
    }

    public void RecordAllow(string actor, string actionType)
    {
        lock (_lock)
        {
            _consecutiveMediumDenials.Remove((actor, actionType));
        }
    }

    private static RiskTier ClassifyTier(int riskScore)
    {
        if (riskScore <= LowTierMaxRiskScore)
        {
            return RiskTier.Low;
        }

        // The 70/71 boundary reuses Constitution §0.6.1's exact existing rule
        // ("Score > 70 always escalates one tier") verbatim.
        return riskScore <= MediumTierMaxRiskScore ? RiskTier.Medium : RiskTier.High;
    }
}
