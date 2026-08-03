# WP-022 Implementation Plan — Resource Management: Quotas, Background Task Controller & Model Residency

## Revision and Source of Truth

Final (post-Recovery). Built from: `docs/Resource-Management-Specification-v1.0.md` §10.4, §10.6, §14, §15, §16, §19, §20, §21, §22, §24 (Component Diagram); `docs/EOS-Implementation-Roadmap-v1.0.md` WP-022 row and Traceability Matrix; `docs/EOS-Specification.md` Part 1 §1.2 (dependency table); ADR-RM002, ADR-RM003; ADR-015-001 (Composition Root Adapter Pattern, WP-016/WP-021 precedent); the approved Phase 1/2/3 reports; the approved Principal Engineer Verification Report and Recovery Plan (Findings F1–F7). This document reflects the system as it exists after Recovery Slices R1–R4 — it does not describe the pre-recovery behavior.

## Objective

Implement fair-share Quotas, the live Background Task Controller gate, and Model Residency Management (roadmap row, verbatim).

## Scope

**Included work (roadmap "Included components," verbatim):** "The five-tier resource-class hierarchy; `request_background_slot()`; Model Loading/Unloading/Residency tracking, feeding AI Provider Layer's routing as a Resource Availability input (retrofit to WP-010)."

**Excluded work (roadmap "Explicitly excluded," verbatim):** "GPU resource type registration (no hardware to register against); domain-specific tuning (flagged as future work by the frozen specification itself)."

Also excluded, disclosed during this WP's own investigation (not roadmap-named, but confirmed out of reach):
- Retrofit into `EOS.AIProvider` (consuming `get_model_residency`) — not named in the roadmap's Expected Deliverables/Test Verification/Demo Criteria fields; no change made to `EOS.AIProvider` in this WP.
- `ProtectionAllowed`/`ProtectionDenied` consumption (§20.1) — scoped out during Phase 2; not required by any of the roadmap's four acceptance-bearing fields.

## Final Architecture

```text
EOS.Contracts (additive)
 ├── ResourceClass (enum)          — 5 values, §16 rank order
 ├── ModelResidencyState (enum)    — Unloaded, Loading (unreachable, disclosed), Resident, Unloading
 ├── ModelResidencyStatus (record) — ModelId, State, RamFootprintMegabytes (nullable, always null — F4)
 └── IResourceManagementClient     — extended with GetModelResidency, RequestBackgroundSlot (§21.1)

EOS.Resources (new + modified)
 ├── QuotaManager             — §10.4/§19: fair-share Model-slot enforcement, Starvation Prevention
 │                               (§19.4, overrides every §15.1 step per Recovery R1); CPU/RAM
 │                               quota fields configured but disclosed-unenforced (F3)
 ├── BackgroundTaskController — §10.6/§15.1: starvation-override check, then CPU tier check,
 │                               then quota check, then maintenance-window check (D10, disclosed
 │                               stub — always true)
 ├── ResourceMonitor (modified) — per-model residency tracking; footprint always null, honest
 │                                 per Recovery R3 (no legal measurement source exists — F4)
 ├── CapacityManager (modified) — EmergencyCapacitySignal/ResourceRecovered on exact §17.4/§19.5
 │                                 transition conditions
 ├── ResourceManagementClient (modified) — implements the two new interface methods
 └── 7 new Composition Root Adapter interfaces (ADR-015-001): IBackgroundJobGrantedEventPublisher,
     IBackgroundJobDeferredEventPublisher, IModelLoadedEventPublisher, IModelUnloadedEventPublisher,
     IResourceQuotaExhaustedEventPublisher, IEmergencyCapacitySignalEventPublisher,
     IResourceRecoveredEventPublisher

EOS.Knowledge (additive)
 └── IBackgroundSlotRequester (ADR-015-001) — CompressionSweep's retrofit call point

EOS.Runner/Program.cs (composition root)
 ├── constructs QuotaManager, BackgroundTaskController, the 7 new adapters
 ├── constructs ResourceManagementBackgroundSlotRequester (correlates the void
 │    RequestBackgroundSlot call with its resulting Granted/Deferred event, synchronously)
 └── wires CompressionSweep's new IBackgroundSlotRequester dependency

EOS.SharedKernel/ThresholdsOptions.cs, config/Thresholds.json
 └── 18 new fields: 15 quota ceilings (§19.2), QuotaStarvationDenialCountThreshold (§19.4),
     ModelIdleResidencyTimeoutSeconds (§14.2), QuotaWindowSeconds (dedicated per Recovery R4)

EOS.Runner/Bootstrap/BootstrapRunner.cs
 └── validates the 15 quota fields are non-increasing by §16 resource-class rank
```

`EOS.Resources`'s Constitution Part 1 §1.2 dependency row remains unchanged: `EOS.Contracts, EOS.SDK`. No new project reference was introduced anywhere in this WP.

## Implementation Decisions (D1–D10, as approved in Phase 3)

- **D1**: `ResourceClass` — 5 values, exact §16 rank order.
- **D2**: `ModelResidencyStatus` shape — `ModelId`, `State`, `RamFootprintMegabytes` (nullable).
- **D3 (superseded by Recovery R3)**: originally a before/after RAM-delta measurement around `InferenceRouted`; found to always measure ~0 because no legal "before loading" instant exists in any frozen event. Corrected: `RamFootprintMegabytes` is always `null`; `ModelLoaded` still publishes with `0.0` as the disclosed unmeasurable sentinel.
- **D4**: Starvation Prevention's "consecutive Sprint cycles" — no Sprint-cycle clock exists anywhere in this codebase (same disclosed condition as `CompressionSweep.cs`'s cadence gap, WP-016); counts consecutive Background Task Controller evaluations instead.
- **D5**: Quota ceiling shape — 3 resource types (CPU/RAM/Model-slot) × 5 resource classes = 15 fields, per §19.2.
- **D6**: `EmergencyCapacitySignal`/`ResourceRecovered` — produced by the existing `CapacityManager`; exact trigger: Emergency signal on any transition *to* Emergency; Recovered only on Critical/Emergency → *exactly* Safe (§19.5's literal "returns below Warning").
- **D7**: `ProtectionAllowed`/`ProtectionDenied` consumption — out of roadmap scope, not implemented.
- **D8**: `request_background_slot()`'s `void` return — outcome observed via `BackgroundJobGranted`/`BackgroundJobDeferred`, correlated synchronously in `ResourceManagementBackgroundSlotRequester` (EventMediator's `Publish` is synchronous and in-process).
- **D9**: `QuotaManager.IsClassQuotaExhausted`'s Model-slot check — no frozen "job completed" signal exists; resolved via an elapsed-time rolling window (dedicated `QuotaWindowSeconds` field per Recovery R4), mirroring `ResourceMonitor`'s own sampling-throttle pattern.
- **D10**: Background Task Controller's maintenance-window check — no Maintenance-Window data source exists anywhere in this codebase; `WithinMaintenanceWindow()` always returns `true`, disclosed identically to D4.

## Recovery Slices (R1–R5)

Implemented after the Principal Engineer Final Review found 7 findings (F1–F7), all confirmed TRUE on independent re-verification.

- **R1 (Finding F1)**: Starvation Prevention did not override CPU-load-caused deferral, violating §19.4's "regardless of contention." Fixed: `QuotaManager.IsStarvationOverrideActive` is now checked first in `BackgroundTaskController.RequestBackgroundSlot`, ahead of every §15.1 step.
- **R2 (Finding F2)**: `ResourceQuotaExhausted` fired for every deferral reason, not just genuine quota exhaustion. Fixed: `QuotaManager.RecordDenial` (counter-only) and `PublishQuotaExhausted` (event, called only from the genuine quota-exhaustion branch) are now separate methods.
- **R3 (Findings F4, F5)**: Model RAM-footprint measurement was structurally guaranteed to return ~0 (no real "before loading" instant exists in any frozen event — confirmed by a final repository-wide search for any real model-loading lifecycle mechanism; none found). Fixed: footprint is always honestly `null`; `ModelResidencyState.Loading` is retained (§22 names it) with a doc comment disclosing it is currently unreachable.
- **R4 (Finding F6)**: The Quota Manager's rolling-window duration was silently coupled to the unrelated OS-sampling-cadence field. Fixed: dedicated `QuotaWindowSeconds` field, defaulted to `30` to preserve identical runtime behavior.
- **R5 (Findings F3, F7)**: Documentation-only. `ResourceClassQuota`'s CPU/RAM fields now carry an explicit disclosure comment (see Known Limitations). This document itself resolves F7.

## Known Limitations

- **CPU/RAM fair-share quotas (§19.2) are configured but not enforced** (Finding F3, disclosed in `ResourceClassQuota.cs`). Only `ModelSlotCount` is checked by `QuotaManager.IsClassQuotaExhausted`. `ResourceMonitor` provides only aggregate, system-wide CPU/RAM measurements — no frozen document or existing mechanism attributes measured CPU/RAM to a specific `ResourceClass`. Genuine enforcement would require new per-class resource-accounting instrumentation not defined by any frozen document; out of this WP's scope as new architecture.
- **Model RAM-footprint is never measured** (Finding F4, resolved honestly rather than fixed). `ModelResidencyStatus.RamFootprintMegabytes` is always `null`; `ModelLoaded` events always carry `0.0`. No frozen event (in this or `AI-Provider-Layer-Specification-v1.0.md`) marks the start of a model load distinct from its completion/residency — confirmed by an exhaustive repository search finding no lifecycle callback, provider hook, or loading-boundary signal anywhere.
- **`ModelResidencyState.Loading` is currently unreachable** (Finding F5). Retained per §22's naming; would become reachable only if a future WP adds a load-start signal.
- **No retrofit into `EOS.AIProvider`**. `get_model_residency` has no production consumer yet; this is consistent with the roadmap's four acceptance-bearing fields, none of which name this retrofit.
- **`request_background_slot()`'s Learning Engine callers do not yet exist** (Learning Engine, Milestone 6, not yet built) — this WP's own scope is satisfied entirely by `CompressionSweep`'s retrofit, matching the roadmap's own stub-then-retrofit precedent (WP-016→WP-020).

## Validation Steps

- `dotnet build` — 0 Warnings, 0 Errors (verified after every slice, including all 4 recovery slices).
- `dotnet format --verify-no-changes` — clean (verified after every slice).
- `EOS.ArchitectureTests` — 3/3, confirming `EOS.Resources`'s dependency row unchanged.
- Full solution regression — all suites green (one pre-existing, independently reconfirmed environmental Ollama cold-start flake under full-suite parallel load, unrelated to this WP).

## Test Strategy

- **Unit tests** (`EOS.Resources.Tests`): `QuotaManagerTests` (fairness, Starvation Prevention including the R1 fix, R2's event-separation), `BackgroundTaskControllerTests` (§15.1 algorithm, R1/R2 regression tests), `ModelResidencyTests` (R3's honest-null behavior, idle-timeout eviction), `CapacityManagerTests` (D6's exact transition conditions).
- **Integration test**: `BackgroundJobContentionIntegrationTests` — roadmap's "simulating CPU contention... deferred then later granted" criterion.
- **Regression tests added during Recovery** (permanent):
  - `RequestBackgroundSlot_StarvationOverride_GrantsDespiteSustainedCpuContention` (F1)
  - `RecordDenial_DoesNotPublishResourceQuotaExhausted` / `PublishQuotaExhausted_PublishesResourceQuotaExhausted` / `RequestBackgroundSlot_DoesNotPublishResourceQuotaExhausted_WhenDeferredForCpuLoad` / `RequestBackgroundSlot_PublishesResourceQuotaExhausted_WhenClassQuotaIsGenuinelyExhausted` (F2)
  - `GetModelResidency_ReportsNullFootprint_BecauseNoLegalMeasurementSourceExists` / `RecordInferenceRouted_PublishesModelLoaded_WithTheDisclosedUnmeasurableSentinel` (F4)
