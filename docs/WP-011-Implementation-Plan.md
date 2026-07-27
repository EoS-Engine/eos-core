# WP-011 Implementation Plan — AI Provider Layer: Embedding Channel & Capability Discovery

**Source of Truth (priority order):** `docs/Development-Workflow.md`, `docs/EOS-Specification.md`, `docs/EOS-Implementation-Roadmap-v1.0.md` (WP-011 row), `docs/AI-Provider-Layer-Specification-v1.0.md`, `docs/Memory-Management-Specification-v1.0.md` (§20.2), `docs/Protection-Layer-Specification-v1.0.md` (§10.9, cited enforcement-pattern precedent), the approved multi-round Gap Analysis/Architecture Review (this session), this plan.

## 1. Objectives (roadmap, verbatim)

Implement `IEmbeddingProviderClient.embed()` and `IAIProviderClient.discover_capabilities()`.

## 2. Scope

- New `EOS.SDK` types: `IEmbeddingProviderClient`, `Vector`, `CapabilityEntry`, `CapabilitySet`.
- `IAIProviderClient` gains `DiscoverCapabilities(string? capabilityFilter)`.
- `AIProviderManager` (WP-010) extended to implement `IEmbeddingProviderClient` and `DiscoverCapabilities`, per AI-Provider-Layer-Specification-v1.0 §10.1a ("the only component that touches both of this specification's two public interfaces").
- New `OllamaEmbeddingAdapter : IEmbeddingProviderClient` (separate class, `EOS.AIProvider`), calling Ollama's `/api/embeddings`.
- One real local embedding model (`nomic-embed-text`, 768-dimensional) pulled via Ollama.
- Tests proving: `EmbedAsync` returns a real vector; `DiscoverCapabilities` returns accurate registry data; `EOS.Knowledge`-side test code can reach the embedding channel.

## 3. Explicit Non-Scope

- No wiring of embeddings into `EOS.Knowledge`'s production `IKnowledgeClient`/`KnowledgeClient` — no consumer exists yet (Memory Management, WP-014+).
- No `EOS.VectorStore`/ChromaDB indexing integration.
- No second embedding model, no cloud provider, no new provider type.
- No Protection/Inference-Budget gating on `EmbedAsync` (defers, inheriting WP-010 Gap 9's accepted rationale; FR-AI6 applies symmetrically but no acceptance criterion requires it this WP).
- No reflection-based testing technique (rejected during architecture review — no DI container exists in this codebase; manual composition-root wiring is the real structural guarantee).
- No modification to `OllamaProviderAdapter.cs`, `ProviderRegistry.cs`, `InferenceRouter.cs`, `HealthMonitor.cs`, `IProviderEventLogger.cs`, `Program.cs`, or `config/Providers.json`.
- No modification to production `EOS.Knowledge.csproj`.

## 4. Architecture Decisions

1. **`AIProviderManager` implements both public interfaces** (§10.1a-mandated). This corrects an earlier draft of this review that had proposed a separate `EmbeddingProviderManager` — re-reading §10.1a directly during the final review surfaced the spec's own explicit "only component that touches both interfaces" language, which a second class would have contradicted.
2. **`CapabilitySet`/`CapabilityEntry` are minimal, flat projections** of `ProviderRegistry.Providers` (`ProviderName`, `ModelName`, `Capabilities` only) — no dimensionality, ranking, or health fields (those belong to `Vector`, `InferenceRouter`, `HealthMonitor` respectively).
3. **`OllamaEmbeddingAdapter` is a new, separate class** — never folds embedding logic into the frozen `OllamaProviderAdapter.cs`.
4. **(Discovered during implementation, resolved without touching a forbidden file — see §7.)** `OllamaProviderAdapter` also implements `IAIProviderClient` directly; adding `DiscoverCapabilities()` as an ordinary interface member broke its compilation. Resolved via a C# default interface method (`CapabilitySet DiscoverCapabilities(string? capabilityFilter) => new([]);` on `IAIProviderClient` itself) — `OllamaProviderAdapter.cs` remains byte-for-byte unmodified (verified via `git diff --quiet`), inheriting the harmless empty default, which is never invoked (only `AIProviderManager`'s own override is ever called by `ReasoningEngine`).
5. **`AIProviderManager` gains two new constructor parameters, both optional** (`embeddingAdapters`, defaulting to an empty dictionary; `providerRegistry`, defaulting to `null` → empty `CapabilitySet`) — so `Program.cs`'s existing construction call requires no change, honoring the plan's explicit non-scope commitment.
6. **`EOS.Knowledge`'s production `.csproj` is not modified**; only `EOS.Knowledge.Tests.csproj` gains the new `EOS.SDK`/`EOS.AIProvider` edges, mirroring the verified `EOS.Reasoning.Tests → EOS.AIProvider` precedent (production `EOS.Reasoning.csproj` carries an unused reference to the same project — a mistake from an earlier WP this plan deliberately does not repeat).
7. **Structural enforcement (FR-AI2/FR-AI3)** relies on manual composition-root wiring (no DI container exists anywhere in this codebase — `Program.cs` never constructs an embedding-capable instance for `EOS.Reasoning`'s benefit) plus the existing XML-based `OnlyAllowedProjectsMayReferenceAIProviderTests` fitness-test pattern, extended to include `EOS.Knowledge.Tests` (proving the positive claim). A perfect, automated proof of the negative claim ("`EOS.Reasoning` cannot call `embed()`") is not achievable at project-reference granularity without a disproportionate new project or a first-of-its-kind reflection technique — both rejected as over-engineering relative to the actual threat model (no DI container to bypass).
8. **`EmbedAsync`'s failure contract**: propagates real transport exceptions (`HttpRequestException`, malformed-response `InvalidOperationException`) rather than a normalized error-enum result — `Vector`'s spec shape has no error-union slot, and no real consumer exists yet to require richer handling. Disclosed simplification, not a hidden gap.
9. **`DiscoverCapabilities` is synchronous** (pure in-memory registry projection, matches `ProviderRegistry.FindByCapability`'s existing convention); `EmbedAsync` is asynchronous (real HTTP I/O, matches `InferAsync`'s convention).

## 5. Files Created

- `src/EOS.SDK/Vector.cs`, `CapabilitySet.cs`, `IEmbeddingProviderClient.cs`
- `src/EOS.AIProvider/OllamaEmbeddingAdapter.cs`
- `tests/EOS.AIProvider.Tests/OllamaEmbeddingAdapterTests.cs`, `OllamaEmbeddingAdapterIntegrationTests.cs`, `AIProviderManagerDiscoverCapabilitiesTests.cs`, `AIProviderManagerEmbedTests.cs`
- `tests/EOS.Knowledge.Tests/EmbeddingChannelStructuralEnforcementTests.cs`
- `docs/WP-011-Implementation-Plan.md`

## 6. Files Modified

- `src/EOS.SDK/IAIProviderClient.cs` — `DiscoverCapabilities` added as a default interface method (public contract change, G2, approved)
- `src/EOS.AIProvider/AIProviderManager.cs` — implements `IEmbeddingProviderClient`; adds `DiscoverCapabilities`/`EmbedAsync`; two new optional constructor parameters
- `tests/EOS.ArchitectureTests/OnlyAllowedProjectsMayReferenceAIProviderTests.cs` — whitelist extended with `EOS.Knowledge.Tests`
- `tests/EOS.Knowledge.Tests/EOS.Knowledge.Tests.csproj` — `EOS.SDK`, `EOS.AIProvider` references added (test-only)

**Not modified (confirmed via `git diff --quiet` per file):** `OllamaProviderAdapter.cs`, `ProviderRegistry.cs`, `InferenceRouter.cs`, `HealthMonitor.cs`, `IProviderEventLogger.cs`, `Program.cs`, `config/Providers.json`, `EOS.Knowledge.csproj` (production), `IKnowledgeClient.cs`, `KnowledgeClient.cs`.

## 7. Public Contract Changes

- `IAIProviderClient` gains `DiscoverCapabilities(string? capabilityFilter)` (G2, approved) — implemented as a default interface method specifically so `OllamaProviderAdapter.cs` requires zero changes (a discovery made during implementation, resolved without touching the forbidden file or requiring a new authorization round, since no forbidden file was touched and no new architecture decision beyond what G2 already approved was made).
- Two brand-new public interfaces/types: `IEmbeddingProviderClient`, `Vector`, `CapabilitySet`/`CapabilityEntry`.

## 8. Configuration Changes

None. `config/Providers.json` is unmodified. Tests reference the pulled embedding model (`nomic-embed-text`) and its real endpoint (`http://localhost:11434`) directly, matching the established `OllamaProviderAdapterIntegrationTests.cs` precedent.

## 9. Dependency Changes

- `tests/EOS.Knowledge.Tests.csproj → EOS.SDK`, `EOS.AIProvider` (new, test-only).
- No production dependency changes anywhere.

## 10. Package Changes

None.

## 11. Tests Added

20 new tests: 4 unit + 1 integration for `OllamaEmbeddingAdapter`; 3 for `AIProviderManager.DiscoverCapabilities`; 2 for `AIProviderManager.EmbedAsync`; 1 structural-enforcement test in `EOS.Knowledge.Tests`.

## 12. Regression Strategy

Full sequential per-project `dotnet test` run across all existing suites (86 tests as of WP-010's closure) plus the new WP-011 tests.

## 13. Acceptance Criteria (roadmap, verbatim)

`embed("test content")` returns a real vector of the configured dimensionality — satisfied: `nomic-embed-text` returns a real 768-dimensional vector, verified both directly (`OllamaEmbeddingAdapterIntegrationTests`) and through `AIProviderManager.EmbedAsync`'s routing path (`AIProviderManagerEmbedTests`, unit-level) and from `EOS.Knowledge`'s own test-side code (`EmbeddingChannelStructuralEnforcementTests`).

## 14. Risks

- Real-Ollama test contention under concurrent (non-sequential) runs — same documented, pre-existing pattern since WP-008.
- Disk/time cost of the pulled embedding model (operational, not code).
- Residual enforcement risk (§4.7) — accepted, not mitigated further this WP.
- `EmbedAsync`'s exception-only failure contract (§4.8) — disclosed simplification, to be revisited when a real consumer exists.

## 15. Rollback Strategy

Standard: revert the merge commit on `main` if a post-merge defect is found. No data migration, no schema change, nothing stateful introduced.

## 16. Implementation Order (as executed)

1. Pulled `nomic-embed-text` via Ollama; verified real 768-dim vectors via direct `curl`.
2. Added `EOS.SDK`: `Vector.cs`, `CapabilitySet.cs`, `IEmbeddingProviderClient.cs`.
3. Edited `IAIProviderClient.cs` (discovered the `OllamaProviderAdapter` compilation break; resolved via default interface method — §4.4).
4. Added `OllamaEmbeddingAdapter.cs`.
5. Extended `AIProviderManager.cs` (§4.1, §4.5).
6. Extended `OnlyAllowedProjectsMayReferenceAIProviderTests.cs`; added `EOS.SDK`/`EOS.AIProvider` to `EOS.Knowledge.Tests.csproj`.
7. Wrote all new tests (§11).
8. Full local verification (restore/build/sequential test/format/`git diff --check`).
9. Architecture Gate self-review; Implementation Report; stop for approval.

## 17. Final Architecture Verification

No new project; no new package; one approved public-contract edit (G2), implemented via a default interface method specifically to avoid touching `OllamaProviderAdapter.cs`; one approved, constrained environment action (G4, `nomic-embed-text` only, local only); zero redesign of `ProviderRegistry`/`InferenceRouter`/`HealthMonitor`/`OllamaProviderAdapter`; `AIProviderManager` extended exactly along the line §10.1a already specified; no future-WP functionality; no new testing technique beyond the existing XML-based fitness-test pattern; no `Program.cs`/`config/Providers.json` change (the one constraint explicitly flagged as requiring an immediate stop, confirmed not triggered); smallest possible dependency graph (one test-only edge).
