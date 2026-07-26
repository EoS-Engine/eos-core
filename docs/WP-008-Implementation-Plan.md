# WP-008 Implementation Plan — Reasoning Engine: Minimal Pipeline

**Revision:** 1 (Final, Approved)
**Source of Truth (priority order):** `docs/Development-Workflow.md`, `docs/EOS-Specification.md`, `docs/EOS-Implementation-Roadmap-v1.0.md` (WP-008 row), `docs/Reasoning-Engine-Specification-v1.0.md`, the approved WP-008 Architecture Gap Analysis, this plan.

## Objective (roadmap, verbatim)

Implement enough of the 12-stage Reasoning pipeline to produce one well-formed, evidence-backed `Decision` — not the full reasoning-type catalog yet.

## Architecture Decisions (from the approved Gap Analysis, not reopened)

1. `IReasoningEngineClient`, `ReasoningRequest`, `Decision`, `Explanation`, `ReasoningType`, `ReasoningFailureMode` live in `EOS.Contracts`.
2. `EOS.Reasoning.csproj` and `tests/EOS.Reasoning.Tests.csproj` gain a `ProjectReference` to `EOS.AIProvider`; `OnlyAllowedProjectsMayReferenceAIProviderTests`'s whitelist extended to include `EOS.Reasoning`, `EOS.Reasoning.Tests`.
3. Stage 1 processes only `ReasoningRequest.Goal` — no `assemble_context()` call.
4. `evidence_refs` = `[$"inference:{inferenceRequest.RequestId}"]` — a real, traceable reference to the actual inference call.
5. `confidence` = fixed `0.5` when inference succeeds with non-empty output ("single real, unweighted, uncorroborated source"); a failed/empty inference never reaches Stage 10.
6. `risk_score` = fixed `0` (structural placeholder, not required non-placeholder by roadmap).
7. `reasoning_type_applied` = fixed `EngineeringReasoning`.
8. Single hypothesis = the raw `InferenceResult.Output`; `rejected_hypotheses = []`.
9. Only `ReasonAsync()` implemented; `compare()`/`get_trust_signal()`/`summarize()`/`query_history()` omitted.
10. No event emission this WP.

## Included Scope (roadmap, verbatim)

`IReasoningEngineClient.reason()` with a single-hypothesis, minimal-stage pipeline; a real, non-empty `Explanation` object; real `evidence_refs`/`confidence` population (no placeholder values).

## Explicitly Excluded Scope (roadmap, verbatim)

The full 12-stage pipeline including Intent Analysis, Constraint Evaluation, Hypothesis Generation (multi-hypothesis), Alternative Exploration, Trade-off Analysis (WP-019); all 13 reasoning types beyond the one implicit default (WP-019); `compare()`, `get_trust_signal()`, `summarize()` (WP-020); Decision Ranking, Decision History (WP-020).

## Vertical Slice Definition

`ReasoningRequest` (`EOS.Contracts`) → `IReasoningEngineClient.ReasonAsync()` → `ReasoningEngine` (`EOS.Reasoning`) → real `InferenceRequest` → `IAIProviderClient.InferAsync()` (`OllamaProviderAdapter`, real Ollama) → `Decision` (`EOS.Contracts`) with non-empty `EvidenceRefs`, `Confidence`, and `Explanation`.

## Projects Affected

`EOS.Contracts`, `EOS.Reasoning`.

## Files to Create

- `src/EOS.Contracts/IReasoningEngineClient.cs`, `ReasoningRequest.cs`, `Decision.cs`, `Explanation.cs`, `ReasoningType.cs`, `ReasoningFailureMode.cs`, `ReasoningFailedException.cs`
- `src/EOS.Reasoning/ReasoningEngine.cs`
- `tests/EOS.Reasoning.Tests/EOS.Reasoning.Tests.csproj`, `ReasoningEngineTests.cs`, `ReasoningEngineIntegrationTests.cs`
- `docs/work-packages/WP-008-Completion-Report.md` (at closure)

## Files to Modify

- `src/EOS.Reasoning/EOS.Reasoning.csproj` — add `ProjectReference` to `EOS.AIProvider`.
- `tests/EOS.ArchitectureTests/OnlyAllowedProjectsMayReferenceAIProviderTests.cs` — extend whitelist.
- `EOS.slnx` — register `tests/EOS.Reasoning.Tests`.

## Files That Must NOT Change

`src/EOS.Runner/**`, `src/EOS.Knowledge/**`, `src/EOS.KnowledgeGraph/**`, `src/EOS.Gates/**`, `src/EOS.Learning/**`, `src/EOS.Planner/**`, `src/EOS.AIProvider/**`, `src/EOS.SDK/**`, `config/*.json`, any specification/roadmap/Constitution document.

## Dependency Changes

`EOS.Reasoning → EOS.AIProvider` (new `ProjectReference`, Constitution-permitted, WP-005-deferred). No package added.

## Package Changes

None.

## Public Contracts

```csharp
public enum ReasoningType { EngineeringReasoning }
public enum ReasoningFailureMode { MissingContext, ConflictingEvidence, InvalidGoal, AmbiguousRequest, UnsupportedTask, InternalError }

public sealed record ReasoningRequest(Guid RequestId, Guid CorrelationId, string Goal, string RequestingRole);

public sealed record Explanation(
    string Why, string[] EvidenceUsed, string[] Assumptions,
    (string Hypothesis, string Reason)[] AlternativesRejected,
    string ConfidenceRationale, string[] Risks);

public sealed record Decision(
    Guid DecisionId, Guid RequestId, ReasoningType ReasoningTypeApplied,
    string SelectedHypothesis, string[] RejectedHypotheses,
    string[] EvidenceRefs, double Confidence, Explanation Explanation,
    string TradeOffs, double RiskScore, bool Reproducible, DateTimeOffset OccurredAt);

public interface IReasoningEngineClient
{
    Task<Decision[]> ReasonAsync(ReasoningRequest request, CancellationToken cancellationToken = default);
}

public sealed class ReasoningEngine(IAIProviderClient aiProviderClient) : IReasoningEngineClient;
```

## Test Strategy

Unit: `InvalidGoal` failure on empty/whitespace `Goal`; `Decision`/`Explanation` field population per Decisions 4–8. Integration (real Ollama, no mocks): `reason("explain the SOLID principles")` returns one `Decision` with non-empty evidence, confidence, and explanation — the roadmap's acceptance criterion verbatim.

## Acceptance Criteria (roadmap, verbatim)

`reason("explain the SOLID principles")` returns a `Decision` with non-empty evidence, confidence, and explanation.

## Implementation Sequence

1. Feature branch `wp-008-reasoning-minimal-pipeline` (created).
2. This plan document.
3. `EOS.Contracts` types.
4. `EOS.Reasoning.csproj` + `OnlyAllowedProjectsMayReferenceAIProviderTests` whitelist extension.
5. `ReasoningEngine`.
6. `EOS.Reasoning.Tests`.
7. Register test project in `EOS.slnx`.
8. Full Local Verification.
9. Architecture Gate self-review.
10. Stop for approval before push/PR.
