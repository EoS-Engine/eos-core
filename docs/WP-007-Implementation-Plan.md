# WP-007 Implementation Plan — Memory Layer: Minimal Storage & Write Path

**Revision:** 1 (Final, Approved)
**Source of Truth (priority order):** `docs/Development-Workflow.md`, `docs/EOS-Implementation-Roadmap-v1.0.md` (WP-007 row), `docs/Memory-Management-Specification-v1.0.md`, `docs/Knowledge-Management-Specification-v1.0.md` §10.9 (as interpreted by the approved Gap Analysis only), `docs/EOS-Specification.md`, the approved WP-007 Architecture Gap Analysis, existing implemented architecture WP-001–WP-006.

## Objective (roadmap, verbatim)

Implement the real `KnowledgeNode` schema and a single write path (`update()`/`consolidate()`-equivalent) into SQL Server — not the full seven-memory-type lifecycle yet.

## Final Architecture Decisions (carried from the approved Gap Analysis, not reopened)

1. Roadmap's "§9/§10.9" citations read as a minimal, structural analog of `Knowledge-Management-Specification-v1.0.md` §10.9 only — none of WP-017/018's `knowledge_metadata` fields are implemented here.
2. `KnowledgeNode` schema: `NodeId (Guid)`, `NodeType (enum: Fact/Lesson/Pattern/Decision/Risk)`, `Content (string)`, `DomainTags (string[])`, `EvidenceRefs (string[])`, `CreatedAt (DateTimeOffset)`.
3. `IKnowledgeClient`'s write method has upsert semantics (create-if-absent, else update); emits no event this WP.
4. `KnowledgeGraphRef` = `Guid`, identical to `NodeId`.
5. No `EOS.Runner`/Bootstrap change.
6. Interface placement: `EOS.Knowledge` itself — Constitution Part 1 §1.2 lists `EOS.Knowledge` directly in `EOS.Planner`'s/`EOS.PrincipalEngineer`'s/`EOS.ProductOwner`'s/`EOS.CTO`'s own `Depends On` columns.
7. Storage layer: `EOS.KnowledgeGraph`, mirroring `SqlEventStore`'s exact pattern (raw `Microsoft.Data.SqlClient`, no ORM).
8. No event emission this WP — `EOS.Knowledge`'s Constitution-declared dependency shape (`EOS.KnowledgeGraph`, `EOS.VectorStore` only) does not include `EOS.Contracts`/`EOS.Orchestrator`.

## Included Scope (roadmap, verbatim)

The `KnowledgeNode` table schema in SQL Server; a minimal `IKnowledgeClient` exposing only `update()` (write) for this milestone; Episodic-Memory-equivalent classification for the vertical slice's interaction record.

## Explicitly Excluded Scope (roadmap, verbatim)

The full seven memory-type lifecycle and Storage Strategy (WP-014); Retrieval Strategy and mechanical ranking (WP-014); Context Assembly (WP-015); Consolidation/Compression/Expiration (WP-015/WP-016); `EOS.VectorStore`/ChromaDB integration (WP-014).

## Vertical Slice Definition

A caller invokes `IKnowledgeClient.UpdateAsync()` (`EOS.Knowledge`) → `KnowledgeClient` wraps `KnowledgeGraphStore.UpsertAsync()` (`EOS.KnowledgeGraph`) → a real `KnowledgeNode` row is persisted into SQL Server → the row is independently confirmed via `KnowledgeGraphStore.GetByIdAsync()`, directly queryable via SQL.

## Projects Affected

`EOS.KnowledgeGraph`, `EOS.Knowledge`.

## Files to Create

- `src/EOS.KnowledgeGraph/KnowledgeNodeType.cs`, `KnowledgeNode.cs`, `KnowledgeGraphStore.cs`
- `src/EOS.Knowledge/IKnowledgeClient.cs`, `KnowledgeClient.cs`
- `tests/EOS.Knowledge.Tests/EOS.Knowledge.Tests.csproj`, `KnowledgeNodeTests.cs`, `KnowledgeGraphStoreTests.cs`, `KnowledgeClientTests.cs`
- `docs/work-packages/WP-007-Completion-Report.md` (at closure)

## Files to Modify

- `src/EOS.KnowledgeGraph/EOS.KnowledgeGraph.csproj` — add `Microsoft.Data.SqlClient` 7.0.2 `PackageReference`.
- `EOS.slnx` — register `tests/EOS.Knowledge.Tests`.

## Files That Must NOT Change

`src/EOS.Runner/**`, `src/EOS.VectorStore/**`, `src/EOS.Reasoning/**`, `src/EOS.Learning/**`, `src/EOS.Planner/**`, `src/EOS.Contracts/**`, `src/EOS.SDK/**`, `src/EOS.Infrastructure/**`, `config/*.json`, `src/EOS.SharedKernel/Configuration/**`, any specification/roadmap/Constitution document.

## Dependency Changes

`EOS.KnowledgeGraph → Microsoft.Data.SqlClient` (new `PackageReference`, no new `ProjectReference`). No other project gains any new reference.

## Package Changes

`Microsoft.Data.SqlClient` 7.0.2 added to `EOS.KnowledgeGraph.csproj` only.

## Database Schema

| Field | Type |
|---|---|
| `NodeId` | `Guid` |
| `NodeType` | `KnowledgeNodeType` enum |
| `Content` | `string` |
| `DomainTags` | `string[]` (JSON column) |
| `EvidenceRefs` | `string[]` (JSON column) |
| `CreatedAt` | `DateTimeOffset` (never rewritten on update) |

## SQL Table Design

```sql
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'KnowledgeNode')
CREATE TABLE KnowledgeNode (
    NodeId UNIQUEIDENTIFIER PRIMARY KEY,
    NodeType NVARCHAR(50) NOT NULL,
    Content NVARCHAR(MAX) NOT NULL,
    DomainTagsJson NVARCHAR(MAX) NOT NULL,
    EvidenceRefsJson NVARCHAR(MAX) NOT NULL,
    CreatedAt DATETIMEOFFSET NOT NULL
)
```

Upsert: `IF EXISTS ... UPDATE ... ELSE ... INSERT ...` — `CreatedAt` set only on insert, never on update.

## Public Contracts

```csharp
public enum KnowledgeNodeType { Fact, Lesson, Pattern, Decision, Risk }

public sealed record KnowledgeNode(
    Guid NodeId, KnowledgeNodeType NodeType, string Content,
    string[] DomainTags, string[] EvidenceRefs, DateTimeOffset CreatedAt);

public sealed class KnowledgeGraphStore(string connectionString)
{
    public Task EnsureTableExistsAsync(CancellationToken cancellationToken);
    public Task UpsertAsync(KnowledgeNode node, CancellationToken cancellationToken);
    public Task<KnowledgeNode?> GetByIdAsync(Guid nodeId, CancellationToken cancellationToken);
}

public interface IKnowledgeClient
{
    Task UpdateAsync(Guid nodeId, KnowledgeNodeType nodeType, string content,
        string[] domainTags, string[] evidenceRefs, CancellationToken cancellationToken = default);
}

public sealed class KnowledgeClient(KnowledgeGraphStore store) : IKnowledgeClient;
```

## Test Strategy

Unit (no DB): `KnowledgeNode` schema/JSON round-trip, `KnowledgeNodeType` enum coverage.
Integration (real SQL Server, no mocks): `EnsureTableExistsAsync` idempotency, upsert insert, upsert update, `CreatedAt` immutability, `GetByIdAsync` existing/missing, full round-trip; `KnowledgeClient.UpdateAsync()` persists correctly, verified through `KnowledgeGraphStore`.

## Acceptance Criteria (roadmap, verbatim)

The vertical slice's interaction is queryable as a real row in SQL Server after a demo run.

## Definition of Done

Per `docs/Development-Workflow.md` §14 in full.

## Implementation Sequence

1. Feature branch `wp-007-memory-minimal-write-path` (created).
2. This plan document.
3. `EOS.KnowledgeGraph` types + store; add `Microsoft.Data.SqlClient`.
4. `EOS.Knowledge` interface + client.
5. `EOS.Knowledge.Tests`.
6. Register test project in `EOS.slnx`.
7. Full Local Verification.
8. Architecture Gate self-review.
9. Stop for approval before PR/merge/tag/closure.
