# WP-014 — Architecture Challenge: Decision Resolution (G1 & G3)

> **Historical record.** This document reproduces, verbatim, the Architecture Challenge as originally delivered and approved, resolving Gaps 1 and 3 from `Architecture-Review.md`. It is preserved unmodified per governance requirement.
>
> **Supersession notice (factual, added at persistence time, body text below unchanged):** G1's conclusion below (the Composition Root Adapter Pattern for embedding access) was authorized for implementation, built, and then **discovered during implementation to have no specification-sanctioned production caller** — neither `query()` nor `query_similar()`'s ratified signatures ever supply content to embed. It was subsequently removed by explicit direction. The governing resolution for G1 is `Final-Implementation-Plan.md` and `docs/Architecture-Gaps/AG-0001-WP014-Hybrid-Retrieval-Inconsistency.md`, not this document. G3's conclusion below stands unchanged and is exactly what was implemented.

---

## GAP 1 — `EOS.Knowledge`'s lack of a legal dependency path to embedding generation

### 1. Exact Evidence

`EOS-Specification.md` Part 1 §1.2, Project Ownership table:

> `| EOS.Knowledge | Principal Engineer | EOS.KnowledgeGraph, EOS.VectorStore | Role projects |`

Same table, `EOS.AIProvider` row, "Never Depends On" column:

> `A third consumer channel beyond EOS.Reasoning (`infer`) and EOS.Knowledge (`embed`)`

Same table, `EOS.Learning` row (for contrast):

> `| EOS.Learning | Principal Engineer | EOS.Contracts, EOS.Knowledge, EOS.SDK | Role projects (no role project depends on it directly) |`

Current repository state (verified, not memory): `src/EOS.Knowledge/EOS.Knowledge.csproj` references only `EOS.KnowledgeGraph` and `EOS.VectorStore`. `IEmbeddingProviderClient`/`Vector` live in `EOS.SDK`. `src/EOS.Learning/EOS.Learning.csproj` (empty scaffold) already carries `ProjectReference`s to `EOS.Contracts`, `EOS.Knowledge`, **and** `EOS.SDK` — an exact, faithful transcription of its own Part 1 §1.2 row.

### 2. Real or Apparent?

**Real.** The `EOS.AIProvider` row's own text names `EOS.Knowledge` as one of exactly two legitimate consumer channels of an AI-Provider-exposed capability. The `EOS.Knowledge` row's "Depends On" column does not grant any path to `EOS.SDK` or `EOS.AIProvider`.

### 3. Root Cause and Blocking Determination

**Root cause:** An internal inconsistency in Part 1 §1.2 — one row's prose presupposes an edge that a different row in the same table does not grant. Compare to `EOS.Learning`'s row, which does list `EOS.SDK` alongside `EOS.Knowledge`.

**Blocking:** Yes, for WP-014 specifically, as scoped at the time of this Challenge.

### 4. Existing Architectural Precedent

**(a) Static-value translation at composition time** — WP-012 (`EOS.Gates.PolicyEntry`) and WP-013 (`ResourceCeilings`).

**(b) Runtime call-adapter at composition time** — WP-010/011 (`IProviderEventLogger`). Defined inside `EOS.AIProvider` itself using only BCL types; `Program.cs` supplies the concrete implementation, `LoggerProviderEventLogger(ILogger logger)`, bridging the call at runtime.

Gap 1 was assessed at this stage as case (b): embedding a query is a per-request runtime call with request-specific input, matching how `IProviderEventLogger.LogEvent(message)` is called once per event.

### 5. Candidate Solutions (as considered at this stage)

**Solution A** — Amend Part 1 §1.2's `EOS.Knowledge` row to add `EOS.SDK`.

**Solution B** — Adapter interface owned by `EOS.Knowledge`, implemented by `Program.cs` (the `IProviderEventLogger` pattern).

**Solution C** — Route embedding through `EOS.Reasoning` instead of `EOS.Knowledge`.

### 6. Why the Rejected Solutions Were Rejected (as reasoned at this stage)

Solution A required an out-of-band Constitution edit outside this challenge's standing. Solution C was rejected on direct specification evidence: Memory-Management-Specification §4 assigns "invoking (not owning) embedding generation" to Memory itself, not Reasoning.

### 7. Smallest Compliant Architecture (as concluded at this stage)

**Solution B** was adopted at this stage: zero new `ProjectReference`, zero Constitution edit, zero new project — one small BCL-typed interface inside `EOS.Knowledge`, one small adapter class inside `Program.cs`.

*(See supersession notice above: this conclusion was later found, during implementation, to have no method in WP-014's actual scope that could legitimately call it, and was removed.)*

### 8–10. Hidden Dependency Analysis / Constitution Compliance / Roadmap Compliance / Final Recommendation (as concluded at this stage)

Solution B introduced no hidden dependency, required no Constitution edit, and was recorded as the final recommendation at this stage of the process. This recommendation did not survive contact with implementation — see the supersession notice above.

---

## GAP 3 — `MemoryType` (§10) vs. `KnowledgeNodeType` reconciliation

### 1. Exact Evidence

Memory-Management-Specification §20.1: `IEnumerable<KnowledgeNode> query(MemoryType type?, string[] domain_tags?, DateRange range?)`.

§10.3 (Long-term): "The permanent content of the Knowledge Graph itself — Facts, ratified Patterns/Best Practices/Principles (post Learning Engine promotion), Decisions, Risks."

§10.4 (Episodic): "Maps directly onto the Knowledge Graph's `Lesson` node type (§0.5.1) *before* Learning Engine promotion — Episodic Memory is where a Lesson lives the moment it's created."

§10.5 (Semantic): "Generalized, timeless engineering knowledge — Facts, and any Pattern/Best Practice/Principle that the Learning Engine has promoted... Explicitly excludes raw, unpromoted Lessons (those are Episodic, §10.4)."

§22: "Long-term Memory — permanent superset containing Semantic Memory; governed identically to §10.3/§10.5."

Current repository state: `src/EOS.KnowledgeGraph/KnowledgeNodeType.cs` — `enum KnowledgeNodeType { Fact, Lesson, Pattern, Decision, Risk }`.

`Learning-Engine-Specification-v1.1.md`: "`PipelineRecord.stage` is *not* a Learning-Engine-invented vocabulary — its values are exactly the Constitution Part 14 stage names (`Lesson`, `Pattern`, `BestPractice`, `Principle`, `GoldenPath`, `Automation`, `ReusableComponent`, `PlatformCapability`)... Pipeline stage-transition logic | `EOS.Learning` (StageEngine) | `EOS.Knowledge`, `EOS.KnowledgeGraph`" / "Pipeline metadata storage (`PipelineRecord`/`TransitionRecord`) | `EOS.Learning` | `EOS.Knowledge`" — i.e., `PipelineRecord` is stored and owned exclusively by `EOS.Learning`, never by `EOS.KnowledgeGraph`.

### 2. Real or Apparent?

**Real, but narrower than the Architecture Review stated.** `BestPractice`/`Principle`/`GoldenPath`/etc. are `PipelineRecord.stage` values owned entirely by `EOS.Learning`, referencing a `KnowledgeGraph` node only via `knowledge_graph_ref`. They are not, and cannot be, `KnowledgeNodeType` values. `KnowledgeNodeType` never needed a sixth/seventh/eighth member.

### 3. Root Cause and Blocking Determination

**Root cause:** §10.5's prose reads as if `BestPractice`/`Principle` were retrievable `NodeType`-like values, when they are Learning-Engine-internal stage labels for content that remains typed `Fact` or `Pattern` throughout, as far as `EOS.KnowledgeGraph`/`EOS.Knowledge` can ever observe.

**Blocking:** No — resolves to exactly one answer derivable from already-approved, already-frozen specification text.

### 4. Existing Architectural Precedent

None needed as a *pattern* — resolved by direct textual evidence, using WP-007's own "resolve from cross-document evidence rather than invent" method.

### 5. Candidate Solutions

Only one construction is defensible:

| `MemoryType` | Concrete filter |
|---|---|
| `Working` | Redis, current micro-cycle scope; no `NodeType` |
| `ShortTerm` | Redis, `task_id`-scoped; no `NodeType` |
| `Session` | Redis, session-scoped; no `NodeType` |
| `Episodic` | `KnowledgeGraph` filter: `NodeType == Lesson` |
| `Semantic` | `KnowledgeGraph` filter: `NodeType in { Fact, Pattern }` |
| `LongTerm` | `KnowledgeGraph` filter: `NodeType in { Fact, Pattern, Decision, Risk }` |
| `Project` | Not a memory type filter — a `domain_tags`-based post-filter over any of the above |

### 6. Why the Rejected Solutions Are Rejected

An alternative giving `BestPractice`/`Principle` their own `KnowledgeNodeType` values would contradict Learning-Engine-Specification-v1.1's explicit ownership statement. An alternative including `Lesson` in `LongTerm` would contradict §10.3's itemized list and §10.4's Episodic-exclusivity statement.

### 7. Smallest Compliant Architecture

One new enum (`MemoryType`, 7 members) inside `EOS.Knowledge`, plus a mechanical mapping function to a `NodeType` filter set (or a Redis-branch for the three ephemeral types). No `KnowledgeNodeType` extension, no schema change.

### 8. Hidden Dependency Analysis

None — entirely internal to `EOS.Knowledge`/`EOS.KnowledgeGraph`.

### 9. Constitution Compliance

Fully compliant — `KnowledgeNodeType` remains exactly the five §0.5.1 values.

### 10. Final Recommendation

**Adopt the mapping table above.** This conclusion was implemented exactly as stated and remains the governing resolution for G3.
