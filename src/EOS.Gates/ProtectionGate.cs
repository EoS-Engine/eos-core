using EOS.Contracts;
using Microsoft.Extensions.Logging;

namespace EOS.Gates;

public sealed class ProtectionGate(
    PolicyEngine policyEngine,
    RuleEngine ruleEngine,
    RiskEngine riskEngine,
    ApprovalEngine approvalEngine,
    ILogger<ProtectionGate> logger) : IProtectionClient
{
    public ValidationResult Validate(ActionRequest action)
    {
        if (action.RiskScore is < 0 or > 100)
        {
            var invalid = new ValidationResult(
                ProtectionVerdict.Deny,
                RiskTier.High,
                $"RiskScore {action.RiskScore} is outside the valid 0-100 range; failing closed.");
            Log(action, invalid);
            return invalid;
        }

        if (string.IsNullOrWhiteSpace(action.Actor) || string.IsNullOrWhiteSpace(action.ActionType))
        {
            var invalid = new ValidationResult(
                ProtectionVerdict.Deny,
                RiskTier.High,
                "Request Validation failed: malformed action.");
            Log(action, invalid);
            return invalid;
        }

        var assessment = riskEngine.Assess(action.Actor, action.ActionType, action.RiskScore);

        var result = assessment.Tier switch
        {
            RiskTier.Low => ValidateLowTier(action),
            RiskTier.Medium => ValidateMediumTier(action),
            RiskTier.High => ValidateHighTier(action),
            _ => throw new ArgumentOutOfRangeException(nameof(action), assessment.Tier, "Unrecognized risk tier."),
        };

        Log(action, result);
        return result;
    }

    private ValidationResult ValidateLowTier(ActionRequest action)
    {
        // Protection-Layer-Specification-v1.0 §14.1: Low tier is async-log only, never blocks (FR-P1).
        riskEngine.RecordAllow(action.Actor, action.ActionType);
        return new ValidationResult(ProtectionVerdict.Allow, RiskTier.Low, Reason: null);
    }

    private ValidationResult ValidateMediumTier(ActionRequest action)
    {
        // §14.1 Medium tier: quick synchronous permission + resource-budget check. No real
        // Permission Model actor-authority data or Scheduler resource-budget infrastructure
        // exists yet in this codebase (WP-012 Implementation Plan) — the Policy Engine pass
        // below is the one real, evidenced check available at this tier this WP.
        var denial = CheckPolicy(action, RiskTier.Medium);
        if (denial is not null)
        {
            riskEngine.RecordMediumTierDenial(action.Actor, action.ActionType);
            return denial;
        }

        riskEngine.RecordAllow(action.Actor, action.ActionType);
        return new ValidationResult(ProtectionVerdict.Allow, RiskTier.Medium, Reason: null);
    }

    private ValidationResult ValidateHighTier(ActionRequest action)
    {
        // §14.2 Full Pipeline (High tier only) — six steps, short-circuits on first denial (FR-P3).
        // Step 1 (Request Validation) already ran in Validate() before tier dispatch.
        // Steps 2-5 (Context/Knowledge/Decision/Resource Validation): this WP's ActionRequest
        // carries no ContextPayload/Explanation/Confidence/resource-budget data, and no real
        // Scheduler budget infrastructure exists yet (Constitution Part 7) — each step passes
        // absent the data it would otherwise check (WP-012 Implementation Plan).

        // Step 6: Policy Validation.
        var policyDenial = CheckPolicy(action, RiskTier.High);
        if (policyDenial is not null)
        {
            return policyDenial;
        }

        var ruleDecision = ruleEngine.Evaluate(action.ActionType);
        if (!ruleDecision.Allow)
        {
            return new ValidationResult(ProtectionVerdict.Deny, RiskTier.High, ruleDecision.Reason);
        }

        // Decision Matrix routing (Constitution §0.6, §10.4) — applied after the pipeline clears.
        var approval = approvalEngine.Resolve(action.ActionType);
        return new ValidationResult(approval.Verdict, RiskTier.High, approval.Reason);
    }

    private ValidationResult? CheckPolicy(ActionRequest action, RiskTier tier)
    {
        var decision = policyEngine.Evaluate(action.ActionType);
        return decision.Allow ? null : new ValidationResult(ProtectionVerdict.Deny, tier, decision.Reason);
    }

    private void Log(ActionRequest action, ValidationResult result)
    {
        logger.LogInformation(
            "Protection validate: ActionId={ActionId} ActionType={ActionType} Actor={Actor} RiskScore={RiskScore} Tier={Tier} Verdict={Verdict}",
            action.ActionId, action.ActionType, action.Actor, action.RiskScore, result.Tier, result.Verdict);
    }
}
