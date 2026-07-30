# WP-015 Bootstrap Document

This document does not design or implement WP-015. It records only what WP-015 inherits from the now-frozen WP-014 baseline, per `docs/EOS-Implementation-Roadmap-v1.0.md`'s WP-015 row ("Memory: Context Assembly & Consolidation").

## Explicit Statement

**WP-015 starts from the frozen WP-014 baseline.** WP-014 (`docs/WP-014/`) is closed and immutable. No WP-015 activity may modify any WP-014 file except through the specific, additive extension points listed below.

## Inputs from WP-014

- `IKnowledgeClient.QueryAsync(MemoryType?, string[]?, DateRange?, CancellationToken)` — real symbolic retrieval, returning mechanically-ranked `KnowledgeNode` results.
- `IKnowledgeClient.QuerySimilarAsync(Guid, CancellationToken)` — real symbolic candidate-pool retrieval.
- `MemoryType`, `DateRange`, `RankingWeights` (`EOS.Knowledge`).
- `RetrievalRanking.Rank` (`EOS.Knowledge`) — the §19.1 mechanical formula, with `vector_similarity` and `access_frequency` terms structurally present and currently zero.
- `KnowledgeGraphStore.QueryAsync` (`EOS.KnowledgeGraph`) — the symbolic filter query WP-015's own retrieval needs (if any) would build on, not duplicate.
- `RedisMemoryStore` (`EOS.Infrastructure`) — real, tested Working/Short-term/Session storage, with no `IKnowledgeClient` caller yet.
- `ThresholdsOptions`'s four ranking-weight fields and `config/Thresholds.json`'s populated values.
- `Program.cs`'s existing `KnowledgeClient` construction (translates `ThresholdsOptions` into `RankingWeights`).

## Frozen Assumptions

- `KnowledgeNodeType` remains exactly `{ Fact, Lesson, Pattern, Decision, Risk }` — unchanged since §0.5.1.
- `MemoryType`'s seven values and their `KnowledgeNodeType` mapping (G3) are fixed: `Episodic→Lesson`, `Semantic→{Fact,Pattern}`, `LongTerm→{Fact,Pattern,Decision,Risk}`, `Project→domain_tags` filter, `Working`/`ShortTerm`/`Session`→Redis (never `KnowledgeNode`).
- `EOS.Knowledge`'s dependency shape (`EOS.KnowledgeGraph`, `EOS.VectorStore` only) is fixed per Constitution Part 1 §1.2 — unresolved by WP-014, still open per AG-0001.
- `EOS.VectorStore` remains an empty scaffold (only its `.csproj`) — no ChromaDB client code exists.
- No embedding-generation path exists anywhere reachable from `EOS.Knowledge`.

## Required Prerequisites (roadmap, verbatim)

"Prerequisites | WP-014" — satisfied; WP-014 is frozen and closed.

## Dependencies (roadmap, verbatim)

"Dependencies on previous WPs | WP-014" — satisfied.

## Architecture Constraints Inherited from WP-014

- The "no infrastructure without a specification-sanctioned production caller" standard applied throughout WP-014's implementation (documented in `docs/WP-014/Final-Implementation-Report.md`) carries forward: WP-015 must not build `EOS.VectorStore`/embedding-generation code speculatively — only when a concrete method in its own approved scope (`assemble_context()`/`consolidate()`) actually requires it.
- The Constitution Part 1 §1.2 dependency-table inconsistency identified in WP-014's Architecture Review (Gap 1) was never resolved — only avoided, by determining WP-014 itself had no legitimate need for it. WP-015's own Architecture Review must independently re-examine whether `assemble_context()`/`consolidate()` requires the same `EOS.Knowledge`→embedding-channel reachability, since §14 (Indexing Strategy) ties embedding generation to `consolidate()` specifically.
- The "project not permitted to depend on a richer layer defines its own plain type; composition root translates" pattern (established across WP-004/010/011/012/013, reused in WP-014 for `RankingWeights`) remains the applicable precedent for any similar dependency-boundary question WP-015 encounters.
- `IKnowledgeClient`'s existing members (`UpdateAsync`, `QueryAsync`, `QuerySimilarAsync`) must remain unchanged in signature — WP-015 may only add new members (`assemble_context()`/`consolidate()`), matching the additive-extension discipline already used by WP-011 and WP-014.

## Files WP-015 May Modify

Per the roadmap's WP-015 row ("Projects affected | `EOS.Knowledge`"):

- `src/EOS.Knowledge/**` (additive: new `assemble_context()`/`consolidate()` members and supporting types)
- `src/EOS.Runner/Program.cs` (composition-root wiring only, if WP-015's own Architecture Review determines it's needed)
- Associated new test files under `tests/EOS.Knowledge.Tests/`
- `config/*.json` / `EOS.SharedKernel/Configuration/*` (additive fields only, if WP-015's own Architecture Review determines new configuration is needed)

## Files WP-015 Must Never Modify

- Any file under `docs/WP-014/` (frozen)
- `docs/Architecture-Gaps/AG-0001-WP014-Hybrid-Retrieval-Inconsistency.md` (tracked independently; WP-015 may reference it but not edit it)
- `docs/WP-014-Requirements-Traceability-Matrix.md`
- `docs/EOS-Specification.md`, `docs/Memory-Management-Specification-v1.0.md`, `docs/Learning-Engine-Specification-v1.1.md`, `docs/EOS-Implementation-Roadmap-v1.0.md`
- `src/EOS.Contracts/**`, `src/EOS.KnowledgeGraph/KnowledgeNodeType.cs`
- `src/EOS.Knowledge/IKnowledgeClient.cs`'s existing member signatures (`UpdateAsync`, `QueryAsync`, `QuerySimilarAsync`) and `src/EOS.Knowledge/MemoryType.cs`'s existing seven values
- `src/EOS.Gates/**`, `src/EOS.Reasoning/**`, `src/EOS.Orchestrator/**`, `src/EOS.Learning/**` — no cited requirement in the WP-015 roadmap row touches these

## AG-0001 Impact

AG-0001 documents that the vector-retrieval stage belongs to `ContextAssembler`/`assemble_context()` — i.e., **to WP-015 itself**, not to WP-014. This means WP-015's own Architecture Review must treat the embedding-reachability question (Constitution Part 1 §1.2's Gap 1, restated in AG-0001) as directly in scope, since §14's Indexing Strategy ties embedding generation to `consolidate()`, and §13/§23.2 tie the vector-similarity stage to `assemble_context()`'s `ContextAssembler` component — both are WP-015 responsibilities per the roadmap. AG-0001 remains tracked independently in `docs/Architecture-Gaps/` and does not itself block WP-015 from starting; it identifies work WP-015's own Architecture Review must account for.

---

**WP-015 starts from the frozen WP-014 baseline described above. No design or implementation decision for WP-015 is made in this document.**
