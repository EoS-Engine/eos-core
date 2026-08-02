# AG-0003 — WP-020 `query_history()` Data-Access Gap: No Legal Path to Decision History

## Summary

During WP-020 architecture review, `IReasoningEngineClient.query_history()` was found to have no legal data-access mechanism in any frozen document. `query_history()` is documented here as a fact only and is deferred, unimplemented, as a result of this document. `compare()`, `get_trust_signal()`, and `summarize()` are unaffected and proceed under WP-020 as planned.

## Evidence

### Finding — `query_history()` has no consumed interface or dependency granting access to its required data source

`Reasoning-Engine-Specification-v1.0.md` §13.7 states, verbatim:

> "A read-only, queryable projection of past Decisions (`IReasoningEngineClient.query_history()`, §16.3) — explicitly a *projection*, not a canonical store: the canonical record of a Decision's evidence remains the Artifact Registry (Part 8); Decision History is a convenience index over `decision_id → evidence_refs/confidence/explanation`, rebuildable at any time from the Artifact Registry and Event Catalog..."

§16.3 ("Consumed Interfaces"), the specification's own exhaustive list of interfaces `EOS.Reasoning` may call, states, verbatim:

> "- `IKnowledgeClient.assemble_context()` — Memory-Management-Specification-v1.0 §20.1, consumed per §12.1 above, unmodified.
> - `IKnowledgeClient.query_similar()` / `.update()` — Constitution §0.5.2 / Memory-Management-Specification-v1.0 §20.1, consumed only where a specific reasoning type (§11) requires direct graph traversal rather than assembled context (rare — most reasoning consumes an already-assembled `ContextPayload`)."

Neither entry grants access to the Artifact Registry or the Event Catalog. `EOS-System-Architecture-Specification-v1.0.md`'s independent "Interfaces (consumed)" line for `EOS.Reasoning` names the same two interfaces and no others. Constitution Part 1 §1.2's dependency table lists `EOS.Reasoning`'s dependencies as exactly "`EOS.Contracts, EOS.SDK`" — no `EOS.Knowledge` reference, unlike `EOS.Planner`'s and `EOS.Learning`'s rows, which include it for equivalent cross-module read access. None of Reasoning-Engine-Specification-v1.0's own ADRs (ADR-R001, ADR-R002, ADR-R003) address this mechanism.

## Analysis

This mirrors AG-0002's precedent: a capability the Specification names and describes, with no interface, adapter shape, or dependency grant defined anywhere to realize it. `docs/Development-Workflow.md` establishes:

> "`docs/EOS-Implementation-Roadmap-v1.0.md` remains the single source of truth for Work Package scope and sequencing." (line 7)

The WP-020 roadmap row's own "Test verification" and "Demo / acceptance criteria" fields (the fields `Development-Workflow.md` line 126 designates as "Acceptance Criteria — copied verbatim from the roadmap") name only `compare()`, `get_trust_signal()`, and `summarize()`:

> "Test verification | Contract tests for `compare()`/`get_trust_signal()` against their exact published pre/postconditions; regression run of WP-016's Compression sweep now using the real `summarize()`"
> "Demo / acceptance criteria | Two similar test Lessons produce a high `compare()` similarity score; WP-016's Compression demo now produces a real, model-generated summary instead of a stub"

`query_history()` is named in the roadmap row's "Included components" and "Git commit scope" fields, but not in either Acceptance Criteria field.

## Impact

WP-020's roadmap-defined Acceptance Criteria (Test Verification and Demo/Acceptance Criteria) do not reference `query_history()` and are fully satisfiable by `compare()`, `get_trust_signal()`, and `summarize()` alone. `query_history()` remains an unimplemented member of `IReasoningEngineClient`; any caller invoking it receives a documented "not yet implemented" failure rather than a fabricated result. No production code exists for this capability.

## Recommendation

A governance/Architecture Board review should determine, when `query_history()` is eventually to be implemented, one of: (a) an additive dependency grant in Constitution Part 1 §1.2 (mirroring the `EOS.CTO`→`EOS.Knowledge` precedent), (b) an additive consumed-interface declaration in Reasoning-Engine-Specification-v1.0 §16.3, or (c) another mechanism this document does not select among. This document does not propose or select a solution.

## Status

Open — Governance Review Required
