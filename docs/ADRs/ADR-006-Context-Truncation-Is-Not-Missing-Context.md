# ADR-006 — Finding #1 — Context Truncation Is Not Missing Context

## 1. Status

**Accepted** — approved as part of the Final Corrected Architecture Gate for *Post-Roadmap WP-A — First Engineering Execution Slice* (working name; not a roadmap Work Package number).

- **adr_id:** `ADR-006`
- **approver:** Product Owner / Architecture Board, per Decision Matrix §0.6
- **conditions:** this ADR authorizes exactly one specification correction (§6 below, applied to `docs/Reasoning-Engine-Specification-v1.0.md` §21) and the matching implementation change in `src/EOS.Reasoning/ReasoningEngine.cs` (`ValidateContext`, plus the truncation assumption in `GenerateExplanation`/`PrepareContext`). It changes no other contract, budget value, or subsystem.

This record is maintained as ADR text only; no Knowledge Graph event-store artifact is created (see ADR-005 §1 for the same rationale).

## 2. Context

Finding #1 was identified during the post-roadmap architecture-completion analysis preceding WP-A. It concerns the reachability of every context-requesting reasoning request — and therefore of the Autonomous Engineering Loop's `UserRequest`/`ManualRequest` path (WP-028) — on any Knowledge Graph that has grown past a few kilobytes of relevant content.

## 3. Finding

`LoopController.RunFromUnderstandAsync` always supplies a `ReasoningContextScope`, so `ReasoningEngine` always assembles context. `KnowledgeClient.AssembleContextAsync` faithfully implements Memory-Management-Specification-v1.0 §15.1/§15.2: `Truncated = assembled.Count < ranked.Count`. `ReasoningEngine.ValidateContext` (WP-019), faithfully implementing Reasoning-Engine-Specification-v1.0 §21's parenthetical, threw `MissingContext` whenever the payload was empty **or** truncated after the one permitted expansion.

## 4. Proven Technical Facts

1. Reasoning §21 "Missing Context" row (original text): *"Memory returns an empty/insufficient `ContextPayload` (Memory-Management-Specification-v1.0 §15.2, `truncated=true` or empty)."*
2. Memory §15.2: *"`ContextPayload.truncated` is always populated truthfully — Memory never silently drops content without signaling that truncation occurred, so a consumer … can distinguish 'there was nothing more relevant' from 'there was more, but it didn't fit the budget.'"*
3. Memory §15.1 pseudocode: `truncated=(len(assembled) < len(ranked))`.
4. Reasoning §12.4 bounds Context Expansion to one attempt; `Thresholds.json` sets `reasoningContextExpansionCap: 1` and `ReasoningEngine.DefaultContextBudget = 2048`.
5. The shared development database held 2,890 Lesson, 8,414 Fact and 707 Pattern nodes at the time of analysis (2026-09-10); with a 2,048-character budget doubled once to 4,096, truncation is guaranteed.
6. Memory §18 designates Episodic memory as non-expiring, so the relevant candidate pool is structurally non-decreasing over a deployment's lifetime.
7. `tests/EOS.Orchestrator.Tests/LoopControllerTests.cs` exercises the Loop's `UserRequest` path only through test doubles; the path had never been executed against real infrastructure.

## 5. Why the Two Contracts Contradict

Under Memory's own definition, `truncated=true` means *"Memory delivered a full budget's worth of ranked content and had more left over"* — a property of every healthy, growing graph. Under Reasoning §21's parenthetical, that same signal meant *insufficient* and therefore failure. The two readings are reconcilable only while the graph is nearly empty. The rule that equates truncation with failure makes context-requesting reasoning unreachable in exactly the state the system is designed to reach. Memory's meaning is the one Memory itself, the ranking design (§19), and the "hard budget cutoff, FR-M5" comment all assume; the Reasoning-side parenthetical is the outlier.

## 6. Decision

Reasoning-Engine-Specification-v1.0 §21 "Missing Context" row, first sentence, is corrected from

> Memory returns an empty/insufficient `ContextPayload` (Memory-Management-Specification-v1.0 §15.2, `truncated=true` or empty).

to

> Memory returns an empty `ContextPayload` — zero items — after the one permitted Context Expansion (§12.4). `truncated=true` (Memory-Management-Specification-v1.0 §15.2) signals that the assembled context is incomplete relative to Memory's ranked candidates; it does not by itself constitute Missing Context. A non-empty truncated payload is validated (§12.6) and used, and its truncation is surfaced through Explainability (§14) so the incompleteness remains observable.

The remainder of the row (one expansion; if still empty → `ReasoningFailed(MissingContext)`; never fabricate evidence) is unchanged.

The exact architectural meaning is therefore:

- An empty context after the permitted expansion is `MissingContext`.
- A non-empty context remains usable context even when truncated.
- `Truncated=true` means the assembled context is incomplete relative to the ranked candidates — nothing more.
- Truncation does **not** by itself mean `MissingContext`, and Reasoning validates/uses a non-empty truncated context.
- The truncation metadata remains observable: Memory §15.2 is unchanged, and the Reasoning Engine records the truncation as an explicit `Explanation.Assumptions` entry.

This is a correction of the contract's meaning, not a workaround for any particular demonstration.

## 7. Alternatives Considered and Rejected

- **Raise the context budget** — does not resolve the contradiction; any budget is exceeded by a growing graph.
- **Have the Loop stop requesting context** — contradicts Autonomous-Engineering-Loop-Specification-v1.0 §7.1 step 3 as realized by WP-028.
- **Drive the human-request path outside `LoopController`** — duplicates specification-owned sequencing in the composition root.
- **Remove or ignore the truncation signal** — violates Memory §15.2's transparency requirement.

## 8. Consequences

1. `ReasoningEngine.ValidateContext` rejects only a `null`/empty acquired context; `PrepareContext` adds the assumption *"Context was truncated to the budget after Context Expansion; further relevant items existed (Memory-Management-Specification-v1.0 §15.2)."* whenever the final payload was truncated.
2. `tests/EOS.Reasoning.Tests/ReasoningEngineTests.cs`: the former `ReasonAsync_ThrowsMissingContext_WhenStillTruncatedAfterContextExpansion` is inverted to `ReasonAsync_Succeeds_WhenContextIsNonEmptyAndStillTruncatedAfterContextExpansion`; `ReasonAsync_Succeeds_WhenContextIsNonEmptyAndTruncated_WithNoExpansionPermitted` and `ReasonAsync_ThrowsMissingContext_WhenStillEmptyAfterContextExpansion` are added; `ReasonAsync_ThrowsMissingContext_WhenAcquiredContextIsEmpty` is unchanged.
3. **Deferred finding — cold start:** an empty Knowledge Graph still yields `MissingContext`, so a fresh installation's Loop cannot start until the graph holds at least one Lesson/Fact/Pattern node. This ADR does not resolve that; it is recorded here for a future governance decision.
4. **Recorded resolution of Governance-Change-Proposal-001 Item 1:** the Artifact Registry (Constitution Part 8) is implemented by WP-A as `EOS.Infrastructure.ArtifactStore` (Constitution Part 4: SQL Server owns indexed artifact metadata) behind `EOS.Contracts.IArtifactRegistryClient` — the project where the roadmap's Traceability Matrix already attributes it. The Matrix's attribution becomes true going forward; the roadmap text itself is not modified.

## 9. Non-Goals

No change to Memory-Management-Specification-v1.0; no change to any budget or threshold value; no change to Context Expansion's single-attempt bound; no change to the Autonomous Engineering Loop; no resolution of the cold-start finding.
