# WP-015 Specification Clarifications (Non-ADR Governance Items)

## Purpose

This document covers three open items from WP-015's Architecture Governance process that do **not** warrant a full Architecture Decision Record, and explains why for each. An ADR records a decision among genuine architectural alternatives with real trade-offs; each item below either has no competing alternative (resolved by direct, already-established precedent) or is a factual bookkeeping correction rather than a design choice.

---

## Item 1 — `EpisodicEntryRef` Definition

**Why this is not an ADR:** Unlike `MemoryRef` (ADR-015-004), `EpisodicEntryRef`'s only evidenced usage is as `consolidate()`'s return type and as `episodic_entry.id` — the identifier of a node just created via `KnowledgeGraph.create_node(type=Lesson, ...)` (§16.2). This is exactly the situation `KnowledgeGraphRef` was already resolved for in WP-007: a reference that always resolves to an *existing graph node*. There is no competing alternative to weigh — the same precedent that resolved `KnowledgeGraphRef` applies here without modification, because the usage context is identical (a freshly-created graph node's own identifier), unlike `MemoryRef`'s need to also cover non-graph-backed ephemeral sources.

**Resolution:** `EpisodicEntryRef` = `Guid`, identical to `KnowledgeGraphRef`, by direct application of WP-007's own precedent (`docs/WP-007-Implementation-Plan.md`: "`KnowledgeGraphRef` = `Guid`, identical to `NodeId`.").

**Evidence:** Memory-Management-Specification-v1.0 §16.2: "episodic_entry = KnowledgeGraph.create_node(...)... `VectorStore.index(episodic_entry.id, embedding)`"; §20.1: "`EpisodicEntryRef consolidate(...)`."

**Classification:** Resolved by precedent — no governance decision required.

---

## Item 2 — `EOS.VectorStore` Ownership Bookkeeping

**Why this is not an ADR:** There is no architectural alternative to weigh — `EOS.VectorStore`'s ownership (Principal Engineer, per Constitution Part 1 §1.2) is not in question, and no competing design exists for *who builds it*. The only gap is that the roadmap's own "Projects affected" listing never names `EOS.VectorStore` for WP-015 (or any WP after WP-014), while §16.2 — WP-015's own cited related section — requires real `VectorStore.index(...)` code. This is a factual completeness gap in the roadmap document, not a decision between alternatives.

**Resolution:** No architectural decision needed. Recorded here as a factual finding: `EOS.VectorStore` production code (a real `index()`/write capability) is required as part of WP-015's own scope, per §16.2, regardless of the roadmap's "Projects affected" listing.

**Evidence:** Roadmap WP-014 row: "Projects affected | `EOS.Knowledge`, `EOS.KnowledgeGraph`, `EOS.VectorStore`"; Roadmap WP-015 row: "Projects affected | `EOS.Knowledge`"; Memory-Management-Specification-v1.0 §16.2: "`VectorStore.index(episodic_entry.id, embedding)`."

**Classification:** Factual finding, not a decision — no ADR required.

---

## Item 3 — Roadmap Correction Necessity

**Why this is not an ADR:** Whether to *edit the roadmap document* is an editorial/documentation action, not an architecture decision — there are no competing architectural designs to choose between. This is functionally identical in kind to AG-0001's own finding for WP-014 (a roadmap-wording gap, tracked as documentation, not resolved via ADR).

**Resolution:** Recommend the roadmap's WP-015 row be corrected to add `EOS.VectorStore` under "Projects affected," for the same reason Item 2 identifies. This edit is **not performed by this document** — it requires separate, explicit authorization, consistent with the standing instruction not to modify the roadmap during governance review.

**Classification:** Documentation correction candidate, tracked here — not an ADR, not performed.

---

## Summary

| Item | Governance Artifact | Why Not an ADR |
|---|---|---|
| `EpisodicEntryRef` | This document | Resolved by direct, already-established precedent (`KnowledgeGraphRef` = `Guid`); no competing alternative. |
| `EOS.VectorStore` ownership | This document | Factual completeness gap in the roadmap's own bookkeeping, not a design choice between alternatives. |
| Roadmap correction | This document | An editorial action, not an architectural decision; parallels AG-0001's own treatment of a roadmap-wording gap. |

`MemoryRef` (ADR-015-004) was the only one of the four originally-flagged "Missing Specification" items that involved a genuine design choice among real alternatives, and was therefore given full ADR treatment.
