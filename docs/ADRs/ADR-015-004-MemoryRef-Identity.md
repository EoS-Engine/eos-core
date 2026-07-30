# ADR-015-004 — `MemoryRef` Identity

## Status

**Accepted**

## Background

`consolidate(MemoryRef source, string reason, string[] evidence_refs)` (§20.1) and §16.2's algorithm reference `source_memory.content`, `source_memory.origin`, and `source_memory.mark_consolidated()` — but no document anywhere defines `MemoryRef`'s concrete shape or identity field. Unlike `KnowledgeGraphRef` (resolved to `Guid` in WP-007 by unanimous prior-WP precedent, since it always resolves to an *existing graph node*), `MemoryRef` must represent content that may originate from Working/Short-term/Session Memory (Redis-backed, no graph node exists yet) as well as content flagged during Gate-failure/`IncidentResolved`-triggered consolidation. The `KnowledgeGraphRef`-as-`Guid` precedent does not cleanly transfer, since a bare `Guid` does not indicate which store (or none) the referenced content actually lives in.

## Problem Statement

What is `MemoryRef`'s concrete shape, sufficient to support `.content`, `.origin`, and `.mark_consolidated()` against sources that may be Redis-backed (Working/Short-term/Session) or ad-hoc (Gate-failure/incident context)?

## Alternatives Considered

1. **Bare `Guid`**, by direct analogy to `KnowledgeGraphRef`'s WP-007 resolution.
2. **A small record combining the already-approved `MemoryType` (WP-014, G3) with a string key** identifying the source within that memory type's backing store (e.g., a Redis key for Working/Short-term/Session; a caller-supplied identifier for ad-hoc/incident-originated content).
3. **Inline content-carrying record** (embeds `Content`/`Origin` directly at construction, no separate key).

## Decision

Adopt Alternative 2 — `MemoryRef` combines a `MemoryType` value (reusing WP-014's already-ratified enum) with a string key locating the source within that memory type's backing store.

## Rationale

Alternative 1 was directly tested against the evidence and does not hold: `KnowledgeGraphRef`'s precedent applies specifically because it always resolves to an *existing graph node* — `MemoryRef` must also represent ephemeral, non-graph-backed content, for which a bare `Guid` conveys no information about where to actually retrieve `.content`/`.origin` from, or where `.mark_consolidated()` should write its flag. Alternative 3 cannot support `.mark_consolidated()`'s implied side effect (§16.2: "so it is not double-consolidated on natural expiry") without some durable location to write the consolidated marker to — an inline, position-less record has nowhere to persist that flag. Alternative 2 reuses WP-014's already-ratified `MemoryType` taxonomy rather than inventing a new one, and gives `.mark_consolidated()` a concrete addressable location (the store identified by `MemoryType` + key) to write to, satisfying the idempotency requirement.

## Consequences

- `MemoryRef`'s shape is now fixed as (`MemoryType`, key) for implementation purposes.
- `Content`/`Origin` are not stored directly on `MemoryRef` itself — they must be resolved by dereferencing the (`MemoryType`, key) pair against the appropriate backing store at `consolidate()` call time. This is a real implementation detail carried forward into the eventual Implementation Plan, not resolved here.
- This decision is not directly evidenced by any specification text — it is the smallest design consistent with `MemoryRef`'s observed usage and WP-014's already-ratified `MemoryType` taxonomy, arrived at because the one candidate precedent (`KnowledgeGraphRef` = `Guid`) was tested and found not to transfer.

## Specification References

- Memory-Management-Specification-v1.0 §16.2: "source_memory.content", "source_memory.origin", "source_memory.mark_consolidated()   # so it is not double-consolidated on natural expiry"
- Memory-Management-Specification-v1.0 §20.1: "`EpisodicEntryRef consolidate(MemoryRef source, string reason, string[] evidence_refs)`"

## Constitution References

None directly — this is a Memory-Management-Specification-internal type with no Constitution-level anchor.

## Impact Analysis

No public contract change (`MemoryRef` lives in `EOS.Knowledge`, not `EOS.Contracts`, matching `IKnowledgeClient`'s own established placement). No dependency change. Concrete field types (e.g., whether the key is a `string` or something narrower) are an implementation-time detail, not decided here.

## Future Work

If a future WP introduces a memory type not covered by the (`MemoryType`, key) shape (e.g., a source with no natural string key), this ADR should be revisited.
