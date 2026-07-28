# WP-014 Freeze Report

This is a historical record only. It does not reinterpret any prior decision.

## Freeze Date

2026-07-28

## Current Branch

`wp-014-memory-retrieval-strategy-mechanical-ranking`

## Current Commit Hash

`3665860d43e9b37c0cea0604a65d90fcc70d1ce4` (base commit — WP-013's completion report on `main`). WP-014's implementation exists as uncommitted working-tree changes on this branch, per the standing instruction not to commit until explicit authorization is given.

## Final Implementation Status

Complete, per `Final-Implementation-Report.md`. `IKnowledgeClient` gained `QueryAsync` (symbolic retrieval, per Memory-Management-Specification §13 stage 1) and `QuerySimilarAsync` (symbolic candidate pool, per Learning-Engine-Specification-v1.1 §11.2/§14.3). The mechanical ranking formula (§19.1) is implemented in full. `MemoryType`↔`KnowledgeNodeType` mapping (G3) is implemented exactly as approved. `EOS.VectorStore` remains at its pre-WP-014 empty-scaffold state; no embedding-generation path was built.

## Architecture Status

Frozen. G3 stands as approved and implemented. G1 (Composition Root Adapter Pattern for embedding access) was authorized, built, then found during implementation to have no specification-sanctioned production caller, and was removed by explicit direction — this correction is documented in `Architecture-Challenge.md`'s supersession notice, `Final-Implementation-Plan.md`, and AG-0001. No further architecture changes are permitted against this WP.

## Documentation Status

Complete. All six required governance artifacts exist under `docs/WP-014/` (or their established locations) and are cross-linked from `docs/WP-014/README.md`:

- `Architecture-Review.md`
- `Architecture-Challenge.md`
- `Final-Implementation-Plan.md`
- `../Architecture-Gaps/AG-0001-WP014-Hybrid-Retrieval-Inconsistency.md`
- `../WP-014-Requirements-Traceability-Matrix.md`
- `Final-Implementation-Report.md`

## Test Status

165 total tests passing, sequential per-project run against real infrastructure (SQL Server, Redis; ChromaDB available but unused by production code), zero regressions:

| Project | Passed |
|---|---|
| `EOS.ArchitectureTests` | 3/3 |
| `EOS.Gates.Tests` | 66/66 |
| `EOS.Orchestrator.Tests` | 5/5 |
| `EOS.Knowledge.Tests` | 28/28 |
| `EOS.Infrastructure.Tests` | 17/17 |
| `EOS.AIProvider.Tests` | 30/30 |
| `EOS.Reasoning.Tests` | 5/5 |
| `EOS.Runner.Tests` | 11/11 |

Build: 0 Warnings, 0 Errors. `dotnet format --verify-no-changes`: clean. `git diff --check`: clean. Zero `.csproj`/`.slnx` changes relative to `main`. No forbidden file (Constitution, either specification, roadmap, `EOS.Contracts`, `EOS.SDK`, `KnowledgeNodeType.cs`, `EOS.Gates`, `EOS.Reasoning`, `EOS.Orchestrator`, `EOS.Learning`) modified.

## Known Accepted Limitations

- `RedisMemoryStore` (`EOS.Infrastructure`) has no wired `IKnowledgeClient` caller — real, tested infrastructure whose retrieval surface belongs to future runtime components owning ephemeral execution state (Reasoning/Orchestrator/Context Assembly), not to the Knowledge query API. Matches the accepted WP-011 `AIProviderManager.EmbedAsync` precedent.
- The ranking formula's `access_frequency` term has no data source anywhere in the codebase — structurally present, always evaluates to zero, disclosed in `RetrievalRanking.cs`.
- The ranking formula's `vector_similarity` term always evaluates to zero within WP-014 — no embedding/vector mechanism exists in this WP's scope; see Architecture Gaps below.
- Recency ranking uses `KnowledgeNode.CreatedAt` rather than a "last updated" timestamp, since no such field exists on `KnowledgeNode` (WP-007 deliberately never rewrites `CreatedAt` on update).
- A stray ChromaDB collection (`eos-knowledge-test-probe`) created during API-shape verification remains in the shared development ChromaDB instance; its `DELETE` endpoint did not succeed on retry. External environment state only — not a repository artifact, does not affect any test or production code path.

## Architecture Gaps

- **AG-0001** (`docs/Architecture-Gaps/AG-0001-WP014-Hybrid-Retrieval-Inconsistency.md`): the roadmap's WP-014 wording ("hybrid symbolic+vector Retrieval Strategy") is inconsistent with the detailed Memory-Management-Specification's component architecture (§9, §13, §23.2), which assigns the vector-retrieval stage to `ContextAssembler`/`assemble_context()` (WP-015). Classified as an Architecture Documentation Inconsistency, not an implementation, architecture, or code defect. Remains tracked independently and does not block WP-014's closure or WP-015's start.

## Closure Statement

**WP-014 is closed.**

**All future work must continue in WP-015.** No further implementation, cleanup, optimization, refactoring, architectural improvement, documentation rewrite, or specification interpretation against WP-014 is authorized from this point forward. WP-014 is the frozen baseline for all subsequent Work Packages.
