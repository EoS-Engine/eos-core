namespace EOS.Gates;

public sealed record PolicyEntry(string ActionType, string Verdict, string Reason);

public sealed record PolicyDecision(bool Allow, string? Reason);

public sealed class PolicyEngine(
    IReadOnlyList<PolicyEntry> globalPolicies,
    IReadOnlyList<PolicyEntry> projectPolicies,
    IReadOnlyList<PolicyEntry> userPolicies,
    IReadOnlyList<PolicyEntry> runtimePolicies)
{
    public PolicyDecision Evaluate(string actionType)
    {
        // Precedence order (Constitution/Protection-Layer-Specification §12.6): Global > Project > User > Runtime.
        // Emergency Policies (§12.5) are represented separately, by EmergencyShutdownState — not
        // as a fifth tier here — since Emergency Shutdown halts all new dispatch indiscriminately
        // rather than matching per-ActionType rules like the four tiers below (WP-013).
        foreach (var tier in new[] { globalPolicies, projectPolicies, userPolicies, runtimePolicies })
        {
            var match = tier.FirstOrDefault(policy =>
                string.Equals(policy.ActionType, actionType, StringComparison.OrdinalIgnoreCase)
                || policy.ActionType == "*");

            if (match is not null)
            {
                var allow = string.Equals(match.Verdict, "Allow", StringComparison.OrdinalIgnoreCase);
                return new PolicyDecision(allow, allow ? null : match.Reason);
            }
        }

        return new PolicyDecision(Allow: true, Reason: null);
    }
}
