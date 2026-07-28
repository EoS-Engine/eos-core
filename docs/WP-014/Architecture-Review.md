# WP-014 — Architecture Review (Phases 1–12)

**Scope reviewed:** WP-014 — Memory: Full Retrieval Strategy & Mechanical Ranking (`docs/EOS-Implementation-Roadmap-v1.0.md`, Milestone 4)
**Sources read at review time:** `docs/EOS-Specification.md` (Constitution, §0.5, Part 1 §1.2, Part 3 §3.1/§3.2, Part 4 §4.1), `docs/EOS-Implementation-Roadmap-v1.0.md` (WP-007, WP-014–WP-018 rows), `docs/Memory-Management-Specification-v1.0.md` (full document, §1–§34), `docs/work-packages/WP-007-Completion-Report.md`, and the `main` HEAD state at the time (commit `3665860`) of `EOS.Knowledge`, `EOS.KnowledgeGraph`, `EOS.VectorStore`, `EOS.SDK`, `EOS.AIProvider`, `EOS.Infrastructure`, `EOS.SharedKernel.Configuration`, and `EOS.Runner/Program.cs`.

> **Historical record.** This document reproduces, verbatim, the Architecture Review as originally delivered and approved before implementation began. It is preserved unmodified per governance requirement — it is not updated to reflect later corrections discovered during implementation. See `Final-Implementation-Plan.md` and `docs/Architecture-Gaps/AG-0001-WP014-Hybrid-Retrieval-Inconsistency.md` for the governing final state.

---

## Phase 1 — Architecture Freeze Check

- WP-013 is fully closed on `main` (merge `998d003`, tag `v0.13.0-wp013`). No open WP was in flight.
- No uncommitted changes existed in the working tree relevant to this review.
- The Constitution's most recent changelog entry (2026-07-25, Part 2 §2.1 Rule 1) is a documentation clarification only, not a redesign, and does not touch Memory/Knowledge.
- **Freeze check: PASS.**

## Phase 2 — Specification Review

- `docs/Memory-Management-Specification-v1.0.md` is the sole architecture document WP-014's roadmap row cites (§10, §12, §13, §19). It is marked **Status: Proposed** (not "Approved" the way `Learning-Engine-Specification-v1.1` and `Protection-Layer-Specification-v1.0` are marked) — a real, evidenced status difference from the last two WPs' governing specs.
- The roadmap's WP-014 row narrows the specification's own scope: **In scope:** all seven memory types mapped to Part 4 stores (§10, §12); the two-stage symbolic+vector Retrieval Strategy (§13); the four-weight mechanical Retrieval Ranking formula (§19). **Explicitly excluded:** "Context Assembly's budget/truncation logic (WP-015); Consolidation/Compression/Expiration (WP-015/WP-016)." §15 (`assemble_context`), §16 (`consolidate`), §17 (compression), §18 (expiration) are out of scope for this WP.
- §21's new events (`WorkingMemoryDiscarded`, `SessionMemoryClosed`, `MemoryCompressed`, `MemoryConsolidated`, `ContextAssembled`) all map to consolidation/compression/expiration/assembly — none map to retrieval or ranking. **Confirmed: WP-014 requires zero new event emission.**

## Phase 3 — Constitution Review

- §0.5 (Knowledge Graph) reaffirmed unchanged: node types Fact/Lesson/Pattern/Decision/Risk (§0.5.1), single query interface via `EOS.Knowledge` (§0.5.2), single-store consistency guarantee (§0.5.3). The current `KnowledgeNodeType` enum matches §0.5.1 verbatim.
- Part 1 §1.2's dependency table lists `EOS.Knowledge | ... | EOS.KnowledgeGraph, EOS.VectorStore | Role projects`; `EOS.KnowledgeGraph | ... | EOS.Infrastructure`; `EOS.VectorStore | ... | EOS.Infrastructure`. The same table's `EOS.AIProvider` row states its "Never Depends On" as *"a third consumer channel beyond EOS.Reasoning (`infer`) and EOS.Knowledge (`embed`)"* — presupposing `EOS.Knowledge` is a direct consumer of an embedding capability, which its own row's dependency list does not grant. Flagged as **Gap 1**.
- Part 4 §4.1 matches Memory-Management-Specification §12's storage table exactly.
- Part 3 §3.1 already lists `KnowledgeUpdated` (producer `EOS.Knowledge`) — confirmed still unemitted (a pre-existing WP-007 gap, not WP-014's to fix).
- §0.12.1 confirms "Micro-cycle" as a real, named cycle — Working Memory's §10.1 definition is a legitimate anchor.
- No Constitution section defines `MemoryType`, `ContextRequest`, `ContextPayload`, `DateRange`, or `EpisodicEntryRef` — these can legally be defined inside `EOS.Knowledge` without touching `EOS.Contracts`, since `IKnowledgeClient` itself already lives there per WP-007's resolved placement decision.

## Phase 4 — Dependency Analysis

- `EOS.Knowledge` referenced only `EOS.KnowledgeGraph`, `EOS.VectorStore` — matches Part 1 §1.2 exactly.
- `EOS.VectorStore` was an empty scaffold — zero source files beyond the `.csproj`. No ChromaDB client code existed anywhere.
- `EOS.Infrastructure` carried `StackExchange.Redis`/`Microsoft.Data.Sqlite`/`Microsoft.Data.SqlClient` package references (pre-scaffolded since WP-001/002/004) but no Redis/SQLite data-read/write wrapper class existed yet.
- `AIProviderManager` implemented `IEmbeddingProviderClient` (WP-011) but had zero real callers on `main`.
- **Findings:** WP-014 requires real read/write code against Redis and ChromaDB — substantial new infrastructure-layer code within already-registered projects, no new projects. The one dependency edge WP-014 cannot avoid needing — `EOS.Knowledge` reaching `IEmbeddingProviderClient` — had no legal path in the current dependency table. This is Gap 1, and a hard blocker for the "hybrid symbolic+vector Retrieval Strategy" the roadmap puts in scope.

## Phase 5 — Architecture Review (SOLID / KISS / YAGNI / DRY)

- **SRP:** For WP-014's narrowed scope, only `MemoryRouter`-equivalent (classification/retrieval) and a ranking component are actually needed; instantiating `ContextAssembler`/`LifecycleEngine` now would implement WP-015/016 functionality prematurely.
- **OCP/ISP:** Extending `IKnowledgeClient` with `query()`/`query_similar()` while leaving `UpdateAsync()` untouched is additive, matching the WP-011 `DiscoverCapabilities` precedent.
- **DIP:** `IKnowledgeClient` remains the sole abstraction role projects depend on.
- **KISS/YAGNI:** The roadmap's own explicit exclusions are the correct YAGNI boundary; an implementation plan must not "get ahead" and build Context Assembly/Consolidation now.
- **DRY:** `KnowledgeNodeType` and `MemoryType` are two distinct, non-overlapping taxonomies (content-type vs. lifecycle-stage/store-location) — intentional per the specification, not a DRY violation, but `query(MemoryType type?, ...)` needs a mapping resolved without duplicating meaning — a real design surface (Gap 3).

## Phase 6 — Component Reuse Review

- `KnowledgeGraphStore`'s existing `UpsertAsync`/`GetByIdAsync` establish the raw-ADO.NET, no-ORM pattern a new symbolic-filter method should follow.
- `KnowledgeNode`/`KnowledgeNodeType` reusable as-is for Long-term/Semantic/Episodic content.
- `IKnowledgeClient`/`KnowledgeClient`'s existing additive-extension precedent (WP-011's default-interface-method) is directly reusable.
- `AIProviderManager` already implements `IEmbeddingProviderClient` in full — the gap is reachability (Gap 1), not missing functionality.
- `DataStoreConnectionOptions` already carries `RedisConnectionString`/`ChromaDbEndpoint`.
- `DataStoreHealthChecker` establishes the exact connection patterns new data-read/write code should reuse.
- `KnowledgeOptions`/`Knowledge.json` (`vectorStoreCollection`) already exists, unused.
- `ThresholdsOptions`'s "additive stub fields" pattern (WP-013) is the direct precedent for adding the four ranking weights.

**No new component should be invented** for any of the above.

## Phase 7 — Public Contract Review

- `EOS.Contracts` needs zero changes — `IKnowledgeClient` lives in `EOS.Knowledge`.
- `IEmbeddingProviderClient`/`Vector` (`EOS.SDK`) need no contract change themselves — the problem is which project may reference them (Gap 1), a dependency-table question, not a contract-change question.
- No contract change is proposed or required to be approved in this review.

## Phase 8 — Gap Analysis

### Gap 1 — `EOS.Knowledge` has no legal dependency path to embedding generation (CRITICAL)

Part 1 §1.2's `EOS.Knowledge` row grants no path to `EOS.SDK`/`EOS.AIProvider`, yet the same table's `EOS.AIProvider` row presupposes `EOS.Knowledge` is a direct "embed" consumer. Root cause: an internal inconsistency in Part 1 §1.2. Blocks §13's vector stage. Severity: Critical. Recommendation (not decided here): either a documented Constitution-table clarification, or the established "own plain type, composition-root translates" pattern (WP-004/010/011/012/013).

### Gap 2 — `EOS.VectorStore` and Redis data-access are unimplemented scaffolds (HIGH)

Real, substantial, in-scope engineering effort confirmed to start from zero for two of three physical stores. Severity: High (effort/estimation risk).

### Gap 3 — `MemoryType` vs. `KnowledgeNodeType` reconciliation is undefined (HIGH)

`query(MemoryType type?, ...)` needs a mapping the specification never explicitly tabulates, and never states whether `BestPractice`/`Principle` (mentioned in §10.5's prose) exist as real `NodeType` values (they do not). Severity: High, blocking for `query()`'s Semantic/Long-term/Episodic cases.

### Gap 4 — Ranking-weight configuration ownership is unassigned (LOW)

Purely additive; matches the WP-013 stub-field precedent or could live in `KnowledgeOptions`. Severity: Low.

### Gap 5 — Specification document status is "Proposed," not "Approved" (MEDIUM, disclosure-only)

Governance fact, no technical impact to WP-014's content directly.

## Phase 9 — Architecture Impact Report

- **Dependencies:** Potentially one new-or-clarified dependency edge (Gap 1) — the single highest-impact item.
- **Layering:** No new project; `EOS.VectorStore` finally becomes a real, non-empty layer.
- **Composition root:** `Program.cs`/`AskCommand` construction grows additively.
- **Configuration:** Additive only.
- **Testing:** Real infrastructure discipline extends naturally to Redis/ChromaDB.
- **Performance:** §27's targets untested until implemented.
- **Maintainability:** Gap 3's reconciliation is the item most likely to create future maintenance debt if resolved implicitly.
- **Future work packages:** WP-015 directly builds on whatever retrieval/ranking shape WP-014 produces.

## Phase 10 — File Classification

**Expected to change:** `IKnowledgeClient.cs`, `KnowledgeClient.cs`, `KnowledgeGraphStore.cs`, `ThresholdsOptions.cs`/`KnowledgeOptions.cs`, `config/Thresholds.json`/`Knowledge.json`, `Program.cs`.
**Expected to be created:** `EOS.VectorStore/*.cs`, a Redis data-access wrapper, new test files, `docs/WP-014-Implementation-Plan.md`.
**Forbidden to touch:** all Constitution/roadmap/spec documents, `EOS.Contracts`, `EOS.Learning`, `EOS.Gates`, `EOS.Reasoning`, `EOS.Orchestrator`, `IKnowledgeClient.cs`'s existing `UpdateAsync` signature.

## Phase 11 — Risks

Architecture risk (Gap 1 resolved incorrectly could create undocumented dependency-table drift); operational risk (first real ChromaDB data-operation use); testing risk (sequential-run time growth); performance risk (no current benchmark); future coupling risk (Gap 3's resolution becomes load-bearing for WP-015/017/018); WP boundary violation risk (scope creep into `assemble_context()`/`consolidate()`).

## Phase 12 — Final Recommendation

**GO WITH DECISIONS REQUIRED.** Gaps 1 and 3 require explicit resolution with exact citations before an Implementation Plan can be written, matching WP-012/013's precedent. Gap 4 trivial, resolvable inline. Gaps 2 and 5 are disclosures, not blockers.
