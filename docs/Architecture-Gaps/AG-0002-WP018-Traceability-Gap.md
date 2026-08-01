# AG-0002 — WP-018 Traceability Gap: Two Specification Capabilities Unassigned in the Roadmap

## Summary

During WP-018 Phase 3 architecture review, two capabilities described in `Knowledge-Management-Specification-v1.0.md` were found to have no assigned owning Work Package in `EOS-Implementation-Roadmap-v1.0.md` or its Architecture Traceability Matrix. Both capabilities are documented here as facts only. Neither is included in WP-018's implementation scope as a result of this document.

## Evidence

### Finding 1 — Reusability computation mechanism

`Knowledge-Management-Specification-v1.0.md` §13 (Knowledge Quality table) states, verbatim:

> "Reusability | Derived from Knowledge Reuse's own discovery/recommendation frequency (§18.2) | Computed by Knowledge Management from its own Reuse Engine's activity log"

This is the only occurrence of "activity log" in the document. No section defines this activity log as a persistent artifact, names its storage location, states an FR-KM1 exemption, or defines an API to record or read it. `Knowledge-Management-Specification-v1.0.md` §15.7's fully specified `search()` ranking algorithm does not reference Reusability and contains no usage-recording step. §18.1 ("Discovery... realized entirely through Search Strategy (§15), not a separate mechanism") and §18.2 ("Recommendation... implemented as a standing query pattern... rather than a new inference mechanism") do not state a recording obligation. §20.1's `IKnowledgeManagementClient` interface (`classify`, `navigate_relationships`, `get_quality`, `search`, `request_governance_action`, `find_duplicates`) contains no recording method.

### Finding 2 — Inbound `DecisionMade` event consumption

`Knowledge-Management-Specification-v1.0.md` §19.1 ("Consumed Events") states, verbatim:

> "`DecisionMade` (Reasoning-Engine-Specification-v1.0 §17) — where a Decision's evidence references a knowledge object, its `Engineering Impact`/`Reliability` Quality attributes (§13) are updated."

No interface signature, adapter shape, or subscriber mechanism is given for this consumption anywhere in the document.

## Specification References

| Document | Section | Exact Quote |
|---|---|---|
| Knowledge-Management-Specification-v1.0.md | §13 | "Reusability \| Derived from Knowledge Reuse's own discovery/recommendation frequency (§18.2) \| Computed by Knowledge Management from its own Reuse Engine's activity log" |
| Knowledge-Management-Specification-v1.0.md | §15.7 | "km_score(item) = q1 * item.quality.confidence + q2 * item.quality.reliability + q3 * relationship_relevance(item, request.relationship_context) - q4 * deprecation_penalty(item.lifecycle_state)" (no Reusability term, no recording step) |
| Knowledge-Management-Specification-v1.0.md | §18.1 | "The general capability of finding existing knowledge relevant to a new need — realized entirely through Search Strategy (§15), not a separate mechanism." |
| Knowledge-Management-Specification-v1.0.md | §18.2 | "implemented as a standing query pattern... rather than a new inference mechanism; it is Search Strategy (§15) invoked proactively rather than reactively." |
| Knowledge-Management-Specification-v1.0.md | §19.1 | "`DecisionMade` (Reasoning-Engine-Specification-v1.0 §17) — where a Decision's evidence references a knowledge object, its `Engineering Impact`/`Reliability` Quality attributes (§13) are updated." |
| Knowledge-Management-Specification-v1.0.md | §20.1 | `IKnowledgeManagementClient` — `classify`, `navigate_relationships`, `get_quality`, `search`, `request_governance_action`, `find_duplicates` (no activity-log recording method, no `DecisionMade` subscriber method) |

## Roadmap References

| Document | Section | Exact Quote |
|---|---|---|
| EOS-Implementation-Roadmap-v1.0.md | WP-018 row, "Included components" | "The `QualityProfile` aggregate; `request_governance_action()` routed through `IProtectionClient.validate()`; Freshness scoring and drift detection; structural (non-semantic) Duplicate Detection; the additive quality/relationship-aware ranking pass (`search()`)." |
| EOS-Implementation-Roadmap-v1.0.md | WP-018 row, "Explicitly excluded" | "Semantic similarity delegation to Reasoning's `compare()` (WP-020, this WP stubs that call structurally until WP-020 exists)." |
| EOS-Implementation-Roadmap-v1.0.md | WP-018 row, "Test verification" | "Unit tests for the ranking formula's independent weighting; an integration test confirming a governance action is denied when Protection returns Deny." |
| EOS-Implementation-Roadmap-v1.0.md | WP-018 row, "Demo / acceptance criteria" | "`search()` returns Memory's results re-ranked by quality/relationship signals, with a stable sort confirmed on tied inputs." |
| EOS-Implementation-Roadmap-v1.0.md | line 976 | "All Reasoning events (`DecisionMade`...`ContextExpansionRequested`) \| Events \| WP-008 (`DecisionMade`), WP-019 (`ContextExpansionRequested`, `LowConfidenceDecisionFlagged`), WP-020 (`ReasoningFailed` handling depth)" — assigns `DecisionMade`'s production to WP-008; does not assign its consumption by Knowledge Management to any WP. |

Neither the Reusability activity-log mechanism nor `DecisionMade` consumption appears in any field of WP-018's roadmap row, and neither is named as excluded/deferred (contrast with the explicit deferral language used for `compare()` in the same row).

## Traceability Matrix References

| Document | Section | Exact Quote |
|---|---|---|
| EOS-Implementation-Roadmap-v1.0.md (Traceability Matrix) | line 1010 | "`IKnowledgeManagementClient` (full) \| Interface \| WP-017 (`classify`/`navigate_relationships`), WP-018 (`get_quality`/`search`/`request_governance_action`/`find_duplicates`)" |
| EOS-Implementation-Roadmap-v1.0.md (Traceability Matrix) | line 1016 | "All Knowledge Management events \| Events \| WP-017 (`KnowledgeClassified`, `KnowledgeRelationshipAdded`), WP-018 (remainder)" — this row covers Knowledge Management's own 9 *outbound* events (§19's table); it does not cover inbound `Consumed Events` (§19.1), and `DecisionMade` is not a Knowledge Management event. |

No row in the Traceability Matrix assigns implementation of a Reusability activity-log mechanism, or of `DecisionMade` consumption, to any Work Package.

## Analysis

`docs/Development-Workflow.md` establishes which document governs implementation scope:

> "`docs/EOS-Implementation-Roadmap-v1.0.md` remains the single source of truth for Work Package scope and sequencing." (line 7)

> "**Frozen Roadmap.** `docs/EOS-Implementation-Roadmap-v1.0.md` defines WP scope and sequencing. A WP's plan may narrow ambiguity within its own row; it does not expand scope beyond it." (line 31)

> "**No Scope Creep.** Anything discovered during implementation that belongs to a different WP is deferred and explicitly recorded, not absorbed into the current one." (line 36)

Both findings describe capabilities the Specification names but that the Roadmap and Traceability Matrix do not assign to WP-018, to any other specific Work Package, or to an explicit future deferral (unlike `compare()`, which carries explicit deferral text in the same WP-018 roadmap row). This is a discrepancy between the Specification's descriptive text and the Roadmap's binding scope assignment.

## Impact

WP-018's official acceptance criteria (Test Verification and Demo/Acceptance Criteria, quoted above under Roadmap References) do not reference Reusability computation or `DecisionMade` consumption. Neither finding affects WP-018's ability to satisfy its own roadmap-defined acceptance criteria. No production code exists yet for either capability in this repository.

## Recommendation

A governance review should determine, for each finding, one of: (a) explicit assignment to a specific future Work Package, (b) explicit deferral language added to the relevant roadmap row, or (c) clarification within the Specification itself if the capability was not intended to require standalone implementation. This document does not select among these options.

## Status

Open — Governance Review Required
