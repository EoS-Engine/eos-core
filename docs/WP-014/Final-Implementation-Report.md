# WP-014 Implementation — Final Report (Post-Correction, Governing Version)

This is the final implementation report, produced after `query_similar()` was corrected from an empty/validate-only stub to a real symbolic candidate pool. It supersedes any earlier, intermediate report produced during this WP's implementation.

## 1. Files Created

- `src/EOS.Knowledge/MemoryType.cs`
- `src/EOS.Knowledge/DateRange.cs`
- `src/EOS.Knowledge/RankingWeights.cs`
- `src/EOS.Knowledge/RetrievalRanking.cs`
- `src/EOS.Infrastructure/RedisMemoryStore.cs`
- `tests/EOS.Knowledge.Tests/KnowledgeClientQueryTests.cs`
- `tests/EOS.Knowledge.Tests/RetrievalRankingTests.cs`
- `tests/EOS.Infrastructure.Tests/RedisMemoryStoreTests.cs`

## 2. Files Modified

- `src/EOS.Knowledge/IKnowledgeClient.cs` — added `QueryAsync`/`QuerySimilarAsync`, both documented with their architectural boundaries inline.
- `src/EOS.Knowledge/KnowledgeClient.cs` — implemented both new methods.
- `src/EOS.KnowledgeGraph/KnowledgeGraphStore.cs` — added `QueryAsync` (symbolic stage, parameterized `NodeType`/`CreatedAt` filter).
- `src/EOS.SharedKernel/Configuration/ThresholdsOptions.cs` — four additive ranking-weight fields.
- `config/Thresholds.json` — populated the four new fields.
- `src/EOS.Runner/Program.cs` — translates `ThresholdsOptions` into `RankingWeights`, passes into `KnowledgeClient`.
- `tests/EOS.Knowledge.Tests/KnowledgeClientTests.cs` — mechanical constructor-argument fix.
- `tests/EOS.Runner.Tests/AskCommandIntegrationTests.cs` — mechanical constructor-argument fix + new interface-member stubs on both test doubles.

**Not modified relative to `main`:** any `.csproj`, `EOS.slnx` (net-zero after an add-then-revert during implementation), `config/Providers.json` (reverted), `EOS.Contracts`, `EOS.SDK`, `KnowledgeNodeType.cs`, `EOS.Learning`, `EOS.Gates`, `EOS.Reasoning`, `EOS.Orchestrator`, all `docs/*.md` governing specifications.

## 3. Build Result

```
dotnet build EOS.slnx → Build succeeded. 0 Warning(s), 0 Error(s)
```

## 4. Test Result

165 total, all passing (sequential per-project, real infrastructure only): `EOS.ArchitectureTests` 3/3, `EOS.Gates.Tests` 66/66, `EOS.Orchestrator.Tests` 5/5, `EOS.Knowledge.Tests` 28/28 (16 pre-existing + 12 new), `EOS.Infrastructure.Tests` 17/17 (14 pre-existing + 3 new), `EOS.AIProvider.Tests` 30/30, `EOS.Reasoning.Tests` 5/5, `EOS.Runner.Tests` 11/11. Zero regressions.

## 5. Architecture Compliance Verification

- **G1 — superseded, not violated:** the entire embedding-adapter path (composition-root adapter, `Providers.json` wiring, `Program.cs` registration) was removed once discovered to have no legitimate production caller — an authorized scope reduction, documented in AG-0001.
- **G3 — implemented exactly as approved:** `Episodic→Lesson`, `Semantic→{Fact,Pattern}`, `LongTerm→{Fact,Pattern,Decision,Risk}`, `Project→domain_tags` filter, verified by integration tests against real SQL Server data.
- **Query-API boundary:** `Working`/`ShortTerm`/`Session` are real, tested Redis infrastructure but are never dispatched inside `KnowledgeClient.QueryAsync` — calling `query()` with any of the three throws `NotSupportedException`, documented on the interface as an architectural limitation.
- **`query_similar()` — corrected, final behavior:** resolves the ref, builds a real symbolic candidate pool (other nodes sharing the resolved node's `NodeType`, excluding itself), ranked via the mechanical formula using the node's own `DomainTags` as scope. Matches Learning-Engine-Specification-v1.1 §11.2/§14.3's real, already-approved consumption exactly.
- **`EOS.VectorStore` removal:** applied the "no infrastructure without a legitimate production caller" rule consistently — since neither retrieval method calls it, the ChromaDB client and its test project were removed; `EOS.VectorStore` is back to its pre-WP-014 empty-scaffold state.
- **Ranking weights placement:** kept in `ThresholdsOptions`/`Thresholds.json`, per Memory-Management-Specification §19.1's direct citation.
- **KISS/YAGNI:** no code path is known in advance to be vacuous on every call — `query()` performs real symbolic retrieval; `query_similar()` performs a real, meaningful candidate-pool query.

## 6. Dependency Verification

Zero `.csproj` changes anywhere in the solution. `EOS.Knowledge.csproj` unchanged (`EOS.KnowledgeGraph`, `EOS.VectorStore` only). `NoCircularProjectReferencesTests` and `OnlyAllowedProjectsMayReferenceAIProviderTests` both pass unmodified.

## 7. Git Status (at report time)

On branch `wp-014-memory-retrieval-strategy-mechanical-ranking`, created from `main` at `3665860`, all implementation changes uncommitted pending final authorization. `git diff --check` and `dotnet format --verify-no-changes` both clean.

## 8. Issues Encountered

- Two genuine specification gaps were discovered only during implementation, not during the Architecture Review/Challenge/Implementation Plan phases: (a) `MemoryType` had no representable shape for Redis-backed types under `query()`'s `KnowledgeNode`-typed return; (b) neither retrieval method's signature has a text input to embed, removing the G1 embedding path entirely. Both are documented in AG-0001.
- Recency ranking uses `KnowledgeNode.CreatedAt` (the only timestamp field that exists) rather than a "last updated" timestamp §19.1's prose describes — `CreatedAt` is deliberately never rewritten on update (WP-007 decision). Disclosed in `RetrievalRanking.cs`'s doc comment, not treated as a blocking gap.
- A strict Specification Compliance Review found that an initially-proposed "fetch stored embedding by NodeId" design for `query_similar()` was only **indirectly implied** by §14's "synchronous... pairing" wording, not explicitly specified — classified as Category B and not implemented, consistent with the "explicitly specified only" standard applied throughout this WP's governance process.

## 9. Architecture Self-Review Summary

Every modified/created file was reviewed for correctness. `KnowledgeGraphStore.QueryAsync` uses fully parameterized SQL (no injection risk). `KnowledgeClient`'s dispatch logic matches G3 exactly. Both new `IKnowledgeClient` methods carry XML doc comments explaining their real, specification-derived behavior. No forbidden file was touched. No hidden dependency edge was introduced.

## 10. Final Recommendation

Implementation compliant with the detailed, method-level specifications and ready for closure — with the roadmap-wording gap tracked independently in AG-0001, not blocking.
