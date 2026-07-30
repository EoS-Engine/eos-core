# ADR-015-002 — `LessonLearned` Producer Ownership

## Status

**Accepted**

## Background

`consolidate()` (§16.2, §20.1) must emit exactly one `LessonLearned` event. Constitution Part 3's event table names `LessonLearned`'s producers as "Any role, EOS.Gates (on novel failure)" — it does not literally name `EOS.Knowledge`. Memory-Management-Specification §21 names "Memory (`consolidate()`, §16)" as producer. WP-015's Architecture Challenge classified this Missing Specification; subsequent cross-validation found additional, directly relevant Constitution-level evidence (below) that was not available at the time of the original classification.

## Problem Statement

Who is the canonical producer of `LessonLearned` for each of `consolidate()`'s four triggers (§16.1), and does `EOS.Knowledge` legitimately produce it for any of them?

## Alternatives Considered

1. **Trigger-dependent reconciliation** — treat Constitution's Part 3 table as non-exhaustive; for the Gate-failure trigger, `EOS.Gates` remains the producer (existing, unchanged §0.8.3 mechanism), and `consolidate()` does not re-emit; for the other three triggers (explicit role, `IncidentResolved`, session close), `consolidate()`'s own emission is the actual production of the event.
2. **"Any role" already covers all non-Gate-failure cases** without needing `EOS.Knowledge` named as a producer at all.
3. **Formal Constitution amendment** naming `EOS.Knowledge` as an explicit additional producer.
4. **Defer real event emission entirely** (no-op), consistent with WP-007's already-accepted precedent for `update()`'s `KnowledgeUpdated`, until a dedicated future WP bootstraps real event infrastructure system-wide (a repository check confirms no event of any type has ever been emitted in production code across all prior Work Packages).

## Decision

Adopt Alternative 1 — trigger-dependent reconciliation, with `EOS.Knowledge` as the producer for the explicit-role, `IncidentResolved`, and session-close triggers, and `EOS.Gates` remaining the unchanged producer for the Gate-failure trigger.

## Rationale

Alternative 2 does not account for the `IncidentResolved` trigger, since no role is "calling" in that automatic scenario. Alternative 3 requires an out-of-band Constitutional amendment disproportionate to what the evidence already supports. Alternative 4, while consistent with existing precedent (no event has ever been emitted in this codebase), directly conflicts with `consolidate()`'s own explicit, formally-stated postcondition ("emits exactly one `LessonLearned` event") and with the roadmap's own WP-015 "Expected deliverables" wording ("a working `consolidate()` producing... a real `LessonLearned` event") — unlike `update()`'s more casually-worded event mention, this is one of only two formal postconditions §20.1 states for `consolidate()`, and Learning Engine's `ClusterTrigger` (§11.1) is specified to consume a real event, not a stub. Alternative 1 is directly supported by convergent evidence: the `IncidentResolved` row's own "(LessonLearned trigger)" annotation and Part 14.1's "from any task/gate/incident" phrasing, both Constitution-level citations independent of Memory-Management-Specification's own claim.

## Consequences

- `consolidate()`'s implementation must branch on trigger type: suppress re-emission for the Gate-failure path (where `EOS.Gates` already emitted the event per §0.8.3), and perform real emission for the other three.
- The `LessonLearned` row's own producer-column text in Constitution Part 3 remains, strictly, incomplete (it does not literally list "Knowledge" or "incident") even after this ADR — accepted as a documentation gap, not a blocking one, given the converging corroborating citations found.
- This ADR does not resolve *how* `EOS.Knowledge` mechanically emits the event — that is ADR-015-001's embedding-adapter-adjacent concern only insofar as both rely on the Composition Root pattern; the actual event-transport mechanism is ADR-015-003's concern.

## Specification References

- Memory-Management-Specification-v1.0 §16.1: "Automatic, on Gate failure (novel failure) | Mirrors Constitution §0.8.3's existing rule that a novel gate failure emits `LessonLearned` — Memory's consolidation is what *produces* the Episodic Memory entry that event references"
- Memory-Management-Specification-v1.0 §16.1: "Explicit role action... | Any role, via `IKnowledgeClient.consolidate()` (§20)"
- Memory-Management-Specification-v1.0 §16.2: "emit LessonLearned(episodic_entry.id, source=source_memory.origin)"
- Memory-Management-Specification-v1.0 §20.1: "Postcondition: emits exactly one `LessonLearned` event; never emits a pipeline-stage event"

## Constitution References

- Part 3 (Event Catalog): "LessonLearned | Any role, EOS.Gates (on novel failure) | Knowledge, Meta Learning pipeline (Part 14) | ..."
- Part 3 (Event Catalog): "IncidentResolved | EOS.DevOps | Knowledge (LessonLearned trigger), Dashboard | ..."
- §0.8.3: "A failed gate emits a blocking status on the task... and a `LessonLearned` event if the failure is novel (first occurrence)"
- Part 14.1: "Lesson | `LessonLearned` event (Part 3) from any task/gate/incident"

## Impact Analysis

No Constitution edit performed. No roadmap edit performed. `consolidate()`'s design must account for trigger-dependent branching at implementation time (not performed by this ADR). Learning Engine's `ClusterTrigger` receives a real, non-stubbed event for the three non-Gate-failure triggers, satisfying its own already-approved consumption assumption.

## Future Work

The Board should consider, independently of WP-015, whether to correct Constitution Part 3's `LessonLearned` producer-column text for completeness, given the convergent evidence found here.
