# WP-019 Implementation Plan — Reasoning Engine: Full 12-Stage Pipeline & Reasoning Types

## Revision and Source of Truth

Revision 3 (FINAL). Restructures Revision 2 to separate **Specification Requirements** (explicitly mandated, no discretion) from **Implementation Decisions** (Specification-compliant but not textually dictated, requiring KISS/YAGNI-justified selection among alternatives), per the final Verification Audit. Built exclusively from: `docs/Reasoning-Engine-Specification-v1.0.md`, `docs/EOS-Implementation-Roadmap-v1.0.md` (WP-019 row), `docs/Development-Workflow.md`, `docs/EOS-Specification.md` (Constitution, Part 1 dependency table only), the approved Phase 1 Repository Identification, and the approved Phase 2 Architecture Review. No other document or prior conversational content was used as evidence. No contradiction was found between the Specification and the Roadmap during this final review.

## Current Repository Baseline

(Carried from Phase 1, unchanged.)

- `src/EOS.Reasoning/ReasoningEngine.cs` — single file, single-inference-call implementation. No stage decomposition, fixed `Confidence = 0.5`, fixed `RiskScore = 0`, always `ReasoningType.EngineeringReasoning`, no Memory context acquisition.
- `src/EOS.Reasoning/EOS.Reasoning.csproj` — references `EOS.Contracts`, `EOS.SDK`, `EOS.AIProvider`.
- `src/EOS.Contracts/IReasoningEngineClient.cs` — one method, `ReasonAsync`.
- `src/EOS.Contracts/ReasoningRequest.cs` — `(Guid RequestId, Guid CorrelationId, string Goal, string RequestingRole)`.
- `src/EOS.Contracts/ReasoningType.cs` — one value, `EngineeringReasoning`.
- `src/EOS.Contracts/Decision.cs` — matches §13.3's Decision Outputs field-for-field already — **no change required**.
- `src/EOS.Contracts/Explanation.cs` — already matches §14's Explanation shape field-for-field, including `AlternativesRejected` already typed as `(string Hypothesis, string Reason)[]` — **no change required**.
- `src/EOS.Contracts/ReasoningFailureMode.cs` — six of six actual §21 failure-mode enum values already exist (`LowConfidence` correctly excluded, since §21 documents it as *not* a `ReasoningFailed` outcome) — **no change required**.
- `src/EOS.SharedKernel/Configuration/ThresholdsOptions.cs` / `config/Thresholds.json` — no Reasoning-specific fields exist yet.
- Branch: `wp-019-reasoning-engine-full-pipeline`, base commit `d90c6dbe65473c16d8225cce23f3a4a871886ca0` (tag `wp-018-complete`), clean working tree, in sync with `origin/main`.

## Objective and Exact Roadmap Scope

**Objective (verbatim):** "Extend WP-008's minimal pipeline to the full 12 stages and all 13 reasoning types."

**Included components (verbatim):** "Intent Analysis, Constraint Evaluation, multi-hypothesis Generation, Alternative Exploration, Trade-off Analysis (the stages WP-008 skipped); all 13 reasoning types' distinct pipeline-stage emphasis; Context Expansion/Reduction/Filtering/Prioritization (§12 of that spec)."

**Explicitly excluded (verbatim):** "`compare()`, `get_trust_signal()`, `summarize()`, `query_history()` (WP-020); Decision Ranking for near-tied hypotheses (included here as it's part of Stage 7, but the caller-facing ranked-list API surface is finalized in WP-020 alongside the other entry points)."

**Test verification (verbatim):** "Unit tests per stage and per reasoning type; regression run of the vertical slice and Milestone 4 integration tests."

**Demo/Acceptance (verbatim):** "A Diagnostic Reasoning request and a Rule-Based Reasoning request produce visibly different pipeline emphasis (confirmed via logged stage weighting) for the same underlying question shape."

## Specification Requirements vs. Implementation Decisions

The following three areas each contain one explicitly-mandated requirement (no discretion) and one genuine Implementation Decision (Specification-compliant either way, requiring KISS/YAGNI selection). They are separated below.

---

### Area 1 — Context Acquisition

**Specification Requirement (not a decision):** `EOS.Reasoning` must call `assemble_context()` (§12.1) without a direct project reference to `EOS.Knowledge`, since Constitution Part 1 §1.2 lists `EOS.Reasoning`'s dependencies as exactly "`EOS.Contracts, EOS.SDK`" (`EOS.Knowledge` absent — contrast `EOS.Learning`'s row, which explicitly includes it). A direct reference is not an available alternative; it is non-compliant by explicit table content, not by preference.

**Implementation Decision: adapter mechanism shape.**

- **Repository Evidence:** `ISummarizer` (WP-016) and `ICompareProvider` (WP-018) are both real, merged instances of a small named interface, defined in the consumer's own project, with the concrete implementation supplied by `EOS.Runner`'s composition root.
- **Specification Evidence:** §12.1 requires the call exists; §19.1's sequence diagram shows the logical call (`Reasoning->>Memory: assemble_context(scope, budget)`) without specifying a C# mechanism. Neither cited document names an adapter pattern.
- **Alternative A:** A `Func<ContextRequest, CancellationToken, Task<ContextPayload>>` delegate passed into `ReasoningEngine`'s constructor.
- **Alternative B:** A small named interface (e.g., `IContextAcquisitionProvider`) defined in `EOS.Reasoning`, implemented by an adapter in `Program.cs`.
- **Selected Alternative:** B.
- **Why Alternative A was rejected:** Not non-compliant — rejected only because it introduces a second style for an already-solved problem in this codebase, adding cognitive overhead for no compliance benefit.
- **Why Alternative B was rejected:** N/A — selected. (Distinguishing note: B is not "more compliant" than A; both satisfy §12.1 and the Part 1 constraint equally. B was chosen on codebase-consistency grounds only.)
- **KISS justification:** B introduces exactly one new type (a one-method interface) — no smaller compliant surface exists once *some* indirection is required.
- **YAGNI justification:** No generalized "Memory access provider" abstraction is introduced; the interface's one method is exactly what §12.1 requires, nothing anticipatory.

---

### Area 2 — `ReasoningRequest` Extension

**Specification Requirement (not a decision):** §13.2's Decision Inputs structure explicitly names `reasoning_type` (inferrable), `constraints[]`, and `context_scope: { domain_tags[], project_scope, budget }` as members of `ReasoningRequest`. Their existence in some form is mandatory; `constraints[]` and `context_scope` carry no "may be inferred" qualifier, so they cannot be derived from `Goal` text alone (§13.2 gives no textual basis for such derivation, and inventing one would be an unjustified abstraction).

`context_scope`'s C# shape must be a new `EOS.Contracts`-local type — this is a **derived requirement**, not a separate decision: `EOS.Contracts` cannot reference `EOS.Knowledge.ContextRequest` (the near-matching existing type), per the same Part 1 §1.2 constraint as Area 1.

**Implementation Decision: additive vs. breaking extension.**

- **Repository Evidence:** WP-017's `IKnowledgeClient.UpdateAsync` extension added a trailing optional `KnowledgeMetadata? metadata = null` parameter without breaking its two prior call sites (mechanical named-argument fix only).
- **Specification Evidence:** §13.2 states the fields exist; it says nothing about whether existing callers of `ReasonAsync` must remain unbroken.
- **Alternative A:** Required (non-optional) new fields, breaking `AskCommand`'s and the test suite's existing call sites.
- **Alternative B:** Additive, optional, trailing parameters.
- **Selected Alternative:** B.
- **Why Alternative A was rejected:** Nothing in the Specification or the WP-019 roadmap row requires breaking existing callers; A's only advantage (no nullability to reason about) does not offset the unnecessary call-site churn it forces.
- **Why Alternative B was rejected:** N/A — selected.
- **KISS justification:** B reuses an already-proven, in-codebase technique rather than introducing a new extension style.
- **YAGNI justification:** B adds exactly the three §13.2-named fields — no builder pattern, no fluent API, no additional convenience fields.

---

### Area 3 — Stage 7 Tie Signalling

**Specification Requirement (not a decision):** Stage 7 (Decision Making) is an Included Component this WP (roadmap: "Decision Ranking for near-tied hypotheses... included here as it's part of Stage 7"). §13.5: "When a request's Stage 5... produces multiple viable hypotheses that Stage 7 does not clearly resolve to one winner... the Reasoning Engine returns a **ranked** list of Decisions rather than forcing a single answer." Implementing this near-tie behavior in `reason()` this WP is mandatory; the roadmap's exclusion is specifically "the caller-facing ranked-list API surface... finalized in WP-020 alongside the other entry points" (i.e., WP-020's four *new* entry points — `compare`/`get_trust_signal`/`summarize`/`query_history` — not `reason()`'s existing signature).

**Implementation Decision: how "tied" is signaled.**

- **Repository Evidence:** `IReasoningEngineClient.ReasonAsync` already returns `Task<Decision[]>`, unchanged since WP-008 — an array, not a single `Decision`.
- **Specification Evidence:** §13.5's text describes the return as "a ranked list of Decisions," with no accompanying flag or wrapper type named anywhere in §13.3 or §13.5.
- **Alternative A:** An explicit flag or wrapper type (e.g., a new `Decision.IsTiedCandidate` field, or a `RankedDecisionSet` wrapper).
- **Alternative B:** Array length as the implicit signal (length 1 = single decision; length > 1 = ranked, tied set).
- **Selected Alternative:** B.
- **Why Alternative A was rejected:** No field or wrapper of this kind is named anywhere in the cited sections; adding one is an uncalled-for contract change beyond what §13.5 asks for.
- **Why Alternative B was rejected:** N/A — selected.
- **KISS justification:** B requires zero contract change — the array already returned since WP-008 is sufficient.
- **YAGNI justification:** No new type is introduced in anticipation of a need the Specification does not name.

---

## Context Expansion Ambiguity — Resolved

**Ambiguity:** §12.4: "the Reasoning Engine may issue exactly one follow-up `assemble_context()` call with an expanded scope/budget — bounded to prevent unbounded back-and-forth (max 1 expansion per request, configurable via `Thresholds.json`, Constitution Part 10)." Two grammatical readings exist.

**Alternative A:** The expansion limit itself ("1") is the value configurable through `Thresholds.json`.

**Alternative B:** The architectural limit of exactly one expansion is fixed/hardcoded, and some other, unnamed expansion-related parameter (e.g., an expanded-budget increment size) is what is configurable.

**Evaluation, using only the Reasoning Specification, the Roadmap, the existing repository, the Constitution, and Development-Workflow.md:**

- No field for any Context Expansion parameter exists in the current repository (`ThresholdsOptions`/`Thresholds.json`) — neither reading is repository-precedented over the other.
- No other numeric or tunable Context Expansion parameter is named anywhere in §12.4 or its surrounding subsections (§12.1–§12.6) — Alternative B requires assuming the existence of a parameter the Specification never names.
- The sentence's own construction places "configurable via `Thresholds.json`" immediately after "max 1 expansion per request," separated only by a comma — the standard English construction for a modifying clause attaching to the noun phrase it directly follows.
- Development-Workflow.md's evidence discipline ("do not invent abstractions," decisions must be "justified directly from... the Specification, the Roadmap, or the current repository") forbids resolving an ambiguity by assuming unstated content.

**Selected Interpretation: Alternative A.**

**Why:** Alternative A requires no invented content — it reads the sentence's only numeric quantity as the antecedent of its only configurability clause. Alternative B requires positing a second, unnamed parameter nowhere present in the text, which is precisely the kind of invention the evidence discipline forbids, regardless of B's superficial implementation simplicity. Alternative A is therefore both the simplest reading requiring no additional assumption and the most textually faithful one — the two criteria converge on the same answer. Implemented as a `[Range(1, int.MaxValue)]`-style configurable integer in `ThresholdsOptions`, shipped with default `1` in `Thresholds.json` (matching §12.4's own stated value while remaining genuinely adjustable, honoring "configurable" as a real requirement rather than a decorative word).

## Preserved Planning Decisions

Not reopened; unaffected by the Context Expansion ambiguity review:

- Composition Root Adapter for context acquisition (Area 1).
- Additive `ReasoningRequest` extension (Area 2).
- Existing `Task<Decision[]>` return shape, no new public method (Area 3).
- No new public API beyond the additive `ReasoningRequest` extension and the `ReasoningType` enum's growth from 1 to 13 values (§11, explicitly mandated).
- Existing `Decision` contract — unchanged, already compliant with §13.3.
- Existing `Explanation` contract — unchanged, already compliant with §14.

## Vertical Slice Definition

`AskCommand → IReasoningEngineClient.ReasonAsync → (context acquisition adapter → Memory) → (all 12 stages) → IAIProviderClient.InferAsync (as needed per reasoning type) → Decision[]` — the same real, callable, already-tested path that exists today, extended in place.

## Scope

**Included:** Intent Analysis, Constraint Evaluation, multi-hypothesis Generation, Alternative Exploration, Trade-off Analysis; all 13 reasoning types' distinct pipeline-stage emphasis (§11); Context Expansion/Reduction/Filtering/Prioritization (§12.2–§12.5); Context Collection (§12.1) and Context Validation (§12.6); the six applicable §21 failure modes plus the `LowConfidence` non-failure flagging path; `DecisionMade`, `ContextExpansionRequested`, `LowConfidenceDecisionFlagged` events.

**Explicitly Excluded** (owning WP named): `compare()`, `get_trust_signal()`, `summarize()`, `query_history()`, and their ranked-list API surface — all WP-020. Semantic/model-level reasoning-type calibration beyond mechanical stage implementation — no WP claims this.

## Projects Affected

`EOS.Reasoning` (primary), `EOS.Contracts` (additive), `EOS.Runner` (composition root wiring), `EOS.SharedKernel` + `config/Thresholds.json` (additive configuration).

## Files to Create

- `src/EOS.Reasoning/IContextAcquisitionProvider.cs` (exact name finalized during implementation) — Area 1's adapter interface.
- Stage-implementing source file(s) inside `src/EOS.Reasoning/` (exact decomposition is implementation-time).
- `tests/EOS.Reasoning.Tests/` — new per-stage/per-type test files (exact names implementation-time).

## Files to Modify

- `src/EOS.Reasoning/ReasoningEngine.cs`, `src/EOS.Contracts/ReasoningRequest.cs`, `src/EOS.Contracts/ReasoningType.cs`, `src/EOS.Runner/Program.cs`, `src/EOS.SharedKernel/Configuration/ThresholdsOptions.cs` / `config/Thresholds.json`, `tests/EOS.Runner.Tests/AskCommandIntegrationTests.cs` (mechanical fix only, if needed).

## Files That Must Not Change

Any file under `src/EOS.Knowledge/`, `src/EOS.KnowledgeGraph/`, `src/EOS.Gates/`, `src/EOS.Orchestrator/`, `src/EOS.Learning/`, `src/EOS.Planner/`, `src/EOS.Resources/`.

## Dependency Changes and Package Changes

None.

## Configuration Changes

- **Context Expansion cap** — resolved above: the cap value itself is the configurable field, default `1`.
- **Low Confidence floor** — §21: a configurable `[0.0, 1.0]` double, matching existing `ThresholdsOptions` weight-field pattern.

Both follow the existing `ThresholdsOptions` pattern exactly (`[Range]`-annotated `required` property, validated at bootstrap via `JsonConfigurationLoader.Validate`, confirmed present and unmodified).

## Test Strategy

**Unit tests:** per-stage and per-reasoning-type coverage per the roadmap's own requirement, including §21's failure modes, the `LowConfidence` flagging path, and §12.4's one-expansion cap.

**Integration tests:** `AskCommandIntegrationTests` and `EOS.Knowledge.Tests`' full suite (105 tests) must continue passing unmodified in behavior.

**Real services required:** SQL Server, Redis, ChromaDB, Ollama — no new external service.

## Acceptance Criteria

Verbatim: "A Diagnostic Reasoning request and a Rule-Based Reasoning request produce visibly different pipeline emphasis (confirmed via logged stage weighting) for the same underlying question shape."

## Definition of Done

Build clean, `dotnet format --verify-no-changes` clean, full-suite regression passing, real CodeRabbit review resolved per the Delta Review policy (`EOS Engineering Governance v2` §5), PR merged only after explicit approval, completion report archived, tag created, branch deleted.

## Risks and Future WP Boundaries

| Risk | Boundary |
|---|---|
| 13 reasoning types implemented as near-duplicate code paths, contradicting §10's "exactly one reasoning engine internally" | This WP's own responsibility, flagged for code review |
| Context Expansion adapter call must not silently allow more than one expansion | Enforced by the configured cap plus a unit test |
| WP-020 will extend the same interface/pipeline — this WP must not build structure requiring redesign, not merely extension, when WP-020 lands | Explicit non-goal: no WP-020 entry-point code written now |

---

=========================================================

## Implementation Plan Status

**Status:** FINAL

**Planning Complete:** YES

**Architecture Questions Remaining:** NO

**Implementation Ready:** YES

**Phase 4 Authorized:** YES

=========================================================

## WP-019 Completion

**Status:** COMPLETE

**Completion Date:** 2026-08-02

**Final Build Status:** `dotnet build EOS.slnx` — 0 Warnings, 0 Errors.

**Test Summary:** 272/272 passing across all projects — `EOS.Reasoning.Tests` 30/30 (including the real-Ollama integration test), `EOS.Runner.Tests` 15/15 (including the real SQL Server/Redis/ChromaDB/Ollama `AskCommandIntegrationTests`), `EOS.Knowledge.Tests` 105/105, `EOS.ArchitectureTests` 3/3, `EOS.AIProvider.Tests` 30/30, `EOS.Gates.Tests` 66/66, `EOS.Orchestrator.Tests` 5/5, `EOS.Infrastructure.Tests` 17/17, `EOS.VectorStore.Tests` 1/1. `dotnet format --verify-no-changes` clean.

**Files Changed:** see the WP-019 Slice Partition's per-slice notes; full list — `src/EOS.Reasoning/ReasoningEngine.cs`, `src/EOS.Reasoning/IContextAcquisitionProvider.cs`, `src/EOS.Reasoning/AcquiredContext.cs`, `src/EOS.Reasoning/ReasoningEngineOptions.cs`, `src/EOS.Reasoning/IDecisionMadeEventPublisher.cs`, `src/EOS.Reasoning/ILowConfidenceDecisionFlaggedEventPublisher.cs`, `src/EOS.Reasoning/IContextExpansionRequestedEventPublisher.cs`, `src/EOS.Reasoning/EOS.Reasoning.csproj`, `src/EOS.Contracts/ReasoningRequest.cs`, `src/EOS.Contracts/ReasoningType.cs`, `src/EOS.Contracts/ReasoningContextScope.cs`, `src/EOS.Runner/Program.cs`, `src/EOS.SharedKernel/Configuration/ThresholdsOptions.cs`, `config/Thresholds.json`, `tests/EOS.Reasoning.Tests/ReasoningEngineTests.cs`, `tests/EOS.Reasoning.Tests/ReasoningEngineIntegrationTests.cs`, `tests/EOS.Runner.Tests/AskCommandIntegrationTests.cs`.

**Completion Notes:** All Roadmap Included Components implemented; the single Demo/Acceptance criterion verified against source; no architecture, interface, or unexpected public contract changes; all Excluded Components (WP-020's `compare()`/`get_trust_signal()`/`summarize()`/`query_history()`, the caller-facing ranked-list API surface) remain untouched.

=========================================================
