# WP-016 Implementation Plan — Memory: Compression & Expiration Lifecycle

Status: Awaiting approval. No implementation has been performed under this plan.

## Scope

Implement Memory Compression and Expiration (roadmap WP-016), completing the full memory lifecycle:

- Compression eligibility check and summarization call (summarization stubbed until WP-020, per the roadmap's own explicit instruction).
- Per-memory-type expiration rules.
- Archival to the Artifact Registry pattern.

Explicitly excluded (per roadmap row): the actual `summarize()` implementation (WP-020, this WP only calls it); Knowledge Management's Archiving lifecycle-state tagging (WP-018).

## Constitution References

- §0.1.1.1 (Evidence over assertion) — original content must be archived, never deleted, before replacement.
- §0.1.1.5 (No data duplication) — archived content must not become a second live copy.
- §0.12.1 (Execution Cycles) — Compression's sweep cadence is Sprint-cycle.
- Part 1 §1.2 (dependency rules) — governs which project references are legal for any new component.
- Part 8 (Artifact Registry) — §8.2 Artifact Types, §8.3 Versioning Rule (immutable, hash/id-addressed, "a 'change' creates a new version... never an in-place edit").

## Specification References

- `Memory-Management-Specification-v1.0.md` §17 (Memory Compression: §17.1 Compression Policy, §17.2 Compression Algorithm) — full text required and already read.
- `Memory-Management-Specification-v1.0.md` §18 (Memory Expiration, per-memory-type table) — full text required and already read.
- `Memory-Management-Specification-v1.0.md` §26 (Security Considerations) — governs the legal/compliance retention-hold sub-criterion.
- `Learning-Engine-Specification-v1.1.md` §7 (Ownership), §9 (Domain Model / `PipelineRecord`) — referenced only to confirm `PipelineRecord`'s ownership and non-existence before WP-026; no other section of this specification is in scope.

## Existing Components Affected

- `EOS.Knowledge/KnowledgeClient.cs`, `IKnowledgeClient.cs` — read for context only; not modified. Compression/Expiration are additive capabilities, not changes to the existing `IKnowledgeClient` contract.
- `EOS.KnowledgeGraph/KnowledgeGraphStore.cs` — requires one additive method to update a node's `Content` without touching its other fields (§17.2's `replace_content`). No existing method's signature or behavior changes.
- `EOS.Infrastructure/RedisMemoryStore.cs` — already supports a `timeToLive` parameter (real since WP-014); Expiration consumes this as-is, no change required.
- `EOS.SharedKernel/Configuration/ThresholdsOptions.cs` / `config/Thresholds.json` — require two additive fields for Short-term/Session TTL configuration, per §18's explicit "idle-timeout policy (`Thresholds.json`)" instruction.
- `EOS.Runner/Program.cs` — requires additive wiring only (new adapters, new construction calls); the existing `"ask"` command path must remain behaviorally unchanged.

## Dependencies

- WP-015 (roadmap-declared prerequisite) — already complete; `KnowledgeClient`, `IMemorySourceStore`, and the Composition Root Adapter Pattern (ADR-015-001) it established are relied upon as precedent.
- WP-020 (forward dependency, not yet built) — owns the real `summarize()`. Per the roadmap's own instruction, WP-016 stubs this call rather than waiting.
- WP-026 (forward dependency, not yet built) — owns `PipelineRecord`, which §17.1's eligibility rule references. Not listed as a roadmap prerequisite of WP-016, but required by the specification's literal eligibility criterion; addressed as a Required Stub below, consistent with how the roadmap itself already treats the WP-020 dependency.

## Risks

- Building a new adapter for a not-yet-existing subsystem (`PipelineRecord`/WP-026) could be read as scope creep if not tightly bounded to a stub with no real logic. Mitigation: the stub contains exactly one method, returns a constant, and is documented as the architecturally-correct value given no `PipelineRecord` exists.
- A compression eligibility check that is insufficiently scoped (e.g., an unconditional "always eligible" test double) risks mutating unrelated data in the shared, persistent development database used by integration tests. Mitigation: any test double used for eligibility must be scoped to a specific test-created entry, never an unconditional true.
- Adding fields to `ThresholdsOptions` (a `required`-field record) risks breaking any other `Thresholds.json` file in the repository that doesn't declare them. Mitigation: confirm only one `Thresholds.json` exists before making the fields required.

## Assumptions

- "Archival to the Artifact Registry pattern" (roadmap's own wording, not "the Artifact Registry" itself) means implementing the pattern's defining property — immutable, insert-only, id-addressed storage of the pre-compression original — using already-existing SQL Server infrastructure (WP-004), not the full Constitution Part 8 service spanning all sixteen artifact types named in §8.2. That broader service has no owning WP anywhere in the roadmap and is out of scope for this WP regardless.
- The "not read in last N Sprint cycles" and "no legal/compliance retention hold" eligibility sub-criteria (§17.1) have no data source anywhere in this codebase (no read-tracking mechanism, no retention-hold flag on any existing type) — treated as permissive/always-satisfied, consistent with the precedent `RetrievalRanking.cs` already set for its own untracked `AccessFrequency` term (WP-014).
- Working Memory requires no Expiration code, since §18's own table states it is "never persisted."

## Required Stubs

- `IPipelineStageStore` (new interface, `EOS.Knowledge`) — Composition Root Adapter Pattern (ADR-015-001), standing in for Learning-Engine-owned `PipelineRecord` (WP-026). Concrete stub adapter in `Program.cs` always reports "not yet reached Pattern stage."
- `ISummarizer` (new interface, `EOS.Knowledge`) — Composition Root Adapter Pattern, standing in for `EOS.Reasoning`'s `summarize()` (WP-020). Concrete stub adapter in `Program.cs` truncates rather than summarizes, never claiming real summarization — this exact stub is explicitly authorized by the roadmap's own WP-016 row.

## Integration Points

- `EOS.Runner/Program.cs` — composition root; wires both stub adapters, the new archival store, and the compression sweep into the existing process, additively.
- `EventMediator` (`EOS.Orchestrator`, already real since WP-015) — `MemoryCompressed` event publication follows the same publisher-interface pattern as `LessonLearned`/`MemoryConsolidated`/`ContextAssembled`.
- `RedisMemoryStore` (`EOS.Infrastructure`, already real since WP-014) — consumed as-is for Expiration; no change to that class.

## Database Impact

- One new table in the existing SQL Server database (via `EOS.KnowledgeGraph`, same connection string already established by WP-004): an insert-only archival table keyed by a generated archive id, storing the source node id, original content, and archive timestamp. No existing table's schema changes.
- No new Redis keyspace convention beyond what `RedisMemoryStore.SetAsync`'s existing `timeToLive` parameter already supports.

## Public Contract Impact

None. `IKnowledgeClient` is unchanged. All new interfaces (`IPipelineStageStore`, `ISummarizer`, `IMemoryCompressedEventPublisher`) are net-new, additive surface area, not modifications to any already-published contract.

## Testing Strategy

- Unit tests for eligibility rules (Compression sweep correctly compresses an entry whose stub-reported stage is "reached," and correctly skips one whose stub-reported stage is "not reached"), run against the real, live SQL Server instance (matching this codebase's established no-mocking convention), with test doubles scoped to a specific test-created node id to avoid mutating unrelated shared data.
- A round-trip test proving archived original content is retrievable after compression (never silently deleted).
- Unit tests for the per-memory-type expiration policy computation (pure logic, no I/O).
- An integration test against the real, live Redis instance proving a Short-term/Session key written with the computed TTL expires unattended within a bounded polling window — the literal binding Test Verification requirement ("expires on schedule without manual intervention").

## Acceptance Criteria

Matching the roadmap row's own binding fields:

- Expected deliverable: "A Sprint-cycle-boundary sweep that correctly identifies and compresses eligible Episodic entries, and expires ephemeral memory types on schedule" — satisfied by real, callable, tested code (invocation cadence itself is out of scope, per Risks/Assumptions above; no scheduler exists anywhere in this codebase for any WP yet).
- Test verification: unit tests for eligibility rules; an integration test confirming Working/Short-term Memory expires on schedule without manual intervention.
- Demo/acceptance criteria: a test entry marked eligible for compression is correctly compressed with its original content archived, not deleted.

## Explicit List of Files Expected to Change

**Created:**
- `src/EOS.Knowledge/IPipelineStageStore.cs`
- `src/EOS.Knowledge/ISummarizer.cs`
- `src/EOS.Knowledge/IMemoryCompressedEventPublisher.cs`
- `src/EOS.Knowledge/CompressionSweep.cs`
- `src/EOS.Knowledge/MemoryExpirationPolicy.cs`
- `src/EOS.KnowledgeGraph/ArchivedContentStore.cs`
- `tests/EOS.Knowledge.Tests/CompressionSweepTests.cs`
- `tests/EOS.Knowledge.Tests/MemoryExpirationPolicyTests.cs`
- `tests/EOS.Knowledge.Tests/ArchivedContentStoreTests.cs`
- `tests/EOS.Runner.Tests/MemoryExpirationIntegrationTests.cs`

**Modified (additive only):**
- `src/EOS.KnowledgeGraph/KnowledgeGraphStore.cs`
- `src/EOS.SharedKernel/Configuration/ThresholdsOptions.cs`
- `config/Thresholds.json`
- `src/EOS.Runner/Program.cs`

**Not modified:** `EOS.Contracts`, `EOS.Reasoning`, `EOS.Gates`, `EOS.AIProvider`, `EOS.SDK`, `EOS.Orchestrator`, `EOS.VectorStore`, `KnowledgeNode.cs`, `KnowledgeNodeType.cs`, `KnowledgeClient.cs`, `IKnowledgeClient.cs`. No `.csproj` file changes.

---

This plan is submitted for approval. No implementation will proceed until explicitly approved.
