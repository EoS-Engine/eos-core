# WP-010 Implementation Plan — AI Provider Layer: Registry, Routing, Health & Failover

**Revision:** 1 (Final, Approved — incorporating the Final Consistency/Regression/Scope Audit's refinements)
**Source of Truth (priority order):** `docs/Development-Workflow.md`, `docs/EOS-Specification.md`, `docs/EOS-Implementation-Roadmap-v1.0.md` (WP-010 row), `docs/AI-Provider-Layer-Specification-v1.0.md`, the approved WP-010 Architecture Gap Analysis (9 gaps), the approved Final Consistency/Regression/Scope Audit, this plan.

## Objective (roadmap, verbatim)

Replace WP-005's single hardcoded adapter with the full Provider Registry, Model Registry, Inference Router, Health Monitor, and Failover.

## Architecture Decisions (from the approved Gap Analysis and Audit)

1. `AIProviderManager : IAIProviderClient` becomes the sole public entry point in `EOS.AIProvider`; `Program.cs`'s composition-root wiring constructs it instead of `OllamaProviderAdapter` directly (no `ReasoningEngine` change).
2. `EOS.AIProvider` defines its own plain records (`ProviderProfile`, `ModelProfile`, `HealthThresholds`); `Program.cs` translates already-validated `SharedKernel` config DTOs into them (`EOS.AIProvider` never references `EOS.SharedKernel`).
3. `Providers.json` gains a nested `models` array per provider (`name`, `capabilities`); `Thresholds.json` gains `providerFailureThreshold`, `providerRecoveryProbeIntervalSeconds`, `inferenceTimeoutSeconds` — all `required`, matching the unbroken 10-for-10 existing config-schema precedent; verified safe against every existing test (only one test touches `ProvidersOptions`, via the real config file, never a hand-rolled fixture).
4. A distinct, `Thresholds.json`-configurable inference-call HTTP timeout (default 100s) — applied by `Program.cs` directly to the `HttpClient.Timeout` it constructs, never inside `OllamaProviderAdapter` (unmodified) — replaces the implicit BCL default, since Constitution's generic "REST: 5s" budget is explicitly inapplicable to model inference latency per `AI-Provider-Layer-Specification-v1.0.md` §23's own repeated exclusion of inference time from tight budgets.
5. Failover integration test registers a deliberately-unreachable endpoint (`http://localhost:1`, reusing WP-005's own proven technique) as a test-local, higher-priority candidate alongside the real Ollama endpoint — the real `config/Providers.json` keeps exactly one provider.
6. No real `EventEnvelope`/Part 3 event publication this WP; `ILogger` structured logging satisfies "logged"/"event" roadmap wording, consistent with the unbroken WP-005–WP-009 precedent.
7. Failover on first dispatch failure — no same-candidate retry loop (roadmap's own "Included components" names Health Monitoring and Failover, not a separate Retry Strategy).
8. `OllamaProviderAdapter` unmodified. `ProviderRegistry` and `ModelRegistry` are **merged into one class** (Scope Audit refinement): §10.3's stated reason for separating them — "one provider may host multiple models" — has zero current consumers (exactly one provider, one model exist today); Context Builder/Response Adapter/Capability Manager are **not** separate classes — the first two are already correctly implemented inside `OllamaProviderAdapter`; the third is folded into `ProviderRegistry`'s capability-query method.
9. Real `IProtectionClient.validate()`/Inference-Budget integration inside the routing algorithm (§15.5/§15.6) is deferred — no roadmap acceptance criterion requires it, no evidenced `ActionRequest` field mapping exists for this call site, and a Protection-Deny outcome has no home in the closed `InferenceErrorType` set without an unauthorized contract change. `InferenceRouter`'s scope this WP: capability filtering (§15.1), health filtering (§15.3, Health-Monitor-only), preference-order ranking (§15.2/line 276/336 — "AI Architect preference order, `Providers.json`"). **Correction (post-review):** an earlier draft of this line also claimed "priority pass-through (§15.4)" as in scope; this was inaccurate and has been removed — `InferenceRequest.Priority` is never read anywhere in routing. §15.4 assigns that field meaning only in the context of a request scheduler/queue (Planning & Execution Engine, WP-023+), which does not exist yet; `AskCommand`'s synchronous, single-request CLI path has nothing for a priority value to be weighed against. Deferred, not implemented — no code claims otherwise.
10. **(Post-review amendment)** Structured event logging (Decision 6) is satisfied via a new zero-dependency `IProviderEventLogger` interface defined in `EOS.AIProvider` itself (`LogEvent`/`LogWarning`, both `string`-typed — no `Microsoft.Extensions.Logging` types referenced), not via `ILogger<T>` directly. `Program.cs` (composition root, which already depends on `Microsoft.Extensions.Hosting`/`ILogger<T>`) adapts the real logger into this interface via a small `LoggerProviderEventLogger` class declared in `Program.cs`. This preserves the plan's original "Package Changes: None" — an initial draft had added `Microsoft.Extensions.Logging.Abstractions` to `EOS.AIProvider.csproj`, which a dependency audit (prompted by a pre-approval review question) showed was avoidable and inconsistent with WP-004's own "project defines its own plain type; composition root bridges to real infrastructure" precedent (`DataStoreConnectionOptions.FromEnvironment()`). The package was removed before approval.
11. **(Post-review corrections, final pre-commit audit)** Three additional defects were found and fixed, none requiring a fresh Architecture Impact Report (bug fixes / doc corrections within already-approved scope, not new architecture):
    - **Disposal correctness:** `Program.cs`'s `try`/`finally` (protecting the constructed `HttpClient`s) originally started only around the final `AskCommand.ExecuteAsync` call, leaving `DataStoreConnectionOptions.FromEnvironment()` and `KnowledgeGraphStore.EnsureTableExistsAsync()` outside it — either throwing would leak the `HttpClient`s. Fixed by moving the `try` to wrap everything from immediately after `httpClients` are constructed through the end.
    - **Roadmap-compliance gap:** the roadmap's WP-010 row lists "Health Monitoring (availability, latency, failure detection)" as an included component; `HealthMonitor` tracked only availability and failure detection. Fixed by adding an optional `TimeSpan? latency` parameter to `RecordSuccess`/`RecordFailure` (sourced from the adapter's already-returned `InferenceResult.Latency`), stored on `ProviderHealthState` and included in the `InferenceCompleted` log line.
    - **Dead code / YAGNI:** `AIProviderManager`'s `if (!adapters.TryGetValue(...)) continue;` guard defended against a state construction never permits (the adapters dictionary and `ProviderRegistry` are always built from the same provider list) and was never exercised by any test. Replaced with a direct indexer lookup, trusting the construction-time invariant per this codebase's established "don't defend against impossible states" convention.

## Included Scope (roadmap, verbatim)

Full Provider/Model Registry with `Providers.json`-driven configuration; the complete routing algorithm (§15.7) including capability/health/resource/priority/policy filtering (scoped per Decision 9 above); Health Monitoring (availability, latency, failure detection); Failover to a ranked candidate list.

## Explicitly Excluded Scope (roadmap, verbatim)

`IEmbeddingProviderClient` (WP-011); `discover_capabilities()` (WP-011); Future Cloud/Vision/Specialized provider *types* (architecturally supported, not implemented).

## Vertical Slice Definition

`InferenceRequest` → `AIProviderManager.InferAsync()` → `InferenceRouter.Route()` (capability + health filter, ranked) → dispatch to the first-ranked candidate's real adapter → on failure, `HealthMonitor.RecordFailure()` + failover to the next-ranked candidate → on success, `HealthMonitor.RecordSuccess()` + normalized `InferenceResult` returned — proven both by a unit-level filter-chain test and a real, live-Ollama Failover integration test.

## Projects Affected

`EOS.AIProvider` (primary), `src/EOS.Runner/Program.cs` (composition-root wiring change, evidenced deviation from the roadmap's literal "Projects affected" field per Decision 1, mirroring WP-009's own precedent), `src/EOS.SharedKernel/Configuration/` (schema extension per Decision 3), `config/*.json` (Decision 3).

## Files to Create

- `src/EOS.AIProvider/ProviderProfile.cs`, `ModelProfile.cs`, `HealthThresholds.cs`
- `src/EOS.AIProvider/ProviderRegistry.cs` (merged Provider+Model Registry, Decision 8)
- `src/EOS.AIProvider/InferenceRouter.cs`
- `src/EOS.AIProvider/HealthMonitor.cs`
- `src/EOS.AIProvider/AIProviderManager.cs`
- `src/EOS.AIProvider/IProviderEventLogger.cs` (added post-review: a zero-package structured-event abstraction — see Decision 10)
- `tests/EOS.AIProvider.Tests/ProviderRegistryTests.cs`, `InferenceRouterTests.cs`, `HealthMonitorTests.cs`, `AIProviderManagerFailoverIntegrationTests.cs`, `NoOpProviderEventLogger.cs` (hand-rolled stub, no mocking framework)
- `docs/work-packages/WP-010-Completion-Report.md` (at closure)

## Files to Modify

- `src/EOS.SharedKernel/Configuration/ProvidersOptions.cs` — add `ModelEntry`, `ProviderEntry.Models`
- `src/EOS.SharedKernel/Configuration/ThresholdsOptions.cs` — add three new `required` fields
- `config/Providers.json`, `config/Thresholds.json` — populate the new fields
- `src/EOS.Runner/Program.cs` — construct `AIProviderManager` instead of `OllamaProviderAdapter` directly

## Files Forbidden to Change

`src/EOS.AIProvider/OllamaProviderAdapter.cs`, `src/EOS.Reasoning/**`, `src/EOS.Gates/**`, `src/EOS.Knowledge/**`, `src/EOS.SDK/**` (no contract change — verified: `ProviderUnavailable` already covers total-Failover-exhaustion per §17.5), `src/EOS.Runner/Bootstrap/**`, `src/EOS.Runner/Commands/AskCommand.cs`, `src/EOS.Tools/**`, any specification/roadmap/Constitution document.

## Dependency Changes

None — no new `ProjectReference` anywhere. `EOS.AIProvider` remains `EOS.SDK`-only.

## Package Changes

None.

## Public Contracts

`IAIProviderClient`, `InferenceRequest`, `InferenceResult`, `InferenceErrorType` (all `EOS.SDK`) — **unchanged**.

## Test Strategy

Unit: `ProviderRegistry`'s capability-query filtering; `InferenceRouter`'s filter/ranking chain; `HealthMonitor`'s failure-threshold/recovery-interval state transitions. Integration (real Ollama + one simulated failure, zero mocks of the working path): `AIProviderManager.InferAsync()` succeeds via Failover when a higher-priority candidate is unreachable. Regression: WP-009's `AskCommandIntegrationTests.cs` and `EOS.Reasoning.Tests`/`EOS.AIProvider.Tests`'s existing suites must all still pass, unmodified.

## Acceptance Criteria (roadmap, verbatim)

The vertical slice demo still passes; a deliberately-injected Ollama outage produces a clean `ProviderMarkedUnavailable` event (satisfied via structured logging, Decision 6) and, if a second candidate is registered, a successful Failover.

## Implementation Sequence

1. Feature branch `wp-010-ai-provider-registry-router` (created).
2. This plan document.
3. `EOS.SharedKernel.Configuration` schema extension; `config/*.json` updates.
4. `EOS.AIProvider`: `ProviderProfile`/`ModelProfile`/`HealthThresholds`/`ProviderRegistry`/`InferenceRouter`/`HealthMonitor`/`AIProviderManager`.
5. `src/EOS.Runner/Program.cs` composition-root update.
6. `EOS.AIProvider.Tests` unit + Failover integration tests.
7. Full Local Verification Checklist, including full-suite regression proof.
8. Architecture Gate self-review.
9. Stop for approval before push/PR.
