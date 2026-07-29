# WP-014 Final Implementation Plan (Approved, Governing Version) — Memory: Full Retrieval Strategy & Mechanical Ranking

**Status: this is the version that actually governed the delivered implementation.** It supersedes the original Implementation Plan's Decision 9 (conditional vector stage) and the subsequent revision's G1 embedding-adapter design, both of which were found during implementation to have no specification-sanctioned production caller and were removed by explicit direction. See `docs/Architecture-Gaps/AG-0001-WP014-Hybrid-Retrieval-Inconsistency.md` for the full discovery record.

**Source of Truth (priority order):** `docs/EOS-Specification.md`, `docs/Memory-Management-Specification-v1.0.md` (§10, §12, §13, §14, §19, §20.1), `docs/EOS-Implementation-Roadmap-v1.0.md`, `docs/Learning-Engine-Specification-v1.1.md` (§11.2, §14.3), `Architecture-Review.md`, `Architecture-Challenge.md` (G3 only — G1 superseded), and `docs/Architecture-Gaps/AG-0001-WP014-Hybrid-Retrieval-Inconsistency.md`.

## Objective (roadmap, verbatim)

Implement the full seven memory-type Storage Strategy, hybrid symbolic+vector Retrieval Strategy, and the mechanical Retrieval Ranking formula.

## Architecture Decisions (final, governing)

1. **G3 (unchanged, as frozen in `Architecture-Challenge.md`) — `MemoryType` mapping:** `Working`/`ShortTerm`/`Session` → Redis; `Episodic` → `NodeType == Lesson`; `Semantic` → `NodeType in {Fact, Pattern}`; `LongTerm` → `NodeType in {Fact, Pattern, Decision, Risk}`; `Project` → `domain_tags` post-filter. `BestPractice`/`Principle`/`GoldenPath` remain `EOS.Learning`'s `PipelineRecord.stage` vocabulary only.
2. **G1 (superseded) — no embedding adapter, no `EOS.VectorStore` production code, no `Providers.json`/`Program.cs` embedding wiring.** Neither `query()` nor `query_similar()`'s ratified signature (Memory-Management-Specification §20.1) contains a field that could supply content to embed. `query()` has only `MemoryType?`/`domain_tags?`/`DateRange?` (all structural filters). `query_similar(KnowledgeGraphRef ref)` takes only an existing-node reference. Building an embedding-generation path for either method would be infrastructure with no specification-sanctioned caller.
3. **`query()` implements symbolic retrieval only** (Memory-Management-Specification §13 stage 1), exactly matching its three parameters.
4. **`query_similar(ref)` implements a real symbolic candidate pool, not a vector-similarity search and not an empty/validate-only stub.** Per Learning-Engine-Specification-v1.1 §11.2 (`candidates = Knowledge.query_similar(record.knowledge_graph_ref)` followed by a *separate* `ReasoningEngine.compare(record, candidates)` call) and §14.3's precondition/postcondition (node-status and self-exclusion only, no similarity-score expectation), and Memory-Management-Specification §5's Non-Responsibilities ("Semantic similarity computation... Reasoning Engine"), `query_similar()`'s specified contract is: resolve the ref, return other nodes sharing its `NodeType`, excluding itself, ranked mechanically. The actual similarity judgment happens afterward, elsewhere, in `ReasoningEngine.compare()` — never in Memory.
5. **Hybrid vector retrieval is completed later, by `ContextAssembler`/`assemble_context()` (§15/WP-015)** — confirmed via §9's component diagram (`EOS.VectorStore` wired only to `ContextAssembler`/`LifecycleEngine`, never to `query()`/`query_similar()`'s associated components), §13 stage 3 (naming `ContextAssembler` explicitly), and §23.2's sequence diagram (showing the vector-similarity call inside the `assemble_context()` flow, after and separate from `query()`). This is documented as a roadmap-wording/detailed-specification inconsistency in AG-0001, not implemented around.
6. **Ranking-weight configuration stays in `ThresholdsOptions`/`Thresholds.json`**, per Memory-Management-Specification §19.1's direct, explicit citation of `Thresholds.json` by name.
7. **Redis and `EOS.KnowledgeGraph` are real, production infrastructure this WP.** `EOS.VectorStore` remains an empty scaffold (unchanged from pre-WP-014 state) — no production caller exists for it in this WP's final scope.

## Scope Implemented

- `IKnowledgeClient` gains `QueryAsync(MemoryType?, string[]?, DateRange?, CancellationToken)` and `QuerySimilarAsync(Guid, CancellationToken)`.
- `MemoryType` (enum), `DateRange` (record), `RankingWeights` (record) — all additive, `EOS.Knowledge`-owned.
- `RetrievalRanking.Rank` — §19.1's four-term mechanical formula (`vector_similarity` and `access_frequency` structurally present, always zero and disclosed as such; `recency_decay` and `domain_match` real).
- `KnowledgeGraphStore.QueryAsync` — symbolic filter (NodeType set + CreatedAt range), raw ADO.NET, parameterized.
- `RedisMemoryStore` (`EOS.Infrastructure`) — minimal `SetAsync`/`GetAsync` with optional TTL, real and tested, no `IKnowledgeClient` caller (its retrieval surface belongs to future runtime components owning ephemeral execution state, per the resolved query-API boundary).
- `ThresholdsOptions`/`config/Thresholds.json` — four additive ranking-weight fields.
- `Program.cs` — translates `ThresholdsOptions` into `RankingWeights`, constructs `KnowledgeClient` with it. No embedding-related wiring.

## Explicit Non-Scope

- `assemble_context()`, `consolidate()`, `ContextPayload`, `ContextRequest`, `EpisodicEntryRef` — WP-015.
- Compression (§17), Expiration (§18) — WP-016.
- Any Constitution/roadmap/specification edit.
- Any change to `KnowledgeNodeType`, `KnowledgeNode`'s schema, or `EOS.Contracts`.
- Any embedding generation, `IEmbeddingProviderClient` consumption, `EOS.VectorStore` production read/write code, `Providers.json` embedding-model registration.
- Dispatch of `Working`/`ShortTerm`/`Session` `MemoryType`s inside `query()` — they are real Redis storage strategies but never `KnowledgeNode` instances; `query()` throws `NotSupportedException` for them, documented on the interface.

## Files Modified (final)

- `src/EOS.Knowledge/IKnowledgeClient.cs`, `KnowledgeClient.cs`
- `src/EOS.KnowledgeGraph/KnowledgeGraphStore.cs`
- `src/EOS.SharedKernel/Configuration/ThresholdsOptions.cs`
- `config/Thresholds.json`
- `src/EOS.Runner/Program.cs`
- `tests/EOS.Knowledge.Tests/KnowledgeClientTests.cs` (mechanical constructor-argument fix)
- `tests/EOS.Runner.Tests/AskCommandIntegrationTests.cs` (mechanical constructor-argument fix + interface-member stubs)

## Files Created (final)

- `src/EOS.Knowledge/MemoryType.cs`, `DateRange.cs`, `RankingWeights.cs`, `RetrievalRanking.cs`
- `src/EOS.Infrastructure/RedisMemoryStore.cs`
- `tests/EOS.Knowledge.Tests/KnowledgeClientQueryTests.cs`, `RetrievalRankingTests.cs`
- `tests/EOS.Infrastructure.Tests/RedisMemoryStoreTests.cs`

## Files Explicitly Not Created / Removed During Implementation

- `EOS.VectorStore`'s production source files (a ChromaDB client) and `EOS.VectorStore.Tests` were built, then removed once discovered to have no legitimate production caller — `EOS.VectorStore` remains at its pre-WP-014 empty-scaffold state.
- `config/Providers.json`'s embedding-model registration was added, then reverted for the same reason.

## Dependency Validation

Zero new `ProjectReference`s anywhere. Zero `.csproj`/`.slnx` changes relative to `main`. `EOS.Knowledge.csproj` unchanged (`EOS.KnowledgeGraph`, `EOS.VectorStore` only).

## Testing Strategy

Real infrastructure only (SQL Server, Redis) — no mocking framework. `KnowledgeClientQueryTests` verifies `MemoryType` dispatch (including the `NotSupportedException` cases and the real `query_similar()` candidate-pool behavior) against real SQL Server data. `RetrievalRankingTests` verifies the formula deterministically. `RedisMemoryStoreTests` verifies the Redis wrapper against a real Redis instance, including TTL expiry.

## Acceptance Criteria (roadmap, verbatim)

"A seeded set of test knowledge items returns in the expected rank order for a sample query." Satisfied by `RetrievalRankingTests` and `KnowledgeClientQueryTests`.

## Risks (final, disclosed)

- `RedisMemoryStore` has no wired `IKnowledgeClient` caller — accepted, matches the WP-011 `AIProviderManager.EmbedAsync` precedent.
- `access_frequency` ranking term has no data source anywhere in the codebase — accepted, structurally present, disclosed in `RetrievalRanking.cs`.
- The roadmap/specification wording inconsistency around "hybrid symbolic+vector Retrieval Strategy" — tracked independently in AG-0001, does not block this WP's closure.
