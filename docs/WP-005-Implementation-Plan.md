# WP-005 Implementation Plan — AI Provider Layer: Single-Adapter Inference Channel

**Revision:** 3 (Final, Approved)
**Source of Truth:** `docs/EOS-Implementation-Roadmap-v1.0.md` (WP-005 row), `docs/AI-Provider-Layer-Specification-v1.0.md` §14/§16/§20.1, `docs/EOS-Specification.md` §0.14.1 (`EOS.SDK` as cross-module contract host), `docs/Development-Workflow.md`.

## Current Repository Baseline

- `EOS.slnx` already registers `src/EOS.AIProvider/EOS.AIProvider.csproj` and `src/EOS.SDK/EOS.SDK.csproj` as WP-001 skeletons.
- `EOS.AIProvider.csproj` currently references `EOS.Contracts` and `EOS.SDK`, with zero source files.
- `EOS.SDK.csproj` currently references `EOS.Core`, `EOS.SharedKernel`, `EOS.Contracts` (pre-existing WP-001 scaffold, unchanged by this WP), with zero source files.
- `EOS.Contracts` contains only `EventEnvelope<TPayload>` — no type WP-005 consumes.
- `config/Providers.json` (`ollama`, `http://localhost:11434`, priority 1) and `config/Inference.json` (`qwen2.5-coder:7b`, maxTokens 4096, temperature 0.2) already committed (WP-002), already structurally validated by `BootstrapRunner`.
- Ollama verified live and reachable with the exact configured model.
- `git`: branch `wp-005-ai-provider-single-adapter` created from clean `main` (`f954c78`).

## Objective (roadmap, verbatim)

Implement `IAIProviderClient.infer()` against a single Ollama adapter — enough to satisfy Reasoning Engine's needs, not the full Provider/Model Registry yet.

## Included Scope

- `EOS.SDK`: `IAIProviderClient` (interface, `InferAsync()` only), `InferenceRequest`, `InferenceResult`, `InferenceErrorType` (closed five-value set: `ProviderUnavailable`, `CapabilityUnsupported`, `ContextTooLarge`, `MalformedResponse`, `Timeout`).
- `EOS.AIProvider`: `OllamaProviderAdapter` — sole concrete implementation of `IAIProviderClient`, targeting Ollama's local `/api/generate` REST endpoint (`stream: false`), taking endpoint/model/maxTokens/temperature as plain constructor parameters.
- `tests/EOS.AIProvider.Tests/`: unit tests (request/response normalization, error translation) + one real integration test against the running Ollama instance.
- `tests/EOS.ArchitectureTests/OnlyAllowedProjectsMayReferenceAIProviderTests.cs`: whitelist fitness test (allowed: `EOS.AIProvider`, `EOS.AIProvider.Tests`, `EOS.ArchitectureTests`).

## Explicitly Excluded

Provider/Model Registry, Routing beyond one adapter, Health Monitoring/Failover (WP-010); `IEmbeddingProviderClient`, `discover_capabilities()` (WP-011); real `IProtectionClient` gate — `EOS.Protection` does not exist until WP-006, so only a mechanical token-budget-estimate-vs-`maxTokens` check is implemented, not a Protection call; §19 event emission — no current subscriber exists, deferred with no owning WP assigned yet; Bootstrap/`EOS.Runner` wiring; `EOS.Reasoning` changes; DI container; retry/circuit-breaker logic; caching; configuration redesign.

## Vertical Slice Definition

`InferenceRequest` (`EOS.SDK`) → `IAIProviderClient.InferAsync()` → `OllamaProviderAdapter` (`EOS.AIProvider`) → real `POST /api/generate` → `InferenceResult` (`EOS.SDK`). The integration test plays the caller role for this WP; no change to `EOS.Reasoning` is required or made.

## Projects Affected

`EOS.SDK` (new: contract types), `EOS.AIProvider` (new: adapter implementation).

## Files to Create

- `src/EOS.SDK/IAIProviderClient.cs`
- `src/EOS.SDK/InferenceRequest.cs`
- `src/EOS.SDK/InferenceResult.cs`
- `src/EOS.SDK/InferenceErrorType.cs`
- `src/EOS.AIProvider/OllamaProviderAdapter.cs`
- `tests/EOS.AIProvider.Tests/EOS.AIProvider.Tests.csproj`, `AssemblyInfo.cs`, `OllamaProviderAdapterTests.cs`
- `tests/EOS.ArchitectureTests/OnlyAllowedProjectsMayReferenceAIProviderTests.cs`
- `docs/work-packages/WP-005-Completion-Report.md` (at closure)

## Files to Modify

- `src/EOS.AIProvider/EOS.AIProvider.csproj` — remove unused `ProjectReference` to `EOS.Contracts`; keep only `EOS.SDK`.
- `EOS.slnx` — add `tests/EOS.AIProvider.Tests/EOS.AIProvider.Tests.csproj`.

## Files That Must NOT Change

`src/EOS.Runner/**`, `src/EOS.Reasoning/**`, `src/EOS.SharedKernel/Configuration/**`, `config/*.json`, `src/EOS.Infrastructure/**`, `src/EOS.Contracts/**`, `src/EOS.SDK/EOS.SDK.csproj`'s existing references, any specification/roadmap document.

## Dependency Changes

- **Removed:** `EOS.AIProvider → EOS.Contracts` (unused — no type from `EOS.Contracts` is consumed anywhere in WP-005's scope; event emission, the only consumer of `EventEnvelope`, is explicitly deferred).
- **Kept:** `EOS.AIProvider → EOS.SDK` (required — `OllamaProviderAdapter` implements `IAIProviderClient` and constructs/returns `InferenceRequest`/`InferenceResult`/`InferenceErrorType`, all defined in `EOS.SDK`).

## Package Changes

None. `System.Net.Http.Json` (BCL) only.

## Architecture Boundary Review

Project graph for this WP: `EOS.AIProvider → EOS.SDK` only. `EOS.SDK`'s pre-existing scaffold references (`EOS.Core`, `EOS.SharedKernel`, `EOS.Contracts`) are untouched — out of this WP's declared boundary. No role project references `EOS.AIProvider` yet; the fitness test enforces this as the current, real state.

## KISS/YAGNI Review

Single adapter, no Registry/Router, no DI container, no new package, `discover_capabilities()` excluded, unused `EOS.Contracts` reference removed.

## Test Strategy

Unit: request/response normalization, error translation (malformed JSON → `MalformedResponse`, unreachable endpoint → `ProviderUnavailable`, oversized token estimate → `ContextTooLarge`). Integration: one real call against the running local Ollama instance, zero mocks. Architecture: whitelist fitness test.

## Acceptance Criteria (roadmap, verbatim)

A test harness calls `infer()` with a sample prompt and receives a normalized `InferenceResult` containing real model output.

## Definition of Done

Full `docs/Development-Workflow.md` §14 checklist; tag `v0.5.0-wp005` (not created this session — closure requires separate approval).

## Risks

Ollama latency variance mitigated with a short deterministic prompt and small `num_predict`; FR-AI6/§19 deferrals recorded here and in the closure report, naming WP-006/WP-008 as future owners.

## Future WP Boundaries

WP-010 (full Registry/Router/Health/Failover), WP-011 (`IEmbeddingProviderClient`, `discover_capabilities()`), WP-006 (real Protection gate), WP-008 (real `EOS.Reasoning` consumption and any resulting dependency-graph decision).

## Proposed Implementation Sequence

1. Feature branch `wp-005-ai-provider-single-adapter` (created).
2. This plan document (this file).
3. Implement `EOS.SDK` contract types.
4. Implement `EOS.AIProvider`'s `OllamaProviderAdapter`; remove unused `EOS.Contracts` reference.
5. Implement `EOS.AIProvider.Tests`.
6. Implement `OnlyAllowedProjectsMayReferenceAIProviderTests`.
7. Add test project to `EOS.slnx`.
8. Full Local Verification.
9. Architecture Gate self-review.
10. Push branch, open PR, real CodeRabbit review, fix VALID findings only.
11. Wait for explicit approval before merge/tag/closure.
