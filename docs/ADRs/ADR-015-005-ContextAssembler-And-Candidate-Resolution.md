# ADR-015-005

## Status

Accepted

## Context

- WP-014 remains correct and frozen; nothing in it is altered by this ADR.
- `RetrievalRanking` correctly implements WP-014's assigned scope (§19.1) and remains frozen.
- `EOS.Knowledge`'s Constitution Part 1 §1.2 dependency shape (`EOS.KnowledgeGraph`, `EOS.VectorStore` only) is frozen and accepted as non-negotiable for this ADR.
- `RedisMemoryStore` lives in `EOS.Infrastructure`, which `EOS.Knowledge` has no legal path to.
- §9 names `ContextAssembler` as an internal `EOS.Knowledge` component responsible for composing context payloads (§15).
- No `ContextAssembler` class, no normalization component, and no candidate abstraction currently exist anywhere.
- WP-014 already established and accepted an identical boundary for `query()`: Working/Short-term/Session Memory is real infrastructure but structurally excluded from `IKnowledgeClient`'s Knowledge-Graph-facing methods, since it is never a `KnowledgeNode`.
- `KnowledgeGraphStore.QueryAsync` already returns `IReadOnlyList<KnowledgeNode>` — the exact type `RetrievalRanking.Rank` already accepts.
- WP-014 previously collapsed the analogous `MemoryRouter`/`LifecycleEngine` components into the single `KnowledgeClient` class; no separate class was ever built for either.
- `Program.cs`'s established, ADR-015-001/003-reaffirmed responsibility is one-time construction/wiring, never per-request business logic.
- `assemble_context()` is a per-request operation, not a construction-time concern.
- §20.1 explicitly assigns `assemble_context()` to the `IKnowledgeClient` interface.
- `IKnowledgeClient.QueryAsync`'s existing signature is frozen and unaffected by this ADR.
- No approved ADR currently resolves `ContextAssembler`'s realization, candidate normalization, or `ContextPayload.Items`'s type.

## Alternatives Considered

**Alternative A — Build `ContextAssembler` as a real, separate internal class, with a new candidate-normalization component reaching both `KnowledgeGraphStore` and `RedisMemoryStore`.**
- Advantages: Most literal realization of §9's named component and §15's "any combination of memory types" definition.
- Disadvantages: Requires `EOS.Knowledge` to reach `EOS.Infrastructure` (`RedisMemoryStore`), which Constitution Part 1 §1.2 does not grant.
- Why rejected: Directly contradicts the accepted, non-negotiable fact that `EOS.Knowledge`'s dependency graph is frozen. Resolving it would require a Constitution amendment, which this ADR is not authorized to make.

**Alternative B — Keep `KnowledgeClient` as sole implementer, introduce a new candidate-normalization type, and have `Program.cs` gather Redis-sourced content externally and inject it into the call.**
- Advantages: Avoids the `EOS.Knowledge`→`EOS.Infrastructure` edge by relocating Redis access to the composition root.
- Disadvantages: Requires either a new parameter on `assemble_context()` (a public contract change to §20.1's frozen signature) or per-request business logic inside `Program.cs`, contradicting its established construction-only role (ADR-015-001, ADR-015-003).
- Why rejected: Violates the frozen public contract and the composition root's established, single responsibility.

**Alternative C — `KnowledgeClient` implements `assemble_context()` directly (no separate `ContextAssembler` class), scoped only to `KnowledgeGraphStore`-sourced memory types. Working/Short-term Memory inclusion is structurally inert this WP, extending the same boundary already accepted for `query()`.**
- Advantages: Zero new dependency edge, zero new class, zero new type, zero public contract change beyond the already-planned additive `AssembleContextAsync` member; directly reuses the `MemoryRouter`/`LifecycleEngine`-collapse precedent WP-014 already established; `RetrievalRanking` and `KnowledgeGraphStore` remain completely untouched.
- Disadvantages: §15's "any combination of memory types" is only partially realized this WP.
- Why selected: It is the only alternative that satisfies every accepted, non-negotiable constraint (frozen dependency graph, frozen `RetrievalRanking`, frozen public contract, established composition-root role) simultaneously, without requiring any governance escalation this ADR is not authorized to grant.

## Decision

`ContextAssembler` remains a **logical responsibility collapsed into the `KnowledgeClient` class** — no separate class is created, mirroring WP-014's own treatment of `MemoryRouter`/`LifecycleEngine`. `KnowledgeClient.AssembleContextAsync` composes context **exclusively from `KnowledgeGraphStore`-sourced memory types** (Episodic, Semantic — the same types already reachable via `QueryAsync`'s existing `MemoryType` mapping). No candidate-normalization step or type is introduced: because the only in-scope source (`KnowledgeGraphStore`) already returns `KnowledgeNode`, and `RetrievalRanking.Rank` already accepts `IReadOnlyList<KnowledgeNode>`, the two already agree with zero intermediate transformation. `ContextRequest.IncludesWorking`/`IncludesShortTerm` remain present on the type (fidelity to §15.1's evidenced fields) but have no effect this WP — structurally present, currently inert, exactly the same disclosed pattern `RetrievalRanking` itself already uses for `vector_similarity`/`access_frequency`.

## Responsibilities

| Responsibility | Owner |
|---|---|
| Public API surface for `assemble_context()` | `IKnowledgeClient` |
| Implementation of budgeted, ranked context composition | `KnowledgeClient` (internal `ContextAssembler` logic, collapsed) |
| Retrieval of Episodic/Semantic candidates | `KnowledgeGraphStore` (unchanged) |
| Mechanical ranking of candidates | `RetrievalRanking` (unchanged, frozen) |
| Candidate normalization | **None — dissolved.** The only in-scope source already produces the exact type ranking already accepts; no normalization step exists or is needed. |
| Working/Short-term Memory retrieval | `RedisMemoryStore` (unchanged, real, tested — remains without an `IKnowledgeClient` caller, per WP-014's own accepted disclosure) |
| Composition-root wiring | `Program.cs` (unchanged responsibility; no new wiring required by this ADR) |

## Component Ownership

`EOS.Knowledge` (`KnowledgeClient` — implements `AssembleContextAsync`); `EOS.KnowledgeGraph` (`KnowledgeGraphStore.QueryAsync` — unchanged, reused as-is); `RetrievalRanking` (unchanged, frozen). `EOS.Infrastructure`/`RedisMemoryStore` is **not** a component of this implementation.

## Dependency Impact

**None.** `EOS.Knowledge`'s dependency graph (`EOS.KnowledgeGraph`, `EOS.VectorStore` only) is unchanged. No new `ProjectReference` anywhere.

## Contract Impact

**None beyond what was already planned.** `IKnowledgeClient` gains `AssembleContextAsync(ContextRequest, CancellationToken)` — additive, as already scoped in the Final Implementation Plan. No existing member's signature changes. No new public type beyond `ContextRequest`/`ContextPayload` themselves.

## Implementation Impact

- **Slice S1** (`ContextRequest`/`ContextPayload` types): `ContextPayload.Items` is confirmed as `IReadOnlyList<KnowledgeNode>`. No `RankingCandidate` or any new candidate type is created.
- **Slice S2** (`assemble_context()` algorithm): scoped to `IncludesEpisodic`/`IncludesSemantic`/`ProjectScope`/`Filters` only; `IncludesWorking`/`IncludesShortTerm` are accepted as request fields but have no effect this WP.
- **Slice S3** (`ContextAssembled` event): unaffected in shape.
- **Slices S4–S11** (Consolidation): entirely unaffected — none touch `RetrievalRanking` or `ContextPayload`.

## Compatibility

- WP-014 remains frozen: **confirmed** — no frozen file is touched, modified, or reinterpreted.
- Constitution unchanged: **confirmed** — no edit to Part 1 §1.2 or any other section.
- Roadmap unchanged: **confirmed** — no edit to any WP row.
- Previous ADRs remain valid: **confirmed** — ADR-015-001 through 004 are unaffected; this ADR adds a fifth, independent decision to the same series.

## Consequences

**Positive:** Zero new dependency, zero new class, zero new type, zero contract change; every accepted constraint (frozen dependency graph, frozen `RetrievalRanking`, frozen public contract, established composition-root role) is satisfied simultaneously; WP-015 Slice S1 is unblocked.

**Negative:** §15's "any combination of memory types" definition is only partially realized this WP — Working/Short-term Memory remains outside `assemble_context()`'s actual output.

**Trade-off:** Full specification breadth is deferred in exchange for zero architectural expansion — consistent with the same trade-off WP-014 already made, and accepted, for `query()`.

## Final Board Resolution

All architectural ambiguity regarding `ContextAssembler`, candidate normalization, `RetrievalRanking` input, and `ContextPayload.Items` is considered permanently resolved.
