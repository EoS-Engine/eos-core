using EOS.Contracts;
using EOS.Knowledge;
using EOS.KnowledgeGraph;
using Microsoft.Extensions.Logging;

namespace EOS.Runner.Commands;

public sealed class AskCommand(
    IReasoningEngineClient reasoningEngine,
    IProtectionClient protectionClient,
    IKnowledgeClient knowledgeClient,
    ILogger<AskCommand> logger)
{
    private const string RequestingRole = "HumanOperator";

    public async Task<int> ExecuteAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            logger.LogError("Malformed request: no text was provided to 'ask'.");
            return 1;
        }

        var request = new ReasoningRequest(
            RequestId: Guid.NewGuid(),
            CorrelationId: Guid.NewGuid(),
            Goal: text,
            RequestingRole: RequestingRole);

        Decision decision;
        try
        {
            var decisions = await reasoningEngine.ReasonAsync(request, cancellationToken);
            decision = decisions[0];
        }
        catch (ReasoningFailedException ex)
        {
            logger.LogError("Reasoning failed: {FailureMode} - {Message}", ex.FailureMode, ex.Message);
            return 1;
        }

        var actionRequest = new ActionRequest(
            ActionId: decision.DecisionId,
            ActionType: "Decision",
            Actor: request.RequestingRole,
            RiskScore: (int)Math.Round(decision.RiskScore));

        var validationResult = protectionClient.Validate(actionRequest);

        if (validationResult.Verdict != ProtectionVerdict.Allow)
        {
            logger.LogError(
                "Decision {DecisionId} was not allowed: {Verdict} - {Reason}",
                decision.DecisionId, validationResult.Verdict, validationResult.Reason);
            return 1;
        }

        await knowledgeClient.UpdateAsync(
            nodeId: decision.DecisionId,
            nodeType: KnowledgeNodeType.Decision,
            content: decision.SelectedHypothesis,
            domainTags: [],
            evidenceRefs: decision.EvidenceRefs,
            cancellationToken: cancellationToken);

        Console.WriteLine(decision.SelectedHypothesis);
        return 0;
    }
}
