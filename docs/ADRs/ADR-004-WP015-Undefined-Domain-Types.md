# ADR-004 — WP-015 Undefined Domain Types (`ContextRequest`, `ContextPayload`, `MemoryRef`, `EpisodicEntryRef`)

> **Superseded in part by `ADR-015-004-MemoryRef-Identity.md`** (Status: Accepted, covers `MemoryRef` only) **and by the governance note `WP-015-Specification-Clarifications.md`** (covers `EpisodicEntryRef`, resolved via direct precedent rather than a full ADR). `ContextRequest`/`ContextPayload`'s evidenced fields, as catalogued below, remain accepted as-is. This document is preserved unmodified as the historical Proposed-stage analysis.

## Status

Proposed

## Context

`IKnowledgeClient.assemble_context(ContextRequest)`/`consolidate(MemoryRef, ...)` (§20.1) reference four types never given a formal, field-by-field schema anywhere in Memory-Management-Specification-v1.0 or any other reviewed document. This mirrors WP-007's own prior need to resolve `KnowledgeGraphRef`'s concrete type from cross-document evidence rather than an explicit definition.

## Problem Statement

What are the field-level schemas of `ContextRequest`, `ContextPayload`, `MemoryRef`, and `EpisodicEntryRef`, to the extent evidence exists — and what remains genuinely unresolved?

## Evidence

Memory-Management-Specification-v1.0 §15.1:
> "on assemble_context(request): budget = request.token_or_size_budget ... if request.includes_working: ... if request.includes_short_term: ... if request.includes_episodic: candidates += KnowledgeGraph.query(type=Lesson, filters=request.filters) ... if request.includes_semantic: candidates += KnowledgeGraph.query(type in [Fact, Pattern, BestPractice, Principle], filters=request.filters) ... if request.project_scope: candidates = filter(candidates, domain_tags contains request.project_scope) ... return ContextPayload(items=assembled, truncated=(len(assembled) < len(ranked)))"

Memory-Management-Specification-v1.0 §15.1 (Redis read): "Redis.read(task_id=request.task_id)"

Memory-Management-Specification-v1.0 §20.1:
> "ContextPayload assemble_context(ContextRequest request) // NEW, §15 — Precondition: request.token_or_size_budget > 0 — Postcondition: sum(item.size for item in result.items) <= request.token_or_size_budget"

Memory-Management-Specification-v1.0 §16.2:
> "on consolidate(source_memory, reason, evidence_refs): episodic_entry = KnowledgeGraph.create_node(type=Lesson, content=source_memory.content, evidence_refs=evidence_refs) ... emit LessonLearned(episodic_entry.id, source=source_memory.origin) ... source_memory.mark_consolidated()"

Memory-Management-Specification-v1.0 §20.1:
> "EpisodicEntryRef consolidate(MemoryRef source, string reason, string[] evidence_refs) // NEW, §16 — Precondition: source.status != already_consolidated"

## Considered Options

There is no meaningful "options" choice for *evidenced* fields — they are catalogued below as found. The only real option concerns the two genuinely undefined types (`MemoryRef`, `EpisodicEntryRef`'s identity):

**Option A — Propose only evidenced fields; leave identity fields as an explicitly disclosed open gap** (no invention).

**Option B — Infer `MemoryRef`/`EpisodicEntryRef` as `Guid`, by analogy to `KnowledgeGraphRef`'s WP-007 resolution.**

**Option C — Treat this as blocking and require a specification clarification before any type is defined in code.**

## Pros / Cons

| Option | Pros | Cons |
|---|---|---|
| A | Honest, doesn't invent; matches the "do not infer field definitions" instruction under which this review was conducted. | Leaves a real, load-bearing gap unresolved — no `MemoryRef`/`EpisodicEntryRef` type can be implemented in code from this alone. |
| B | Reuses a working precedent (WP-007), directionally plausible given every other reference-type in this codebase (`KnowledgeGraphRef`) resolved to `Guid`. | Not evidenced — `KnowledgeGraphRef` always resolves to an *existing* graph node; `MemoryRef`'s `source_memory` may originate from Working/Short-term/Session content that is not yet a graph node at all (§16.1's own trigger list includes ephemeral, non-`KnowledgeGraph`-backed sources), so the analogy is not a clean match. |
| C | Removes ambiguity entirely. | Introduces an external dependency (waiting on a specification update) not otherwise required by this ADR process. |

## Consequences

Proceeding with Option A means the eventual Implementation Plan cannot fully specify `MemoryRef`/`EpisodicEntryRef` until this ADR's open question is separately resolved — this is a genuine implementation blocker, not merely a documentation nicety.

## Recommendation

**Option A** for `ContextRequest`/`ContextPayload` (evidenced fields below, proposed with confidence); **Option A** (not B) for `MemoryRef`/`EpisodicEntryRef` — the `KnowledgeGraphRef` analogy was actively tested and does not cleanly hold, given `MemoryRef` must represent ephemeral, potentially non-graph-backed sources that `KnowledgeGraphRef` was never used for.

### Proposed Fields

| Type | Field | Basis |
|---|---|---|
| `ContextRequest` | `TokenOrSizeBudget` | Explicit — §15.1, §20.1 precondition |
| `ContextRequest` | `IncludesWorking` | Derived — §15.1 |
| `ContextRequest` | `IncludesShortTerm` | Derived — §15.1 |
| `ContextRequest` | `IncludesEpisodic` | Derived — §15.1 |
| `ContextRequest` | `IncludesSemantic` | Derived — §15.1 |
| `ContextRequest` | `ProjectScope` | Derived — §15.1 |
| `ContextRequest` | `Filters` | Derived — §15.1 (internal shape not specified) |
| `ContextRequest` | `TaskId` | Derived — §15.1 |
| `ContextPayload` | `Items` | Explicit — §15.1 |
| `ContextPayload` | `Truncated` | Explicit — §15.1/§15.2 |
| `ContextPayload` | `Items[].Size` | Derived — §20.1 postcondition |
| `MemoryRef` | `Content` | Derived — §16.2 (`source_memory.content`) |
| `MemoryRef` | `Origin` | Derived — §16.2 (`source_memory.origin`) |
| `MemoryRef` | *(identity/key)* | **Not proposed — no evidence anywhere** |
| `EpisodicEntryRef` | `Id` | Derived — §16.2 (`episodic_entry.id`) |
| `EpisodicEntryRef` | *(any other field)* | **Not proposed — no evidence anywhere** |

## Open Questions

- `MemoryRef`'s identity/construction — genuinely unresolved by any reviewed document.
- `EpisodicEntryRef`'s shape beyond `.Id` — genuinely unresolved.
- `source_memory.mark_consolidated()` is evidenced only as a behavior (method), not a field — not schematized here.

## Decision Required

The evidenced fields above can be accepted as design input without further ratification. `MemoryRef`'s and `EpisodicEntryRef`'s identity gaps require an explicit Product Owner/Architect decision (they cannot be resolved by further specification search — confirmed via repeated re-reading across two review passes) before either type can be implemented.
