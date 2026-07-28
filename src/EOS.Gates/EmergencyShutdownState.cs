using EOS.Contracts;

namespace EOS.Gates;

/// <summary>
/// Owns the entire Emergency Shutdown lifecycle (Protection-Layer-Specification-v1.0 §12.5,
/// §26.1) — activation, clearing, and recognition of its own control ActionTypes. Modeled as a
/// stateful flag rather than a fifth PolicyEngine tier, since Emergency Shutdown halts all new
/// dispatch indiscriminately rather than matching per-ActionType rules (WP-013 Implementation
/// Plan, Decision 6). No Actor-to-Authority-Level verification is performed — the caller-
/// asserted ActionType is trusted, matching this codebase's established trust boundary (WP-013
/// Architecture Challenge, G3; precedent: AskCommand's "HumanOperator" RequestingRole, WP-009).
/// </summary>
public sealed class EmergencyShutdownState
{
    private const string ActivateActionType = "EmergencyShutdown";
    private const string ClearActionType = "EmergencyShutdownCleared";

    private readonly object _lock = new();
    private bool _isActive;

    /// <summary>
    /// Recognizes and processes the two Emergency Shutdown control actions, and — while
    /// shutdown is active — holds every other action at Defer (§26.1). Returns the resulting
    /// ValidationResult whenever <paramref name="action"/> is a control action or shutdown is
    /// currently active; returns null only when <paramref name="action"/> is unrelated to
    /// Emergency Shutdown and shutdown is not active, meaning the caller should proceed with
    /// normal validation.
    /// </summary>
    public ValidationResult? TryHandleControlAction(ActionRequest action)
    {
        if (string.Equals(action.ActionType, ActivateActionType, StringComparison.OrdinalIgnoreCase))
        {
            lock (_lock)
            {
                _isActive = true;
            }

            return new ValidationResult(
                ProtectionVerdict.Allow,
                RiskTier.High,
                $"Emergency Shutdown activated by {action.Actor}; all new dispatch is held pending clearance.");
        }

        if (string.Equals(action.ActionType, ClearActionType, StringComparison.OrdinalIgnoreCase))
        {
            lock (_lock)
            {
                _isActive = false;
            }

            return new ValidationResult(
                ProtectionVerdict.Allow,
                RiskTier.High,
                $"Emergency Shutdown cleared by {action.Actor}; normal operation resumed.");
        }

        lock (_lock)
        {
            if (_isActive)
            {
                return new ValidationResult(
                    ProtectionVerdict.Defer,
                    RiskTier.High,
                    "Emergency Shutdown is active; new dispatch is held pending clearance.");
            }
        }

        return null;
    }
}
