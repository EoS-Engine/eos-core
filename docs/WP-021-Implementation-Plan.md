# WP-021 Implementation Plan — Resource Management: Monitor & Capacity Planning

## Revision and Source of Truth

Revision 2 (FINAL). Revises Revision 1's Decision D1 (synchronous, not async, `IResourceManagementClient`) and Decision D3 (live per-`Validate()` measurement, not a one-time startup snapshot) following a review finding that the startup-snapshot design did not satisfy the roadmap's own "real, measured values" requirement on an ongoing basis. Adds an explicit Sampling Strategy section (§18 compliance). Built exclusively from: `docs/Resource-Management-Specification-v1.0.md` §10.1/§10.1a (context only), §10.2, §10.3, §17, §18, §8 (FR-RM1–FR-RM10, cross-referenced directly from the in-scope sections), §9 (NFRs, same basis), §20 (Events — cross-referenced from FR-RM6, and by the Roadmap's own Traceability Matrix assignment); `docs/EOS-Implementation-Roadmap-v1.0.md` WP-021 row and Traceability Matrix; `docs/EOS-Specification.md` Part 1 §1.2 (dependency table rows for `EOS.Resources`, `EOS.Gates`, `EOS.Orchestrator`, `EOS.AIProvider`, `EOS.Runner`) and Part 7 §7.2 (Scheduler budget structure names, cross-referenced from §10.1a/§10.3); the approved Phase 1 and Phase 2 reports. No other document was used as evidence.

## Current Repository Baseline

- `src/EOS.Resources/EOS.Resources.csproj` — project skeleton exists, references `EOS.Contracts`, `EOS.SDK`. Zero `.cs` files.
- `src/EOS.Contracts/` — no `IResourceManagementClient`, resource-type, or capacity-tier type exists.
- `src/EOS.Gates/ResourceCeilings.cs` — existing record (`CpuCeilingPercent`, `RamCeilingMegabytes`, `DiskCeilingMegabytes`, `ModelUsageCeilingTokens`, `ContextSizeCeilingTokens`, `BackgroundTasksCeilingCount`), doc-commented as "stub values only... Real enforcement awaits Resource Management (WP-021)."
- `src/EOS.Gates/ProtectionGate.cs` — constructor already accepts `ResourceCeilings resourceCeilings`; body contains `_ = resourceCeilings;` (intentionally discarded) with a comment naming WP-021 as the point it becomes real.
- `src/EOS.SharedKernel/Configuration/ThresholdsOptions.cs` / `config/Thresholds.json` — six existing ceiling fields (single value per dimension; no Warning/Critical/Emergency gradient; no Queue Length or Cache Usage field at all).
- `src/EOS.Runner/Program.cs` — constructs `ResourceCeilings` from `thresholdsOptions` and passes it into `ProtectionGate`.
- No `tests/EOS.Resources.Tests/` project exists.
- Branch: `wp-021-resource-management-monitor-capacity-planning`, base commit is post-WP-020 `main` (tag `wp-020-complete`), clean working tree.

## Objective and Exact Roadmap Scope

**Objective (verbatim):** "Implement real CPU/RAM/Disk/Model/Queue/Background/Cache measurement and the Safe/Warning/Critical/Emergency threshold computation."

**Included components (verbatim):** "Sampling-based measurement for all seven monitored dimensions; the four-tier threshold computation; `IResourceManagementClient.get_current_budget()`/`get_current_tier()`."

**Explicitly excluded (verbatim):** "Quota Manager and Background Task Controller (WP-022); Model Residency Management (WP-022); Allocation Manager's GPU-extensibility registry (built here structurally per ADR-RM003, but no GPU type is registered — none exists on the target hardware)."

**Expected deliverables (verbatim):** "`get_current_budget()` returns real, measured values; Protection's Resource Protection (WP-013) is updated to consume these instead of its stub."

**Test verification (verbatim):** "Unit tests for threshold computation; an integration test recording real measurements against the running Docker Compose stack and comparing them to the Infrastructure Roadmap's Phase 3/5 baseline figures"

**Demo / acceptance criteria (verbatim):** "`get_current_budget(CPU)` returns a value derived from a live measurement, not a hardcoded constant; Protection's ceiling check now cites this real value in its logs"

## Final Architecture

```
EOS.Resources (new real code)
 ├── ResourceMonitor        — §18: samples 7 dimensions (CPU, RAM, Disk, Model Usage,
 │                             Queue Length, Background Tasks, Cache Usage)
 ├── CapacityManager        — §17: computes Safe/Warning/Critical/Emergency per dimension
 │                             from ResourceMonitor's sample + Thresholds.json boundaries
 └── ResourceManagementClient (implements IResourceManagementClient, EOS.Contracts)
       — composes Monitor + Capacity Manager; the one public entry point

EOS.Contracts (additive)
 ├── IResourceManagementClient   — public interface, multi-consumer (mirrors
 │                                  IProtectionClient/IReasoningEngineClient precedent)
 ├── ResourceType (enum)         — CPU, RAM, Disk, ModelUsage, QueueLength, BackgroundTasks,
 │                                  CacheUsage (§18.2's exact 7 rows)
 └── CapacityTier (enum)         — Safe, Warning, Critical, Emergency (§17.1–§17.4)

EOS.Resources (Composition Root Adapter interface, ADR-015-001 pattern)
 └── IResourceThresholdCrossedEventPublisher — mirrors IDecisionMadeEventPublisher (WP-019)/
                                                 IContextAssemblyEventPublisher (WP-015) precedent

EOS.Runner/Program.cs (composition root, unchanged dependency posture — already "Everything")
 ├── constructs the real ResourceManagementClient
 └── constructs the EventMediator-backed IResourceThresholdCrossedEventPublisher adapter

EOS.Gates (modified, no new project dependency — IResourceManagementClient lives in
            EOS.Contracts, which EOS.Gates already depends on)
 ├── ProtectionGate.cs   — now also takes IResourceManagementClient; calls it live, once per
 │                          Validate() invocation, and logs the real returned value alongside
 │                          the existing static ResourceCeilings (mechanism: Decision D3, below)
 └── ResourceCeilings.cs — unchanged shape; doc comment updated (still the §19.3 Limits record)
```

No project gains a dependency it does not already have. `EOS.Gates`, `EOS.Orchestrator`, `EOS.AIProvider` each already depend on `EOS.Contracts` (Constitution Part 1 §1.2), which is where the only new public interface lives.

## Finalized Implementation Decisions

Each decision below follows the Repository Evidence / Specification Evidence / Selected Alternative format established in WP-019's Implementation Plan.

---

### Decision D1 — `IResourceManagementClient` location and shape (revised)

**Repository Evidence:** `IProtectionClient` (`EOS.Contracts`) and `IReasoningEngineClient` (`EOS.Contracts`) are both multi-consumer public interfaces resident in `EOS.Contracts`, implemented by a concrete class in the owning project (`EOS.Gates`, `EOS.Reasoning`). **Revision 2 correction:** `IProtectionClient.Validate(ActionRequest action)` — the interface `ProtectionGate` itself implements, and the direct consumer of Resource Management's output — is synchronous: no `Task`, no `CancellationToken`. `IReasoningEngineClient`'s async shape exists because it performs genuine remote I/O (AI Provider network calls); local resource measurement (CPU/RAM/Disk read via OS-level queries) is not equivalent I/O.

**Specification Evidence:** Constitution Part 1 §1.2: `EOS.Gates` → "EOS.Contracts, EOS.Domain (read)"; `EOS.Orchestrator` → "EOS.Contracts, EOS.Application"; `EOS.AIProvider` → "EOS.Contracts, EOS.SDK" — none lists `EOS.Resources`. §10.1's component diagram shows all three as consumers of Resource Management's "published budget values/signals." §18.1: measurement is "sampled," never "continuously instrumented" — a fast, bounded, local operation, not a blocking remote call.

**Decision (revised):** `IResourceManagementClient` is declared in `EOS.Contracts`, synchronous: `double GetCurrentBudget(ResourceType resourceType)` and `CapacityTier GetCurrentTier(ResourceType resourceType)` — matching `IProtectionClient`'s own synchronous shape, since `ProtectionGate` (the one identified consumer this WP wires) cannot `await` inside its synchronous `Validate()` method without either blocking on async work (an anti-pattern) or changing `IProtectionClient`'s own signature (a public-contract change the roadmap does not authorize, per original Decision D3). Concrete implementation (`ResourceManagementClient`) lives in `EOS.Resources`.

**KISS/YAGNI:** Exactly the two roadmap-named methods; no `get_model_residency`/`request_background_slot` (WP-022's own methods, per the Traceability Matrix) added ahead of need. Synchronous shape is simpler than async here — no background thread, no task scheduling, no cancellation plumbing for a fast local read.

---

### Decision D2 — `ResourceType` and `CapacityTier` enum values

**Specification Evidence:** §18.2's Monitored Dimensions table names exactly: CPU, RAM, Disk, Model Usage, Queue Length, Background Tasks, Cache Usage. §17.1–§17.4 name exactly: Safe, Warning, Critical, Emergency.

**Decision:** `ResourceType { Cpu, Ram, Disk, ModelUsage, QueueLength, BackgroundTasks, CacheUsage }` and `CapacityTier { Safe, Warning, Critical, Emergency }` — both in `EOS.Contracts`, values transcribed verbatim from the two cited tables, no additions.

---

### Decision D3 — `ProtectionGate`/`ResourceCeilings` consumption mechanism (revised)

**Why Revision 1 was wrong:** Revision 1 had `Program.cs` call `GetCurrentBudgetAsync` once at startup and freeze the result into `ResourceCeilings` for the process lifetime. This does not satisfy FR-RM2 ("Every computed budget value... MUST be derived from real, measured system state (§18), **never a static hardcoded guess**") on an ongoing basis — after the first sample, the value is exactly as static as WP-013's original stub, just with one real number baked in at boot instead of a constant from `Thresholds.json`. It also does not satisfy the roadmap's Demo criterion "Protection's ceiling check **now cites this real value** in its logs" in the sense the criterion intends: a value frozen at process start is not "real" at the time of each subsequent check.

**Repository Evidence:** `ProtectionGate`'s constructor already accepts plain, non-interface dependencies (`ResourceCeilings`, `EmergencyShutdownState`, etc.) constructed once by `Program.cs` — but `IProtectionClient.Validate(ActionRequest action)` remains synchronous (unchanged, not authorized to change by this WP).

**Specification Evidence:** §19.3 (Limits) remains distinct from §17 (Capacity Planning thresholds) — this distinction is preserved. Objective (verbatim): "Implement real CPU/RAM/Disk/Model/Queue/Background/Cache **measurement**" — describes an ongoing capability, not a one-time boot-time action.

**Decision (revised):** `ProtectionGate`'s constructor gains one new parameter: `IResourceManagementClient resourceManagementClient` (an `EOS.Contracts` interface — no new project dependency, since `EOS.Gates` already depends on `EOS.Contracts`). `ResourceCeilings`'s shape and role are otherwise unchanged (still the static §19.3 Limits record, still just structurally wired — no `ActionRequest`-vs-ceiling comparison logic is added, since no `ActionRequest` field carries a requested resource amount yet, an already-documented WP-013 gap this WP is not asked to close). Inside `Validate()`, at the existing logging step, `ProtectionGate` now also calls `resourceManagementClient.GetCurrentBudget(ResourceType.Cpu)` (synchronously, per Decision D1) and includes the result in its log output — a genuinely live measurement on every single `Validate()` call, not a value frozen at startup. `Program.cs` still constructs `ResourceManagementClient` once (as it does every other dependency) and passes the *client*, not a pre-computed value, into `ProtectionGate`.

**KISS/YAGNI:** No `IProtectionClient` signature change. No background timer, no hosted service, no thread management — the synchronous interface (Decision D1) makes the live call trivial to add at the exact point the log statement already exists.

---

### Decision D4 — `ResourceThresholdCrossed` event, in scope per explicit instruction

**Specification Evidence:** §20's Events table: "`ResourceThresholdCrossed` *(new)* | Producer: Capacity Manager (§17) | Consumers: Background Task Controller, Health Monitor, Dashboard | Payload: resource_type, tier (Safe/Warning/Critical/Emergency)." Roadmap Traceability Matrix: "All Resource Management events | Events | WP-021 (`ResourceThresholdCrossed`), WP-022 (remainder)."

**Decision:** Implemented. `CapacityManager` publishes `ResourceThresholdCrossed(resource_type, tier)` whenever a dimension's computed tier changes, via a Composition Root Adapter (`IResourceThresholdCrossedEventPublisher`, `EOS.Resources`), matching the established pattern (`IDecisionMadeEventPublisher`, WP-019; `IContextAssemblyEventPublisher`, WP-015). No other Resource Management event (`BackgroundJobGranted`, `ModelLoaded`, `ResourceQuotaExhausted`, `EmergencyCapacitySignal`, `ResourceRecovered`) is implemented — each belongs to a component this WP explicitly excludes (Quota Manager, Background Task Controller, Model Residency — all WP-022), except `EmergencyCapacitySignal`, whose producer (Capacity Manager) is in-scope but whose event is not named for WP-021 by the Traceability Matrix ("WP-022 (remainder)") — excluded per that explicit assignment, not omitted by oversight.

**KISS/YAGNI:** Exactly the one event the Traceability Matrix assigns to this WP.

---

### Decision D5 — `Thresholds.json` field shape for the four-tier gradient

**Specification Evidence:** §17.5: "All four tiers, per resource type, are defined in `Thresholds.json`." — explicit per-resource-type granularity, ruling out one shared set of boundary values across all seven dimensions.

**Decision:** Seven new triplets (Warning/Critical/Emergency boundary per dimension; Safe is implicitly "below Warning," consistent with §17.1's "baseline range... may draw allocation freely" framing rather than a fourth explicit number) — 21 new fields total, following the existing `ThresholdsOptions` naming convention (e.g., `Cpu` + tier name + unit suffix, mirroring `CpuCeilingPercent`'s own pattern). Exact field names are finalized at implementation time (Slice 1), not enumerated here, per "do not generate production code."

**KISS/YAGNI:** No fields added for `EmergencyCapacitySignal` or any WP-022-owned concept.

---

## Sampling Strategy (§18 compliance detail)

**How sampling is triggered:** On-demand (pull-based), not a background timer. `ResourceMonitor` takes no action until `ResourceManagementClient.GetCurrentBudget`/`GetCurrentTier` is called by a consumer (`ProtectionGate`, or the integration test). No new hosted service, background thread, or scheduler is introduced — repository fact confirms none exists anywhere in this codebase today, and none is required here.

**Whether measurements are cached:** Yes. `ResourceMonitor` holds, in memory, the last-sampled value and timestamp per `ResourceType`.

**Refresh behavior:** On each call, `ResourceMonitor` compares elapsed time since that dimension's last sample against a new configurable interval (`ResourceSamplingIntervalSeconds`, `Thresholds.json`). If the elapsed time is at or beyond the interval, a fresh OS-level measurement is taken and the cache is updated; otherwise the cached value is returned unchanged. This throttle is per-dimension, independent of how often any consumer calls it.

**Why this satisfies §18.1's Sampling Model while respecting the Non-Bottleneck NFR:**
- §18.1 (verbatim): "All monitored dimensions below are sampled at a bounded cadence (configurable, `Thresholds.json`), never continuously instrumented in a way that would itself compete for the CPU it measures." The elapsed-time throttle *is* the bounded, configurable cadence this sentence names — the measurement rate is capped regardless of caller frequency (e.g., if `ProtectionGate.Validate()` is called many times per second, real OS measurement still only happens at most once per configured interval).
- This is strictly more conservative than a free-running background timer: a timer samples on its own schedule even when nothing is consuming the value; this design samples only when something is asking, and never more often than the configured bound. Zero sampling cost when the system is idle.
- FR-RM2 ("never a static hardcoded guess") remains satisfied: within the bounded interval, every returned value was measured from real system state, not a startup-only snapshot or a `Thresholds.json` constant.

## Vertical Slice Definition

`ProtectionGate.Validate() → resourceManagementClient.GetCurrentBudget(Cpu) (live, synchronous, per-call) → ResourceManagementClient → CapacityManager (§17 tier computation) → ResourceMonitor (§18 real sampling, lazily refreshed per Sampling Strategy below) → logged real value` — a real, callable, testable path from live measurement to Protection's log output, re-executed on every `Validate()` call, not once at process start.

## Scope

**Included:** Real sampling for all 7 dimensions (§18); Safe/Warning/Critical/Emergency computation (§17); `IResourceManagementClient.GetCurrentBudget`/`GetCurrentTier`; `ResourceThresholdCrossed` event; `Program.cs`/`ProtectionGate.cs` retrofit.

**Explicitly Excluded** (owning WP named): Quota Manager, Background Task Controller (WP-022); Model Residency Management (WP-022); Allocation Manager's GPU-extensibility registry — built structurally (ADR-RM003) but no GPU type registered (no future WP claims this — no GPU exists on target hardware); Scheduler (`EOS.Orchestrator`)/AI Provider Layer consumption of published values — not named as a WP-021 deliverable, no owning WP identified in the reviewed documents (a documented, not silently dropped, boundary); `EmergencyCapacitySignal`, `BackgroundJobGranted`/`Deferred`, `ModelLoaded`/`Unloaded`, `ResourceQuotaExhausted`, `ResourceRecovered` events — WP-022 ("remainder," Traceability Matrix).

## Projects Affected

`EOS.Resources` (primary, first real code), `EOS.Contracts` (additive), `EOS.Gates` (retrofit), `EOS.Runner` (composition root wiring), `EOS.SharedKernel` + `config/Thresholds.json` (additive configuration).

## Files to Create

- `src/EOS.Contracts/IResourceManagementClient.cs`
- `src/EOS.Contracts/ResourceType.cs`
- `src/EOS.Contracts/CapacityTier.cs`
- `src/EOS.Resources/ResourceMonitor.cs`
- `src/EOS.Resources/CapacityManager.cs`
- `src/EOS.Resources/ResourceManagementClient.cs`
- `src/EOS.Resources/IResourceThresholdCrossedEventPublisher.cs`
- `tests/EOS.Resources.Tests/EOS.Resources.Tests.csproj` (new test project) + unit test file(s) for `CapacityManager`
- `tests/EOS.Resources.Tests/ResourceManagementClientIntegrationTests.cs` (real Docker Compose stack)

## Files to Modify

- `src/EOS.Gates/ProtectionGate.cs` — constructor gains `IResourceManagementClient`; remove the discard, call it live and log the real value at each `Validate()` (Decision D3).
- `src/EOS.Gates/ResourceCeilings.cs` — doc comment only (no shape change, per Decision D3).
- `src/EOS.Runner/Program.cs` — construct `ResourceManagementClient` and the event publisher adapter; pass the client into `ProtectionGate` (no per-startup snapshot).
- `src/EOS.SharedKernel/Configuration/ThresholdsOptions.cs` / `config/Thresholds.json` — add the 21 new tier-boundary fields (Decision D5) plus `ResourceSamplingIntervalSeconds` (Sampling Strategy).

## Files That Must Not Change

Any file under `src/EOS.Knowledge/`, `src/EOS.KnowledgeGraph/`, `src/EOS.Reasoning/`, `src/EOS.Learning/`, `src/EOS.Planner/`, `src/EOS.Orchestrator/`, `src/EOS.AIProvider/`, `src/EOS.Contracts/IProtectionClient.cs`, `ActionRequest`, `ValidationResult`.

## Dependency Changes and Package Changes

None. No new project reference; no new NuGet package.

## Configuration Changes

21 new tier-boundary fields in `ThresholdsOptions`/`Thresholds.json` (Decision D5), plus one new `ResourceSamplingIntervalSeconds` field (Sampling Strategy, §18.1's "bounded cadence, configurable"), each `[Range]`-annotated per the existing pattern, validated at bootstrap via `JsonConfigurationLoader.Validate` (unmodified mechanism).

## Test Strategy

**Unit tests:** `CapacityManager`'s tier computation (Safe/Warning/Critical/Emergency boundaries) per dimension, per the roadmap's own "Test verification" field.

**Integration tests:** `ResourceMonitor` recording real measurements against the running Docker Compose stack, compared to the Infrastructure Roadmap's Phase 3/5 baseline figures — new real-infrastructure test, no additional service beyond what already runs (SQL Server/Redis/ChromaDB are already required by other suites; this test needs only the host machine's own CPU/RAM/Disk, not a new container).

**Real services required:** none beyond the host OS itself for measurement.

## Acceptance Criteria Mapping

| Roadmap Criterion (verbatim) | Satisfied By |
|---|---|
| "`get_current_budget(CPU)` returns a value derived from a live measurement, not a hardcoded constant" | `ResourceMonitor` real sampling (throttled per Sampling Strategy) → `ResourceManagementClient.GetCurrentBudget(ResourceType.Cpu)` |
| "Protection's ceiling check now cites this real value in its logs" | `ProtectionGate.Validate()` calls `resourceManagementClient.GetCurrentBudget(ResourceType.Cpu)` live, on every invocation, and logs the result (Decision D3, revised) |

## Verification Checklist

- [ ] `dotnet build` clean
- [ ] `dotnet format --verify-no-changes` clean
- [ ] All existing tests still pass
- [ ] New unit tests (`CapacityManager`) pass
- [ ] New integration test (real measurement) passes
- [ ] `EOS.ArchitectureTests` passes (no dependency violations introduced)
- [ ] Both roadmap Acceptance Criteria demonstrated

## Definition of Done

Per Development-Workflow.md §14: implementation matches this approved plan exactly; all tests pass; `dotnet build`/`dotnet format` clean; Architecture Gate passed with no unresolved finding; real CodeRabbit review completed and every finding classified/resolved; documentation (this plan + completion report) reflects the final implementation; PR merged normally into `main`; annotated tag created; closure report written; working tree/branch state clean; no scope beyond this plan implemented.

## Rollback Strategy

Standard PR-level rollback: since WP-021 introduces only additive files, one modified `EOS.Gates` file (comment + log statement), one modified `Program.cs` wiring point, and additive configuration, reverting the merge commit fully restores WP-020's `main` state with no data migration, no schema change, and no persisted state to unwind (`EOS.Resources` performs no writes — measurement/publication only, FR-RM1).

## Risks

| Risk | Mitigation/Boundary |
|---|---|
| §9 Non-Bottleneck NFR: measurement itself must not compete with what it measures | Lazy, elapsed-time-throttled sampling (Sampling Strategy) — real measurement happens at most once per configured interval regardless of caller frequency; zero cost when idle |
| `ProtectionGate.Validate()` now performs a live measurement call on every invocation — could this itself become a bottleneck if called at high frequency? | Mitigated by the same throttle: repeated calls within the configured interval return the cached value, not a fresh OS query — verified directly in Slice 2/4 |
| FR-RM3: Emergency threshold must never be zero-headroom | `CapacityManager`'s Emergency boundary validated against this constraint in unit tests |
| Scheduler/AI Provider Layer consumption left unassigned (no owning WP found in reviewed documents) | Documented here as an observed boundary, not silently dropped; not this WP's responsibility to resolve |
| 21 new configuration fields plus the sampling interval | Bounded, explicit, `[Range]`-validated at bootstrap — no open-ended schema |

## KISS/YAGNI Justification

Every new type (`IResourceManagementClient`, `ResourceType`, `CapacityTier`, `IResourceThresholdCrossedEventPublisher`) has a named, current consumer within this WP's own scope (Decisions D1–D2, D4). No WP-022-owned capability (Quota Manager, Background Task Controller, Model Residency, `get_model_residency`/`request_background_slot`) is introduced ahead of need. `ResourceCeilings`'s shape is deliberately left unchanged (Decision D3) rather than redesigned, minimizing the footprint of the Protection retrofit to exactly what the roadmap's Expected Deliverable requires. The synchronous interface shape (Decision D1, revised) and lazy/throttled sampling (Sampling Strategy) together avoid introducing any background timer, hosted service, or thread-management code — none of which exists anywhere in this codebase today and none of which is required to satisfy any reviewed document.

---

## Implementation Plan Status

**Status:** FINAL

**Planning Complete:** YES

**Architecture Questions Remaining:** NO

**Implementation Ready:** YES

**Phase 4 Authorized:** Pending your explicit approval
