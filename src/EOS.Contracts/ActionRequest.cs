namespace EOS.Contracts;

public sealed record ActionRequest(
    Guid ActionId,
    string ActionType,
    string Actor,
    int RiskScore);
