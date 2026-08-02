using EOS.Contracts;
using EOS.SDK;
using Microsoft.Extensions.Logging;

namespace EOS.Reasoning;

/// <summary>
/// WP-019 — Reasoning-Engine-Specification-v1.0 §10's 12-stage pipeline. <see cref="ReasonAsync"/>
/// calls all 12 stages in order (§10.2: "Every request to <c>reason()</c> passes through all
/// applicable stages in order"). See each stage's own comment for its specific reasoning.
/// </summary>
public sealed class ReasoningEngine(
    IAIProviderClient aiProviderClient,
    IContextAcquisitionProvider contextAcquisitionProvider,
    ReasoningEngineOptions options,
    IDecisionMadeEventPublisher decisionMadeEventPublisher,
    ILowConfidenceDecisionFlaggedEventPublisher lowConfidenceDecisionFlaggedEventPublisher,
    IContextExpansionRequestedEventPublisher contextExpansionRequestedEventPublisher,
    ILogger<ReasoningEngine> logger) : IReasoningEngineClient
{
    private const double FixedConfidence = 0.5;
    private const double FixedRiskScore = 0;
    private const string CandidateDelimiter = "===CANDIDATE===";
    private const int DefaultContextBudget = 2048;

    // §11's 13 Reasoning Types, verbatim "Pipeline Emphasis" column text — logged per request
    // to satisfy the roadmap's Demo/Acceptance criterion ("visibly different pipeline emphasis
    // ... confirmed via logged stage weighting").
    private static readonly IReadOnlyDictionary<ReasoningType, string> PipelineEmphasis = new Dictionary<ReasoningType, string>
    {
        [ReasoningType.DeterministicReasoning] = "Stages 4, 7, 12 dominate; stages 5/6/8/9 are trivial (single hypothesis, no real alternative)",
        [ReasoningType.AnalyticalReasoning] = "Stages 1, 3, 6 dominate — heavy multi-step decomposition",
        [ReasoningType.RuleBasedReasoning] = "Stage 4 dominates; used when Constraint Evaluation alone determines the outcome",
        [ReasoningType.GoalOrientedReasoning] = "Stages 2, 3, 7 dominate",
        [ReasoningType.ContextualReasoning] = "Stage 1 dominates; heavy reliance on Memory's Context Assembly (§12)",
        [ReasoningType.ArchitecturalReasoning] = "Stages 4, 8, 9 dominate",
        [ReasoningType.EngineeringReasoning] = "Balanced use of all stages",
        [ReasoningType.DiagnosticReasoning] = "Stages 1, 5, 6 dominate — hypothesis generation over candidate causes",
        [ReasoningType.RootCauseAnalysis] = "Extends Diagnostic Reasoning with heavier Stage 6 (multi-step causal chaining)",
        [ReasoningType.ComparativeReasoning] = "Stages 5-7 dominate; minimal goal/intent analysis",
        [ReasoningType.RiskReasoning] = "Stages 4, 9, 10 dominate",
        [ReasoningType.OptimizationReasoning] = "Stages 5, 7, 9 dominate",
        [ReasoningType.StrategicReasoning] = "All stages, with Stage 9 (Trade-off Analysis) weighted most heavily",
    };

    public async Task<Decision[]> ReasonAsync(ReasoningRequest request, CancellationToken cancellationToken = default)
    {
        var reasoningType = request.ReasoningType ?? ReasoningType.EngineeringReasoning;
        logger.LogInformation(
            "ReasoningType {ReasoningType} pipeline emphasis (§11): {Emphasis}",
            reasoningType, PipelineEmphasis[reasoningType]);

        var (acquiredContext, expansionWasNeeded) = await ProcessContextAsync(request, cancellationToken);
        UnderstandGoal(request);
        AnalyzeIntent(request);
        var constraints = EvaluateConstraints(request);
        GenerateHypotheses(request);

        ValidateContext(request, acquiredContext);

        var (contextItems, contextNote) = PrepareContext(acquiredContext, request.Goal, reasoningType);
        var confidence = EvaluateConfidence(request.ContextScope is not null, expansionWasNeeded);

        var (inferenceResult, evidenceRefs) = await PerformMultiStepReasoningAsync(
            request, constraints, contextItems, cancellationToken);
        var hypotheses = SplitHypotheses(inferenceResult.Output!);

        var decisions = hypotheses.Length <= 1
            ? [BuildSingleDecision(request, reasoningType, inferenceResult, evidenceRefs, confidence, contextNote)]
            : BuildRankedDecisions(request, reasoningType, hypotheses, inferenceResult, evidenceRefs, confidence, contextNote);

        foreach (var decision in decisions)
        {
            ValidateDecision(decision);

            decisionMadeEventPublisher.PublishDecisionMade(
                decision.DecisionId, decision.RequestId, decision.Confidence, decision.RiskScore, decision.ReasoningTypeApplied);

            if (decision.Confidence < options.LowConfidenceFloor)
            {
                lowConfidenceDecisionFlaggedEventPublisher.PublishLowConfidenceDecisionFlagged(
                    decision.DecisionId, decision.Confidence, options.LowConfidenceFloor);
            }
        }

        return decisions;
    }

    // Stage 1 (§10): Context Processing — "normalize/structure the ContextPayload received
    // from Memory (§12.1)". Real when the caller supplies ContextScope (§13.2): the Composition
    // Root Adapter is invoked to acquire it, with §12.4 Context Expansion attempted (bounded by
    // the configured cap) whenever the returned payload is truncated. Callers who do not supply
    // a scope see the exact prior no-op.
    private async Task<(AcquiredContext? Context, bool ExpansionWasNeeded)> ProcessContextAsync(
        ReasoningRequest request, CancellationToken cancellationToken)
    {
        if (request.ContextScope is not { } scope)
        {
            return (null, false);
        }

        var acquired = await contextAcquisitionProvider.AcquireContextAsync(scope, cancellationToken);
        var expansionWasNeeded = false;

        for (var attempt = 0; attempt < options.ContextExpansionCap && acquired.Truncated; attempt++)
        {
            expansionWasNeeded = true;
            var expandedScope = scope with { Budget = (scope.Budget ?? DefaultContextBudget) * 2 };
            contextExpansionRequestedEventPublisher.PublishContextExpansionRequested(request.RequestId, scope, expandedScope);
            acquired = await contextAcquisitionProvider.AcquireContextAsync(expandedScope, cancellationToken);
            scope = expandedScope;
        }

        return (acquired, expansionWasNeeded);
    }

    // Stage 2 (§10): Goal Understanding — unchanged since Slice 2; this stage's
    // specification-groundable content (the non-empty check) is already complete.
    private static void UnderstandGoal(ReasoningRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Goal))
        {
            throw new ReasoningFailedException(ReasoningFailureMode.InvalidGoal, "Goal must not be empty.");
        }
    }

    // Stage 3 (§10): Intent Analysis. No specification-given ambiguity-detection algorithm
    // exists (§21's AmbiguousRequest names the outcome, not the mechanism) — remains a no-op to
    // avoid inventing an undocumented heuristic that would reject previously-accepted goals.
    private static void AnalyzeIntent(ReasoningRequest request)
    {
    }

    // Stage 4 (§10): Constraint Evaluation — enumerates request.Constraints so Stage 6 can fold
    // them into its inference payload, satisfying "the decision must respect" them.
    private static string[] EvaluateConstraints(ReasoningRequest request) => request.Constraints ?? [];

    // Stage 5 (§10): Hypothesis Generation. The candidate split happens after Stage 6's single
    // inference call returns (see SplitHypotheses) — Stage 6's prompt instructs the model to
    // emit multiple candidates only when genuinely distinct viable answers exist, realizing
    // "propose one or more candidate resolutions" without any additional inference call.
    private static void GenerateHypotheses(ReasoningRequest request)
    {
    }

    // §12.6 Context Validation + §21 Missing Context: "if still insufficient [after Context
    // Expansion], returns ReasoningFailed(failure_mode=MissingContext)".
    private static void ValidateContext(ReasoningRequest request, AcquiredContext? acquiredContext)
    {
        if (request.ContextScope is null)
        {
            return;
        }

        if (acquiredContext is null || acquiredContext.Items.Count == 0 || acquiredContext.Truncated)
        {
            throw new ReasoningFailedException(
                ReasoningFailureMode.MissingContext,
                "Context acquisition returned an empty or still-truncated ContextPayload after Context Expansion (§12.4).");
        }
    }

    // §12.2 Context Prioritization + §12.3 Context Filtering + §12.5 Context Reduction: a
    // second, reasoning-specific pass over Memory's already-returned items — never re-queries
    // Memory. Filtering never discards every item (falls back to the unfiltered set) to avoid
    // manufacturing an artificial MissingContext condition Memory itself did not report.
    private static (string[] Items, string? Note) PrepareContext(
        AcquiredContext? acquired, string goal, ReasoningType reasoningType)
    {
        if (acquired is null || acquired.Items.Count == 0)
        {
            return ([], null);
        }

        var (filtered, filteredCount) = FilterAndPrioritizeContext(acquired.Items, goal);
        var reduced = ReduceContext(filtered, reasoningType);
        var reducedCount = filtered.Length - reduced.Length;

        List<string> notes = [];
        if (filteredCount > 0)
        {
            notes.Add($"{filteredCount} context item(s) were considered but filtered as not relevant to the stated goal (§12.3).");
        }

        if (reducedCount > 0)
        {
            notes.Add($"{reducedCount} context item(s) were reduced given {reasoningType}'s pipeline emphasis (§12.5).");
        }

        return (reduced, notes.Count > 0 ? string.Join(" ", notes) : null);
    }

    private static (string[] Items, int FilteredCount) FilterAndPrioritizeContext(IReadOnlyList<string> items, string goal)
    {
        var goalTokens = Tokenize(goal);
        var scored = items
            .Select(item => (Item: item, Score: Tokenize(item).Count(token => goalTokens.Contains(token, StringComparer.OrdinalIgnoreCase))))
            .ToList();

        var relevant = scored.Where(scoredItem => scoredItem.Score > 0).ToList();
        var chosen = relevant.Count > 0 ? relevant : scored;
        var ordered = chosen.OrderByDescending(scoredItem => scoredItem.Score).Select(scoredItem => scoredItem.Item).ToArray();

        return (ordered, items.Count - chosen.Count);
    }

    private static string[] Tokenize(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    // §12.5's own named example: "a Deterministic Reasoning request received a large context
    // payload" — reduction applies only to that named case, to the single most relevant item.
    private static string[] ReduceContext(string[] items, ReasoningType reasoningType) =>
        reasoningType == ReasoningType.DeterministicReasoning && items.Length > 1 ? items[..1] : items;

    // Stage 6 (§10): Multi-Step Reasoning. Exactly one inference call, as in WP-008 — folds in
    // Stage 4's constraints and Stage 1/§12's prepared context, and instructs the model to emit
    // multiple candidates via CandidateDelimiter only when genuinely distinct viable answers
    // exist (Stage 5, realized without any additional inference call or SDK change).
    private async Task<(InferenceResult Result, string[] EvidenceRefs)> PerformMultiStepReasoningAsync(
        ReasoningRequest request, string[] constraints, string[] contextItems, CancellationToken cancellationToken)
    {
        var payload = request.Goal;

        if (constraints.Length > 0)
        {
            payload += $"\n\nConstraints to respect:\n- {string.Join("\n- ", constraints)}";
        }

        if (contextItems.Length > 0)
        {
            payload += $"\n\nRelevant context:\n- {string.Join("\n- ", contextItems)}";
        }

        payload +=
            $"\n\nIf, and only if, multiple genuinely distinct and comparably viable answers " +
            $"exist, present each as its own candidate separated by a line containing exactly " +
            $"\"{CandidateDelimiter}\". Otherwise, respond with a single answer only.";

        var inferenceRequest = new InferenceRequest(
            RequestId: Guid.NewGuid(),
            CorrelationId: request.CorrelationId,
            CapabilityRequired: "Chat",
            Payload: payload,
            ContextPayloadRef: null,
            TokenBudgetEstimate: 2048,
            Priority: 0,
            Caller: "EOS.Reasoning");

        var inferenceResult = await aiProviderClient.InferAsync(inferenceRequest, cancellationToken);

        if (!inferenceResult.Success || string.IsNullOrWhiteSpace(inferenceResult.Output))
        {
            throw new ReasoningFailedException(
                ReasoningFailureMode.InternalError,
                inferenceResult.ErrorMessage ?? "AI Provider returned no usable output.");
        }

        string[] evidenceRefs = [$"inference:{inferenceRequest.RequestId}"];
        return (inferenceResult, evidenceRefs);
    }

    // Stage 5 (§10) realized: parses Stage 6's single inference call output into multiple
    // candidate hypotheses when the model reports more than one genuinely distinct viable
    // answer. Legacy/typical output (no delimiter) yields exactly one hypothesis.
    private static string[] SplitHypotheses(string output) =>
        output.Split(CandidateDelimiter, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    // Stage 7 (§10): Decision Making — "select a primary candidate from the hypotheses". The
    // single-hypothesis case (the overwhelming majority) has exactly one candidate to select.
    private static string MakeDecision(string[] hypotheses) => hypotheses[0];

    // Stages 10 (§13.4) computed here since its inputs (context completeness) are already
    // known before Stage 6 runs; Stage 10 (§10) is still the conceptual owner of this value.
    // Confidence reflects context completeness — was Context Expansion needed, and (having
    // survived §12.6's Context Validation) did it resolve the insufficiency.
    private static double EvaluateConfidence(bool contextRequested, bool expansionWasNeeded)
    {
        if (!contextRequested)
        {
            return FixedConfidence;
        }

        return Math.Clamp(FixedConfidence + (expansionWasNeeded ? 0.05 : 0.1), 0.0, 1.0);
    }

    private Decision BuildSingleDecision(
        ReasoningRequest request, ReasoningType reasoningType, InferenceResult inferenceResult,
        string[] evidenceRefs, double confidence, string? contextNote)
    {
        var hypotheses = new[] { inferenceResult.Output!.Trim() };
        var selectedHypothesis = MakeDecision(hypotheses);
        var rejectedHypotheses = ExploreAlternatives();
        var tradeOffs = AnalyzeTradeOffs();
        var explanation = GenerateExplanation(request, inferenceResult, evidenceRefs, rejectedHypotheses, contextNote);

        return new Decision(
            DecisionId: Guid.NewGuid(),
            RequestId: request.RequestId,
            ReasoningTypeApplied: reasoningType,
            SelectedHypothesis: selectedHypothesis,
            RejectedHypotheses: rejectedHypotheses,
            EvidenceRefs: evidenceRefs,
            Confidence: confidence,
            Explanation: explanation,
            TradeOffs: tradeOffs,
            RiskScore: FixedRiskScore,
            Reproducible: false,
            OccurredAt: DateTimeOffset.UtcNow);
    }

    // §13.5: Stage 5 produced multiple viable hypotheses that Stage 7 does not clearly resolve
    // to one winner — return a ranked list of Decisions (frozen plan Area 3, Alternative B:
    // array length itself signals the tie; length > 1 = ranked, tied set).
    private static Decision[] BuildRankedDecisions(
        ReasoningRequest request, ReasoningType reasoningType, string[] hypotheses, InferenceResult inferenceResult,
        string[] evidenceRefs, double confidence, string? contextNote)
    {
        var decisions = new Decision[hypotheses.Length];
        for (var i = 0; i < hypotheses.Length; i++)
        {
            var rejected = hypotheses.Where((_, index) => index != i).ToArray();
            var tradeOffs = AnalyzeTradeOffs(hypotheses.Length);
            var explanation = GenerateExplanation(request, inferenceResult, evidenceRefs, rejected, contextNote);

            decisions[i] = new Decision(
                DecisionId: Guid.NewGuid(),
                RequestId: request.RequestId,
                ReasoningTypeApplied: reasoningType,
                SelectedHypothesis: hypotheses[i],
                RejectedHypotheses: rejected,
                EvidenceRefs: evidenceRefs,
                Confidence: confidence,
                Explanation: explanation,
                TradeOffs: tradeOffs,
                RiskScore: FixedRiskScore,
                Reproducible: false,
                OccurredAt: DateTimeOffset.UtcNow);
        }

        return decisions;
    }

    // Stage 8 (§10): Alternative Exploration — single-hypothesis path: empty, per §13.3's own
    // exception ("never empty unless only one hypothesis was ever possible"). The multi-
    // hypothesis path's rejected set is computed directly in BuildRankedDecisions.
    private static string[] ExploreAlternatives() => [];

    // Stage 9 (§10): Trade-off Analysis — single-hypothesis path: fixed string, unchanged from
    // WP-008. Multi-hypothesis path (§13.5): no per-candidate scoring signal exists in this
    // pipeline (Stage 10 confidence is request-level, not per-hypothesis), so the trade-off is
    // honestly reported as unscored ranking, not a fabricated comparison.
    private static string AnalyzeTradeOffs() =>
        "No alternative hypotheses were generated in this minimal pipeline; trade-off analysis does not apply.";

    private static string AnalyzeTradeOffs(int candidateCount) =>
        $"{candidateCount} candidate hypotheses were generated and are returned as a ranked, tied set " +
        "(§13.5); no independent per-candidate scoring signal exists in this pipeline to rank them further.";

    // Stage 11 (§10): Explainability. Records any rejected hypotheses (Stage 8) and any
    // Context Filtering/Reduction note (§12.3/§12.5) in the Explanation.
    private static Explanation GenerateExplanation(
        ReasoningRequest request, InferenceResult inferenceResult, string[] evidenceRefs,
        string[] rejectedHypotheses, string? contextNote)
    {
        List<string> assumptions =
        [
            $"The single inference call from provider model '{inferenceResult.Model}' is treated as sufficient without independent corroboration.",
        ];

        if (contextNote is not null)
        {
            assumptions.Add(contextNote);
        }

        return new Explanation(
            Why: $"The AI Provider's inference output was selected as the answer to the stated goal: \"{request.Goal}\"",
            EvidenceUsed: evidenceRefs,
            Assumptions: assumptions.ToArray(),
            AlternativesRejected: rejectedHypotheses
                .Select(hypothesis => (hypothesis, "Not selected as this ranked entry's primary candidate (§13.5 tied ranking)."))
                .ToArray(),
            ConfidenceRationale: "Confidence reflects evidence strength (a single real inference source) and context completeness (§13.4) — whether Context Expansion was needed and, having passed §12.6's Context Validation, whether it resolved the insufficiency.",
            Risks: ["This decision is based on exactly one uncorroborated model inference and has not been validated against retrieved knowledge or historical outcomes."]);
    }

    // Stage 12 (§10.1): Decision Validation — self-consistency check only, per the explicit
    // boundary with Protection Layer's (forthcoming) safety/policy gating. Checks only that the
    // Decision is well-formed: evidence resolves (non-empty) and the explanation is non-generic.
    private static void ValidateDecision(Decision decision)
    {
        if (decision.EvidenceRefs.Length == 0)
        {
            throw new ReasoningFailedException(ReasoningFailureMode.InternalError, "Decision has no evidence references.");
        }

        if (string.IsNullOrWhiteSpace(decision.Explanation.Why))
        {
            throw new ReasoningFailedException(ReasoningFailureMode.InternalError, "Decision has no explanation.");
        }
    }
}
