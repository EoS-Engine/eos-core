# ADR-015-001 — Embedding Generation Reachability

## Status

**Accepted**

## Background

WP-015 must implement `consolidate()` (Memory-Management-Specification-v1.0 §16.2), whose algorithm requires `EOS.Knowledge` to invoke embedding generation on real content. `EOS.Knowledge`'s Constitution-declared dependency shape does not include a path to the project where that capability lives. This gap was first identified during WP-014's Architecture Review (Gap 1) and re-confirmed, unresolved, during WP-015's own Architecture Review and Challenge (Challenge 2, classified Missing Specification).

## Problem Statement

How may `EOS.Knowledge` legally invoke `EmbeddingProvider.embed(...)` given that Constitution Part 1 §1.2 grants it no dependency path to `EOS.SDK`/`EOS.AIProvider`?

## Alternatives Considered

1. **Amend Constitution Part 1 §1.2** to add `EOS.SDK` to `EOS.Knowledge`'s "Depends On" column.
2. **Composition Root Adapter Pattern** — `EOS.Knowledge` defines a small, BCL-typed interface; `EOS.Runner`'s `Program.cs` supplies the concrete adapter, internally invoking `AIProviderManager`.
3. **Route the embed call through `EOS.Reasoning`**, which already has `EOS.SDK` access.

## Decision

Adopt Alternative 2 — the Composition Root Adapter Pattern.

## Rationale

Alternative 1 requires an out-of-band Constitutional amendment (Constitution §0.6: Constitutional amendment requires CTO + Principal Engineer consensus and human sign-off) before any code can be written — disproportionate for a per-WP need this specification-driven and already has a proven, lower-cost answer. Alternative 3 is directly contradicted by Memory-Management-Specification §4, which assigns "invoking (not owning) embedding generation" to Memory itself, not to Reasoning. Alternative 2 requires zero Constitution edit, zero new `ProjectReference`, and reuses an already-merged, working pattern in this exact codebase (`IProviderEventLogger`/`LoggerProviderEventLogger`, WP-010/011) — with a genuine, specification-required caller this time, unlike WP-014's earlier, later-removed attempt at the same pattern.

## Consequences

- `EOS.Knowledge` gains a new, small, BCL-typed interface for embedding generation, implemented by `Program.cs`.
- Constitution Part 1 §1.2's internal inconsistency (the `EOS.AIProvider` row presupposing an "embed" channel the `EOS.Knowledge` row does not grant) remains permanently undocumented at the table level. This is accepted, bounded technical/documentation debt, not a blocking condition — any future WP with the same need re-applies the same adapter pattern at the same low cost.

## Specification References

- Memory-Management-Specification-v1.0 §16.2: "embedding = EmbeddingProvider.embed(episodic_entry.content)    # delegated, §14"
- Memory-Management-Specification-v1.0 §14: "Memory owns *when* to index, never *how* the embedding model computes the vector (FR-M3)."
- Memory-Management-Specification-v1.0 §4: "invoking (not owning) embedding generation and summarization *content* generation to an AI Provider (§0.14) via a defined client interface — never compute these itself."

## Constitution References

- Part 1 §1.2: `EOS.Knowledge | Principal Engineer | EOS.KnowledgeGraph, EOS.VectorStore | Role projects`
- Part 1 §1.2: `EOS.AIProvider | ... | A third consumer channel beyond EOS.Reasoning (`infer`) and EOS.Knowledge (`embed`)`

## Impact Analysis

No dependency-table edge added. No Constitution edit. No new project. `EOS.Knowledge.csproj` remains unchanged. `Program.cs` gains one adapter class and one construction call at implementation time (not performed by this ADR).

## Future Work

Any future WP needing `EOS.Knowledge`↔`EOS.SDK`/`EOS.AIProvider` reachability should default to this same pattern unless the Board separately elects to close Constitution Part 1 §1.2's underlying inconsistency directly.
