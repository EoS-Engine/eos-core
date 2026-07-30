# WP-015 Final Implementation Plan — Memory: Context Assembly & Consolidation

**Status:** Authoritative implementation contract. Governed by the Architecture Review, Architecture Challenge, and accepted ADRs (`docs/ADRs/ADR-015-001` through `ADR-015-004`, and `WP-015-Specification-Clarifications.md`). WP-014 is frozen and untouched by this plan.

**Source of Truth (priority order):** `docs/EOS-Specification.md`, `docs/Memory-Management-Specification-v1.0.md` (§9, §13, §15, §16, §20.1, §21, §25), `docs/Learning-Engine-Specification-v1.1.md` (§11.1, §14.3), `docs/EOS-Implementation-Roadmap-v1.0.md` (WP-015 row), `docs/WP-014/Freeze-Report.md`, `docs/WP-014/Final-Implementation-Report.md`, `docs/ADRs/ADR-015-001` through `ADR-015-004`, `docs/ADRs/WP-015-Specification-Clarifications.md`.

---

## 1. Objective (roadmap, verbatim)

"Implement `assemble_context()`'s budgeted composition logic and `consolidate()`'s ephemeral-to-persistent promotion."

## 2. Scope

- `assemble_context(ContextRequest)` — budgeted, ranked, symbolic context composition with truncation transparency (§15.1, §15.2).
- `consolidate(MemoryRef, string reason, string[] evidence_refs)` — the four consolidation triggers (§16.1) and the consolidation algorithm (§16.2): create the Episodic `KnowledgeNode`, generate and index its embedding, emit `LessonLearned` (per ADR-015-002's trigger-dependent producer rule), mark the source consolidated (idempotency).
- `MemoryRef` (ADR-015-004 shape: `MemoryType` + key) and `EpisodicEntryRef` (`Guid`, per `WP-015-Specification-Clarifications.md` Item 1).
- `EOS.VectorStore`'s first production write capability (`index(...)`), per §16.2 and `WP-015-Specification-Clarifications.md` Item 2.
- The embedding-generation adapter (ADR-015-001) and the automatic-trigger event wiring (ADR-015-003).
- `MemoryConsolidated` and `ContextAssembled` events (§21), emitted via the same Composition Root mechanism ADR-015-001/003 establish (no separate ADR needed — Memory-Management-Specification §21 assigns both to "Memory" without any Constitution-table ownership conflict, unlike `LessonLearned`).

## 3. Explicit Non-Scope

- Compression, Expiration (§17, §18 — WP-016), including `MemoryCompressed`, `WorkingMemoryDiscarded`, `SessionMemoryClosed` events (§21) — none are named in the roadmap's WP-015 "Included components," and `WorkingMemoryDiscarded`/`SessionMemoryClosed` align with §18's expiration lifecycle, not §15/§16.
- Knowledge Management's additive ranking pass (WP-018).
- Any change to `IKnowledgeClient.QueryAsync`/`.QuerySimilarAsync`/`.UpdateAsync` (WP-011/WP-014, frozen).
- Any change to `MemoryType`, `KnowledgeNodeType`, `RetrievalRanking`, `RankingWeights` (WP-014, frozen) beyond what ADR-015-004 additively requires for `MemoryRef`.
- `assemble_context()`'s vector-similarity stage: architecturally assigned to `ContextAssembler` per §9/§13/§23.2, but mechanically unbuildable this WP for the same reason established for WP-014's `query()`/`query_similar()` — `ContextRequest` (§15.1) has no field carrying free text or a query embedding. Applying WP-014's own precedent directly (not a new decision): `assemble_context()` is symbolic-only in this plan.
- Real RabbitMQ-backed Event Catalog delivery — ADR-015-003 explicitly accepts `EventMediator` (in-process) as an interim mechanism; building real cross-service delivery is out of scope.
- The roadmap's WP-015 "Projects affected" correction (add `EOS.VectorStore`) — tracked in `WP-015-Specification-Clarifications.md`, not performed by this plan.

## 4. Inputs (frozen, from WP-014)

`IKnowledgeClient.QueryAsync`/`.QuerySimilarAsync`, `MemoryType`, `DateRange`, `RankingWeights`, `RetrievalRanking.Rank`, `KnowledgeGraphStore.QueryAsync`/`UpsertAsync`/`GetByIdAsync`, `RedisMemoryStore`, `ThresholdsOptions`'s ranking-weight fields, `Program.cs`'s existing `KnowledgeClient` construction.

## 5. Outputs (roadmap, verbatim)

"A working `assemble_context()` respecting a caller-specified budget; a working `consolidate()` producing a real Episodic Memory entry and a real `LessonLearned` event."

## 6. Dependencies

| Dependency | Classification | Basis |
|---|---|---|
| WP-014 (frozen baseline) | Required | Roadmap: "Prerequisites | WP-014" |
| ADR-015-001 (embedding adapter) | Required | Governs `consolidate()`'s embedding step |
| ADR-015-002 (`LessonLearned` producer) | Required | Governs `consolidate()`'s emission behavior |
| ADR-015-003 (automatic trigger wiring) | Required | Governs two of `consolidate()`'s four triggers |
| ADR-015-004 (`MemoryRef` shape) | Required | Governs `consolidate()`'s parameter type |
| `EOS.Orchestrator.EventMediator` | Required | First real production use, per ADR-015-003 |

## 7. Components Affected

| Component | Change | Basis |
|---|---|---|
| `EOS.Knowledge` | New `AssembleContextAsync`, `ConsolidateAsync` on `IKnowledgeClient`; new `ContextRequest`, `ContextPayload`, `MemoryRef`, `EpisodicEntryRef` types; new embedding-adapter interface (ADR-015-001) | Roadmap: "Projects affected \| EOS.Knowledge" |
| `EOS.KnowledgeGraph` | Possible new `CreateNodeAsync`-equivalent method if `UpsertAsync`'s semantics don't cleanly match §16.2's `create_node` intent (decided at implementation time, not here — no evidence mandates a new method name over reusing `UpsertAsync`) | §16.2 |
| `EOS.VectorStore` | First production source file: an `index(id, embedding)` write method | §16.2; `WP-015-Specification-Clarifications.md` Item 2 |
| `EOS.Infrastructure` | None anticipated beyond what `RedisMemoryStore` (WP-014) already provides | — |
| `EOS.Orchestrator` | None — `EventMediator` reused as-is, no changes to its own code | ADR-015-003 |
| `EOS.Runner` (`Program.cs`) | Composition-root wiring: embedding adapter (ADR-015-001), `EventMediator` subscription (ADR-015-003) | ADR-015-001, ADR-015-003 |

## 8. Implementation Order (atomic tasks, each independently reviewable)

**Task 1 — `ContextRequest`, `ContextPayload` types (`EOS.Knowledge`).**
Scope: evidenced fields only, per `ADR-004`'s catalogue (superseded document, fields unchanged).
Definition of Done: both types compile; every field traces to a cited spec passage (§15.1/§20.1); no field beyond those already catalogued; `dotnet build` clean.

**Task 2 — `assemble_context()` symbolic assembly algorithm.**
Scope: §15.1's algorithm, §15.2's truncation transparency, using WP-014's existing `RetrievalRanking`/`KnowledgeGraphStore.QueryAsync`/`RedisMemoryStore` unchanged.
Definition of Done: budget cutoff and `truncated` flag behave exactly per §15.1/§15.2/§25 (empty-budget case returns `truncated=true`, never errors); unit tests passing against real Redis/SQL Server data; zero modification to any WP-014 file.

**Task 3 — `ContextAssembled` event emission.**
Scope: Composition Root emission (§21 payload: request_id, item_count, truncated) — no new ADR needed, per §3 above.
Definition of Done: event observable via `EventMediator` in an integration test; payload fields match §21 exactly; `EOS.ArchitectureTests` unaffected.

**Task 4 — `MemoryRef`, `EpisodicEntryRef` types.**
Scope: per ADR-015-004's (`MemoryType`, key) shape and Specification-Clarifications Item 1's `Guid` resolution.
Definition of Done: both types compile; `MemoryRef` carries no field beyond ADR-015-004's ratified shape; `EpisodicEntryRef` is `Guid`, matching `KnowledgeGraphRef`'s precedent exactly.

**Task 5 — `EOS.VectorStore`'s first production `index(...)` write method.**
Scope: per §16.2's `VectorStore.index(episodic_entry.id, embedding)` call.
Definition of Done: real write against the live ChromaDB instance, verified by a passing integration test; no read/query method added (out of this task's scope, per WP-014's own precedent of building only what has a caller); zero `.csproj` change.

**Task 6 — Embedding-generation adapter (ADR-015-001).**
Scope: BCL-typed interface in `EOS.Knowledge`; concrete adapter in `Program.cs` wrapping `AIProviderManager`.
Definition of Done: round-trips real content through the real embedding channel in an integration test (mirrors WP-011's `EmbeddingChannelStructuralEnforcementTests` shape); `EOS.Knowledge.csproj` has zero new `ProjectReference`; `OnlyAllowedProjectsMayReferenceAIProviderTests` stays green unmodified.

**Task 7 — `consolidate()` algorithm.**
Scope: §16.2's full algorithm — create Episodic `KnowledgeNode`, generate + index embedding (Tasks 5–6), emit `LessonLearned` per ADR-015-002's trigger-dependent rule, mark source consolidated.
Definition of Done: all four triggers produce correct behavior in tests, including the Gate-failure no-re-emit case (ADR-015-002) and the idempotent no-op on an already-consolidated source (§25); real SQL Server row + real ChromaDB index confirmed per test.

**Task 8 — `MemoryConsolidated` event emission.**
Scope: Composition Root emission (§21 payload: source_memory_type, episodic_entry_id).
Definition of Done: event observable via `EventMediator` for every trigger path in Task 7's tests, including the Gate-failure path (which still emits `MemoryConsolidated` even though it does not re-emit `LessonLearned`, per ADR-015-002's scope).

**Task 9 — Automatic-trigger wiring (ADR-015-003).**
Scope: `Program.cs` subscribes to `EventMediator` for Gate-failure-adjacent and `IncidentResolved` signals, invoking `ConsolidateAsync` on receipt.
Definition of Done: a simulated publish through `EventMediator` correctly triggers `consolidate()` in an integration test; no new dependency edge introduced for `EOS.Knowledge` or `EOS.Orchestrator`; `NoCircularProjectReferencesTests` stays green unmodified.

**Task 10 — Full local verification.**
Scope: restore/build/test/format/diff-check, run after every task above, not only at the end.
Definition of Done: `dotnet build` 0/0; full sequential regression suite (165 WP-014 tests + this WP's additions) all passing; `dotnet format --verify-no-changes` clean; `git diff --check` clean; zero `.csproj`/`.slnx` change vs. the frozen WP-014 baseline.

## 9. Increment Strategy / Vertical Slices

**Slice 1 — Context Assembly** (Tasks 1–3): independently demonstrable — a caller can request a budgeted, ranked, symbolic context payload and observe correct truncation behavior, with zero dependency on Slice 2.

**Slice 2 — Consolidation** (Tasks 4–9): independently demonstrable — a role (or automatic trigger) can consolidate ephemeral content into a real Episodic entry with a real `LessonLearned` event, with zero dependency on Slice 1 having run first.

Task 10 (verification) applies continuously across both slices, per each task's own Definition of Done above.

Both slices are real, complete, and independently testable — neither is a stub for the other.

## 10. Public APIs to Implement (`IKnowledgeClient`, additive only)

- `Task<ContextPayload> AssembleContextAsync(ContextRequest request, CancellationToken cancellationToken = default)`
- `Task<EpisodicEntryRef> ConsolidateAsync(MemoryRef source, string reason, string[] evidenceRefs, CancellationToken cancellationToken = default)`

No change to `UpdateAsync`, `QueryAsync`, `QuerySimilarAsync` (frozen, WP-011/WP-014).

## 11. Internal Types/Classes to Implement (named only, no shape/code — implementation-time detail)

- `ContextRequest`, `ContextPayload` (`EOS.Knowledge`)
- `MemoryRef`, `EpisodicEntryRef` (`EOS.Knowledge`)
- The embedding-generation adapter interface (`EOS.Knowledge`) and its `Program.cs` implementation (ADR-015-001)
- `EOS.VectorStore`'s indexing class (name decided at implementation time)

## 12. Event Flow

- `assemble_context()` → emits `ContextAssembled` (request_id, item_count, truncated) — observability only, Dashboard consumer (§21).
- `consolidate()`, explicit-role/`IncidentResolved`/session-close triggers → emits `LessonLearned` (episodic_entry_id, source) — real production emission, consumed by Learning Engine's `ClusterTrigger` (§11.1) and Knowledge (§21), per ADR-015-002.
- `consolidate()`, Gate-failure trigger → does **not** re-emit `LessonLearned` (already emitted by `EOS.Gates` per §0.8.3, unchanged) — `consolidate()` only creates the referenced Episodic entry, per ADR-015-002.
- `consolidate()` (all triggers) → emits `MemoryConsolidated` (source_memory_type, episodic_entry_id) — informational only, per §21.
- `Program.cs` subscribes to `EventMediator` for Gate-failure-adjacent and `IncidentResolved` signals and calls `ConsolidateAsync` on receipt, per ADR-015-003.

## 13. Data Flow

- `assemble_context()`: `ContextRequest` → `RedisMemoryStore`/`KnowledgeGraphStore.QueryAsync` (per `request.includes_*` flags) → `RetrievalRanking.Rank` → hard budget cutoff → `ContextPayload`.
- `consolidate()`: `MemoryRef` → resolve source content via `MemoryType` + key (Redis or ad-hoc) → `KnowledgeGraphStore` create/upsert (Episodic `KnowledgeNode`, type `Lesson`) → embedding adapter → `EOS.VectorStore.index(episodic_entry.id, embedding)` → `LessonLearned`/`MemoryConsolidated` emission → mark source consolidated.

## 14. Error Handling (§25, verbatim policy, applied to this WP's scope)

- "AI Provider unavailable during embedding... Indexing deferred and retried per Constitution Part 5 §5.3 policy; content is still written to `EOS.KnowledgeGraph` immediately... the vector index simply lags until the provider recovers."
- "Context Assembly budget exceeded before any item fits... Returns an empty `ContextPayload` with `truncated=true` rather than erroring."
- "Consolidation called on already-consolidated source... No-op with a warning log (idempotent, mirrors Learning-Engine-Specification-v1.1 FR-1's idempotency pattern)."
- "Redis unavailable (Working/Short-term/Session Memory)... degraded mode operates with reduced Working Memory... Long-term/Semantic/Episodic Memory... are unaffected."

## 15. Idempotency Rules

- `consolidate()` on an already-consolidated `MemoryRef` is a no-op with a warning log (§25; §20.1 precondition: "`source.status != already_consolidated`").
- `source_memory.mark_consolidated()` (§16.2) is the mechanism preventing double-consolidation on natural expiry.

## 16. Concurrency Considerations

- `EventMediator.Publish`/`Subscribe` (existing WP-000-era code) iterates a snapshot (`handlersForType.ToArray()`) — no new concurrency primitive required by this plan.
- No specification text addresses concurrent `consolidate()` calls against the same `MemoryRef`; the idempotency guard (§25) is the only concurrency-adjacent behavior evidenced.

## 17. Required Tests

- Unit tests for `assemble_context()`'s budget/truncation logic (§15.1/§15.2), against real Redis/SQL Server data (no mocks, per this session's established discipline).
- Unit/integration tests for `consolidate()`'s four triggers, including the Gate-failure no-re-emit behavior (ADR-015-002) and the idempotency no-op (§25).
- Tests proving `EOS.VectorStore.index(...)` performs a real write against the live ChromaDB instance.
- Tests proving the embedding adapter (ADR-015-001) round-trips real content through the real embedding channel (WP-011 precedent: `EmbeddingChannelStructuralEnforcementTests`).

## 18. Required Architecture Tests

- `NoCircularProjectReferencesTests`, `OnlyAllowedProjectsMayReferenceAIProviderTests` (existing, `EOS.ArchitectureTests`) must remain green with zero modification — proof that ADR-015-001's adapter pattern introduced no new dependency edge.

## 19. Required Integration Tests

- End-to-end: explicit-role `consolidate()` call → real Episodic `KnowledgeNode` row (SQL Server) → real embedding indexed (ChromaDB) → real `LessonLearned` observable via `EventMediator`.
- End-to-end: `Program.cs`-mediated automatic trigger (simulated Gate-failure/`IncidentResolved` publish) → `consolidate()` invoked → correct no-re-emit behavior for the Gate-failure case.

## 20. Required Regression Tests

Full sequential per-project run (all WP-014-frozen suites, 165 tests, plus this WP's additions) — zero regression permitted, matching every prior WP's closure discipline.

## 21. Acceptance Criteria (roadmap, verbatim)

"A manually-triggered consolidation produces a real Episodic Memory row and a real event visible on the event backbone (WP-003)." Plus (from WP-015's own roadmap row): "Unit tests for budget enforcement and truncation flagging."

## 22. Completion Checklist

- [ ] `assemble_context()` implemented, symbolic-only, budget/truncation correct
- [ ] `consolidate()` implemented, all four triggers, correct producer behavior per trigger (ADR-015-002)
- [ ] `MemoryRef`/`EpisodicEntryRef` implemented per ADR-015-004/Specification-Clarifications
- [ ] `EOS.VectorStore` real `index()` write path implemented and tested
- [ ] Embedding adapter (ADR-015-001) implemented and wired in `Program.cs`
- [ ] Automatic-trigger `EventMediator` wiring (ADR-015-003) implemented in `Program.cs`
- [ ] `ContextAssembled`, `MemoryConsolidated` events emitted
- [ ] All required tests (§17–20) passing
- [ ] Build clean, format clean, diff-check clean, zero regressions
- [ ] No forbidden file touched (WP-014 frozen files, Constitution, specifications, roadmap)

## 23. Risks

- `assemble_context()`'s vector stage remains architecturally unassigned to any buildable mechanism this WP (§3, disclosed) — carried forward exactly as WP-014 left the same gap for `query()`/`query_similar()`.
- ADR-015-003's `EventMediator` mechanism is unprecedented in this codebase (first real end-to-end use) — first-use risk, already disclosed in that ADR.
- `EOS.VectorStore`'s first production write path carries the same first-use risk WP-014 identified for its (removed) read path.
- No specification addresses concurrent `consolidate()` invocations against the same source — disclosed gap, not resolved here.

## 24. Traceability to Specifications and ADRs

| Plan Item | Specification | ADR |
|---|---|---|
| `assemble_context()` symbolic algorithm | §15.1, §15.2 | — |
| `assemble_context()` vector-stage non-scope | §9, §13, §23.2 (architectural assignment); §15.1 (no text field, mirrors WP-014) | — |
| `consolidate()` algorithm | §16.2 | ADR-015-001 (embedding), ADR-015-002 (producer), ADR-015-004 (`MemoryRef`) |
| Four consolidation triggers | §16.1 | ADR-015-002, ADR-015-003 |
| `EOS.VectorStore` ownership | — | `WP-015-Specification-Clarifications.md` Item 2 |
| `EpisodicEntryRef` = `Guid` | §16.2, §20.1 | `WP-015-Specification-Clarifications.md` Item 1 |
| Idempotency | §25, §20.1 precondition | — |

---

## Final Consistency Validation

- **Constitution:** No conflict — `EOS.Knowledge`'s dependency shape (Part 1 §1.2) is unchanged; ADR-015-001/003's Composition Root pattern introduces no new `ProjectReference`; §0.8.3's Gate-failure mechanism is left unchanged per ADR-015-002.
- **Specifications:** No conflict — every plan item traces to §15/§16/§20.1/§21/§25 or an accepted ADR; `assemble_context()`'s scope reduction is disclosed, not silently contradicted.
- **Accepted ADRs:** No conflict — ADR-015-001 through 004 and both Specification-Clarifications items are applied as ratified, none reopened or altered.
- **Implementation order:** Valid — Slice 1 (steps 1–3) has no dependency on Slice 2 (steps 4–9); embedding adapter (step 6) precedes `consolidate()` (step 7), which depends on it; automatic-trigger wiring (step 9) is last, depending on `consolidate()` existing.
- **Roadmap deliverables:** Both covered — "a working `assemble_context()` respecting a caller-specified budget" (Slice 1) and "a working `consolidate()` producing a real Episodic Memory entry and a real `LessonLearned` event" (Slice 2).

No code generated, no pseudocode, no interfaces, no class implementations, no `Program.cs` modification, no repository modification beyond this plan document. Implementation has not begun.
