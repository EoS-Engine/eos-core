# WP-007 Completion Report — Memory Layer: Minimal Storage & Write Path

# Summary

Implemented the real `KnowledgeNode` schema and a single write path (`update()`-equivalent, upsert semantics) into SQL Server, per `docs/EOS-Implementation-Roadmap-v1.0.md`'s WP-007 row and `docs/Memory-Management-Specification-v1.0.md` §16.2/§20.1. Because the ratified specification left `KnowledgeNode`'s field schema, `IKnowledgeClient.update()`'s parameter list, and `KnowledgeGraphRef`'s type genuinely undefined (confirmed via a dedicated Architecture Gap Analysis), this WP's design decisions were resolved from cross-document evidence and existing implemented architecture (WP-001–WP-006) rather than invented — documented in full in `docs/WP-007-Implementation-Plan.md`.

# Vertical Slice Delivered

`IKnowledgeClient.UpdateAsync()` (`EOS.Knowledge`) → `KnowledgeClient` wraps `KnowledgeGraphStore.UpsertAsync()` (`EOS.KnowledgeGraph`) → a real `KnowledgeNode` row is persisted into SQL Server, independently confirmed via `KnowledgeGraphStore.GetByIdAsync()` — directly satisfying the roadmap's acceptance criterion: *"The vertical slice's interaction is queryable as a real row in SQL Server after a demo run."* Verified by 15 passing tests against the real, running SQL Server instance (WP-004 infrastructure), zero mocks.

# Architecture Decisions

Resolved via a dedicated, user-approved Architecture Gap Analysis before implementation began (five original blockers plus two implementation-level decisions, all recorded in full in `docs/WP-007-Implementation-Plan.md`):

1. **Contract placement:** `IKnowledgeClient` in `EOS.Knowledge` itself — Constitution Part 1 §1.2 lists `EOS.Knowledge` directly in `EOS.Planner`'s/`EOS.PrincipalEngineer`'s/`EOS.ProductOwner`'s/`EOS.CTO`'s own `Depends On` columns.
2. **`KnowledgeNode` schema:** `NodeId (Guid)`, `NodeType (enum: Fact/Lesson/Pattern/Decision/Risk)`, `Content (string)`, `DomainTags (string[])`, `EvidenceRefs (string[])`, `CreatedAt (DateTimeOffset)` — every field traced to specific ratified pseudocode (`consolidate()`'s `create_node()` call) or unanimous prior-WP `Guid`/`DateTimeOffset` precedent.
3. **`update()` has upsert semantics** (create-if-absent, else update); emits no event — `update()`'s own ratified signature was literally incomplete (`void update(KnowledgeGraphRef ref, ...)`), and `EOS.Knowledge`'s Constitution-declared dependency shape (`EOS.KnowledgeGraph`, `EOS.VectorStore` only) excludes `EOS.Contracts`/`EOS.Orchestrator`, making real event emission architecturally unreachable this WP.
4. **`KnowledgeGraphRef` = `Guid`** — no specification ever commits it to a concrete type; four-for-four unanimous prior-WP precedent (`EventEnvelope.EventId`, `StoredEvent.EventId`, `InferenceRequest.RequestId`, `ActionRequest.ActionId`).
5. **No `EOS.Runner`/Bootstrap change** — matches WP-005/WP-006's identical, successfully-closed precedent.
6. **Storage layer in `EOS.KnowledgeGraph`**, mirroring `SqlEventStore`'s exact pattern (raw `Microsoft.Data.SqlClient`, no ORM).
7. Roadmap's "§9/§10.9" citations, verified against the actual specification text, correspond to `Knowledge-Management-Specification-v1.0.md` §10.9 (a later, WP-017/018-owned document) — read as instructing a minimal structural analog only, never importing that document's full `knowledge_metadata` fields.

# Files Created

- `src/EOS.KnowledgeGraph/KnowledgeNodeType.cs`, `KnowledgeNode.cs`, `KnowledgeGraphStore.cs`
- `src/EOS.Knowledge/IKnowledgeClient.cs`, `KnowledgeClient.cs`
- `tests/EOS.Knowledge.Tests/` (`EOS.Knowledge.Tests.csproj`, `KnowledgeNodeTests.cs`, `KnowledgeGraphStoreTests.cs`, `KnowledgeClientTests.cs`)
- `docs/WP-007-Implementation-Plan.md`

# Files Modified

- `src/EOS.KnowledgeGraph/EOS.KnowledgeGraph.csproj` — added `Microsoft.Data.SqlClient` 7.0.2 `PackageReference`
- `EOS.slnx` — registered `tests/EOS.Knowledge.Tests`

No WP-001–WP-006 file touched (`EOS.Runner`, `EOS.VectorStore`, `EOS.Reasoning`, `EOS.Learning`, `EOS.Planner`, `EOS.Contracts`, `EOS.SDK`, `EOS.Infrastructure`, `config/*.json`, `EOS.SharedKernel/Configuration` all confirmed byte-identical to pre-WP-007 `main` throughout).

# Dependency Changes

`EOS.KnowledgeGraph → Microsoft.Data.SqlClient` (new `PackageReference` only — no new `ProjectReference` anywhere). `EOS.Knowledge`'s and `EOS.KnowledgeGraph`'s existing `ProjectReference`s (already Constitution-correct since WP-001) are unchanged.

# Package Changes

`Microsoft.Data.SqlClient` 7.0.2 added to `EOS.KnowledgeGraph.csproj` only, matching the version already used by `EOS.Infrastructure`'s `SqlEventStore`.

# Build Results

```
dotnet restore EOS.slnx → succeeded, no errors
dotnet build EOS.slnx   → Build succeeded. 0 Warning(s), 0 Error(s)
```

# Test Results

66 total, all passing, confirmed stable:
- `EOS.ArchitectureTests`: 3/3 (unchanged, WP-005/006 unaffected)
- `EOS.Knowledge.Tests`: 15/15 (new) — schema/enum/JSON round-trip unit tests; `EnsureTableExistsAsync` idempotency, upsert insert, upsert update, `CreatedAt` immutability, `GetByIdAsync` existing/missing, full round-trip (integration); `KnowledgeClient.UpdateAsync()` persistence verified through `KnowledgeGraphStore` directly (integration) — zero mocks
- `EOS.Gates.Tests`: 13/13 (unchanged, WP-006 unaffected)
- `EOS.Infrastructure.Tests`: 14/14 (unchanged, WP-004 unaffected)
- `EOS.AIProvider.Tests`: 7/7 (unchanged, WP-005 unaffected)
- `EOS.Orchestrator.Tests`: 5/5 (unchanged, WP-003 unaffected)
- `EOS.Runner.Tests`: 9/9 (unchanged, WP-002/004 unaffected)

# Format Results

`dotnet format EOS.slnx --verify-no-changes` → exit 0. `git diff --check` → exit 0.

# CodeRabbit Summary

Real review completed on PR #4 (status `SUCCESS`), 1 actionable comment + 1 pre-merge check warning, both rejected:

| # | Finding | Severity | Classification | Justification |
|---|---|---|---|---|
| 1 | `EnsureTableExistsAsync`/`UpsertAsync` are not atomic under concurrent callers (race on `CREATE TABLE`, race on first-write-for-a-`NodeId`) | Critical | **INVALID** (rejected) | Identical unhardened pattern already exists, unaddressed, in WP-004's merged `SqlEventStore` — the approved plan required mirroring it exactly. No WP-007 acceptance criterion, spec section, or roadmap requirement addresses concurrency. No real concurrent caller exists yet (`EOS.Reasoning`, WP-008, remains an empty skeleton). The suggested fix (application locks, `UPDLOCK`/`HOLDLOCK` transactions) would introduce a concurrency-control architecture pattern found nowhere else in the codebase — explicitly forbidden by this WP's authorization ("never introduce architecture changes"). |
| 2 | Docstring Coverage pre-merge check: 0.00% vs. 80.00% threshold | Warning | **INVALID** (rejected) | Same reasoning already accepted on PR #2 (WP-005) and PR #3 (WP-006): zero docstrings is this repository's established, unbroken convention across every prior WP. |

0 VALID, 2 INVALID, 0 OUT OF SCOPE, 0 OVER-ENGINEERING. No fix commit required. Both rejections documented as a PR comment (workflow §10).

# Architecture Gate Summary

A dedicated Architecture Gap Analysis was performed before implementation (beyond the standard Architecture Gate) to resolve five genuine specification/roadmap gaps — a roadmap citation mismatch, an undefined `KnowledgeNode` schema, an incomplete `update()` signature, an undefined `KnowledgeGraphRef` type, and a Bootstrap-wiring ambiguity — each resolved from quoted evidence and existing precedent, none invented, all explicitly approved before code was written. The subsequent Local Architecture/Self-Review Gate found zero Critical/High/Medium findings. The one CodeRabbit Critical finding was evaluated and correctly rejected rather than fixed, since fixing it would itself have violated this WP's architecture-preservation mandate.

# Git Record

- **Implementation commit:** `5d0cdd1d673d6a6857c81fb6f592e7e848584ac1` — "Implement WP-007: Memory Layer - Minimal Storage & Write Path"
- **Merge commit:** `9774bb8e999a08741235c53b65d91d5f78002821` (normal merge, two parents, no squash, no rebase, no history rewrite)
- **PR:** [EoS-Engine/eos-core#4](https://github.com/EoS-Engine/eos-core/pull/4)
- **Remote:** `origin = https://github.com/EoS-Engine/eos-core.git`
- Feature branch `wp-007-memory-minimal-write-path` deleted both locally and remotely after successful merge.

# Repository Status

Local `main` == `origin/main` == merge commit `9774bb8`. Working tree clean. WP-008 not started.

# Lessons Learned

Unlike WP-001–WP-006, this WP's own governing specification (`Memory-Management-Specification-v1.0.md`) left a core data contract (`KnowledgeNode`) and its primary write method's signature genuinely incomplete in the ratified text itself (a literal `...` in `update()`'s parameter list), and the roadmap's own section citations pointed at content that does not exist under those numbers. A dedicated, evidence-based Architecture Gap Analysis — rather than either inventing the missing details or blocking indefinitely — proved to be the correct mechanism: every gap was resolvable by cross-referencing a later, independent specification (`Knowledge-Management-Specification-v1.0.md`), the same document's own algorithm pseudocode, and unanimous existing-implementation precedent, without requiring a single Constitution, specification, or roadmap text amendment.
