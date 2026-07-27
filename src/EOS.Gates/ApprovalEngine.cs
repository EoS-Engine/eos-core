using EOS.Contracts;

namespace EOS.Gates;

public sealed record ApprovalDecision(ProtectionVerdict Verdict, string? Reason);

/// <summary>
/// Executes Constitution Part 0 §0.6's Decision Matrix mechanically (Protection-Layer-
/// Specification-v1.0 §10.4) via an internal ActionType-to-row mapping — ActionRequest.ActionType
/// (EOS.Contracts, unchanged) already carries enough expressiveness for this; no contract change
/// is introduced (WP-012 Architecture Freeze, G3).
/// </summary>
public sealed class ApprovalEngine
{
    private static readonly IReadOnlySet<string> HumanRequiredActionTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Constitutional amendment",
            "Security-sensitive change",
            "Disaster recovery invocation",
        };

    public ApprovalDecision Resolve(string actionType)
    {
        if (HumanRequiredActionTypes.Contains(actionType))
        {
            return new ApprovalDecision(
                ProtectionVerdict.Defer,
                $"Decision Matrix (Constitution Part 0 §0.6): '{actionType}' requires human sign-off.");
        }

        return new ApprovalDecision(ProtectionVerdict.Allow, Reason: null);
    }
}
