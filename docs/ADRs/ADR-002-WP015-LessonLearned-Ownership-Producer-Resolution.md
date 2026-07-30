# ADR-002 — WP-015 `LessonLearned` Ownership & Producer Resolution

> **Superseded by `ADR-015-002-LessonLearned-Producer-Ownership.md`** (Status: Accepted, Option A ratified). This document is preserved unmodified as the historical Proposed-stage analysis.

## Status

Proposed

## Context

`consolidate()` (Memory-Management-Specification-v1.0 §16.2, §20.1) must emit exactly one `LessonLearned` event. Constitution Part 3's event table names `LessonLearned`'s producers as "Any role, EOS.Gates (on novel failure)" — it does not literally name `EOS.Knowledge`/Memory. Memory-Management-Specification §21 names "Memory (`consolidate()`, §16)" as producer. A prior validation pass surfaced additional, previously-uncited Constitution evidence bearing directly on this question (below).

## Problem Statement

Who is the canonical producer of `LessonLearned` for each of the four consolidation triggers (§16.1), and how does this reconcile with the Constitution's own event table?

## Evidence

`EOS-Specification.md` Part 3 (Event Catalog):
> "| LessonLearned | Any role, EOS.Gates (on novel failure) | Knowledge, Meta Learning pipeline (Part 14) | context, observation, source_task_id | Knowledge Graph (canonical) + event store | Replayable | v1 |"

`EOS-Specification.md` §0.8.3:
> "A failed gate emits a blocking status on the task (Task Lifecycle, Part 6) and a `LessonLearned` event if the failure is novel (first occurrence) — feeding directly into Meta Learning (Part 14)."

`EOS-Specification.md` Part 3 (Event Catalog), `IncidentResolved` row:
> "| IncidentResolved | EOS.DevOps | Knowledge (LessonLearned trigger), Dashboard | incident_id, resolution, root_cause | Event store + Knowledge Graph | Replayable | v1 |"

`EOS-Specification.md` Part 14.1 (Compounding Pipeline table):
> "| Lesson | `LessonLearned` event (Part 3) from any task/gate/incident |"

Memory-Management-Specification-v1.0 §16.1 (Gate-failure trigger row):
> "Automatic, on Gate failure (novel failure) | Mirrors Constitution §0.8.3's existing rule that a novel gate failure emits `LessonLearned` — Memory's consolidation is what *produces* the Episodic Memory entry that event references"

Memory-Management-Specification-v1.0 §16.1 (explicit-role trigger row):
> "Explicit role action ("this is worth remembering") | Any role, via `IKnowledgeClient.consolidate()` (§20)"

Memory-Management-Specification-v1.0 §16.2:
> "emit LessonLearned(episodic_entry.id, source=source_memory.origin)   # Constitution Part 3, existing event"

## Additional Evidence (found during Phase 2 cross-validation)

Repository check (factual verification, not a specification citation): `src/EOS.Contracts/EventEnvelope.cs` and `src/EOS.Infrastructure/SqlEventStore.cs` exist, but a repository-wide search found **zero production call sites for either, anywhere, across all prior Work Packages** — no event of any type (not `LessonLearned`, not `TaskCreated`, not `KnowledgeUpdated`) has ever actually been emitted in production code in this codebase's history. This reframes the problem: it is not a Memory-subsystem-local gap, but a symptom of a system-wide, never-yet-bootstrapped event-emission capability.

## Considered Options

**Option A — Trigger-dependent reconciliation.** Constitution's Part 3 table is treated as non-exhaustive. For the Gate-failure trigger, `LessonLearned` is already emitted by `EOS.Gates` per the pre-existing, unchanged §0.8.3 mechanism — `consolidate()` in this path only creates the referenced entry and does *not* re-emit. For the other three triggers (explicit role, `IncidentResolved`, session close), no prior emission exists, so `consolidate()`'s own emission (§16.2) is the actual production of that event, with `EOS.Knowledge` as producer.

**Option B — "Any role" already covers all non-Gate-failure cases,** on the theory that the calling role (or, for `IncidentResolved`, `EOS.DevOps`) is the producer of record and `EOS.Knowledge` is merely the mechanism.

**Option C — Formal Constitution amendment** naming `EOS.Knowledge` (via `consolidate()`) as an explicit additional producer alongside "Any role, EOS.Gates."

**Option D — Defer real event emission entirely** (a no-op or stubbed `emit`), consistent with WP-007's already-accepted precedent of leaving `update()`'s `KnowledgeUpdated` unemitted, until a dedicated future WP bootstraps real event infrastructure system-wide.

## Pros / Cons

| Option | Pros | Cons |
|---|---|---|
| A | Reconciles all cited passages without declaring any of them wrong; the `IncidentResolved` row's own "(LessonLearned trigger)" annotation and Part 14.1's "from any task/gate/incident" both directly support it; accounts for all four triggers including the automatic ones. | Requires `consolidate()`'s algorithm to branch by trigger type — a real behavioral distinction beyond a documentation nuance; is a synthesis across three citations, not one direct statement. |
| B | No Constitution change needed. | Does not account for `IncidentResolved` (no role is "calling" in that automatic scenario); leaves that case genuinely unresolved. |
| C | Most explicit; permanently closes the gap. | Requires a Constitutional amendment (§0.6: CTO + Principal Engineer consensus, human sign-off). |
| D | Consistent with unbroken existing precedent (zero events emitted anywhere in 14 prior WPs); avoids building bespoke, WP-015-local event infrastructure for what is actually a system-wide gap; lowest implementation risk. | Directly violates §20.1's explicit postcondition ("emits exactly one `LessonLearned` event") — unlike `update()`'s more casually-worded event mention, `consolidate()`'s event emission is one of only two formally stated postconditions for that method; Learning Engine's `ClusterTrigger` (§11.1) is specified to consume real `LessonLearned` events, so deferral would silently break that already-approved downstream consumer's assumed input. |

## Consequences

Option A requires `consolidate()`'s implementation to know which trigger invoked it and suppress re-emission specifically for the Gate-failure path — a real design detail that must be carried into the eventual Implementation Plan, not merely a documentation choice. Option D would be materially cheaper but leaves `consolidate()` failing its own specified postcondition and breaks Learning Engine's already-approved consumption assumption — this is a heavier consequence than WP-007's `update()`/`KnowledgeUpdated` deferral, which had no known downstream consumer depending on it at the time.

## Recommendation

**Option A remains the recommendation**, not Option D — despite Option D's newly-discovered precedent-consistency, `consolidate()`'s postcondition is explicit and load-bearing (Learning Engine's Meta Learning pipeline cannot begin without a real `LessonLearned`), unlike `update()`'s deferred event. Confidence in Option A is upgraded on this pass: the `IncidentResolved` row's explicit "(LessonLearned trigger)" annotation and Part 14.1's "from any task/gate/incident" phrase are both direct Constitution-level citations (not merely Memory-Management-Specification's own claim), and both independently corroborate `EOS.Knowledge` as the incident-triggered producer. However, the newly-found system-wide scope of the underlying gap (Additional Evidence, above) means the Board should weigh whether resolving real event emission belongs inside WP-015 at all, or should be escalated to a dedicated cross-cutting infrastructure decision — see Open Questions.

## Open Questions

- The `LessonLearned` row's own producer-column text ("Any role, EOS.Gates") still does not literally name `EOS.Knowledge` or "incident" even after this stronger evidence — should that cell be corrected for completeness, independent of this ADR?
- Does "session close with flagged content" (§16.1's fourth trigger) have any Constitution-level corroboration analogous to the `IncidentResolved` row's annotation? None was found in this or the prior review pass — this trigger's producer attribution rests on Memory-Management-Specification alone.
- **Given no event of any type has ever been emitted in production across 14 prior WPs, is bootstrapping real event emission an appropriately-scoped decision for WP-015 alone, or does it warrant a dedicated cross-cutting infrastructure Work Package?** This ADR does not answer that question — it is surfaced for the Board's explicit consideration.

## Decision Required

Cross-cutting architecture decision per Constitution §0.6. Requires ratification by the Architecture Board before `consolidate()`'s trigger-branching behavior is implemented.
