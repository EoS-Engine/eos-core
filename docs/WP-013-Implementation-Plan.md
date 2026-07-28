# WP-013 Implementation Plan — Protection Layer: Governance Domains, Resource Ceilings & Emergency Shutdown

**Source of Truth (priority order):** `docs/EOS-Specification.md` (Part 0 §0.2/§0.6/§0.6.1/§0.12.1, Part 1 §1.2, Part 7 §7.1/§7.2), `docs/Protection-Layer-Specification-v1.0.md` (§11, §12.5/§12.6, §16, §26/§26.1), `docs/EOS-Implementation-Roadmap-v1.0.md` (WP-013 row), the approved WP-013 Architecture Review, Architecture Challenge (G1–G5 resolved), and the approved Implementation Plan/design refinements (this document).

## Objective (roadmap, verbatim)

Implement the eleven Protection Domains' specific logic, Resource Protection ceiling enforcement, and Emergency Shutdown.

## Architecture Decisions (frozen, as approved)

1. **G1 (resolved):** Per-domain logic for all eleven Protection Domains is satisfied at `ActionType`-string-routing granularity — no `ActionRequest` change. Reasoning (risk-based gating) and Configuration (Decision-Matrix routing) are already fully satisfied by WP-012's `RiskEngine`/`ApprovalEngine`. Learning/Planning have no real caller yet. AI Providers is structurally enforced by the AI Provider Layer's own registry. Resources is an explicit roadmap-sanctioned stub. Knowledge/Memory/Local Files/Projects/System Settings are demonstrated via representative `ActionType` conventions through `PolicyEngine`.
2. **G2 (resolved):** Resource ceiling stub values live entirely inside `EOS.Gates`-owned configuration (`ThresholdsOptions`), never read from `EOS.Resources`.
3. **G3 (resolved):** Emergency Shutdown activation/clearing is triggered by caller-asserted `ActionType` values (`"EmergencyShutdown"`/`"EmergencyShutdownCleared"`) — no Authority-Level verification, no identity system, matching the established `"HumanOperator"` trust-boundary precedent (WP-009).
4. **G4 (resolved):** `ThresholdsOptions` gains six additive resource-ceiling fields matching §16's table (CPU, RAM, Disk, Model Usage, Context Size, Background Tasks) — stub values, structurally present, not yet compared against any real requested amount.
5. **G5 (accepted, disclosed):** Emergency Shutdown's "Rule Conflict" trigger path (§26.1) has no real test this WP — `RuleEngine` remains a pass-through with zero configured rules, so a Rule Conflict cannot occur. Only the "human-authorized action requests it directly" trigger is implemented/tested.
6. **Emergency Shutdown is a stateful flag, not a fifth `PolicyEngine` tier** — §12.5 describes an indiscriminate on/off mode, categorically different from the four per-`ActionType` rule tiers.
7. **Post-plan design refinement:** `EmergencyShutdownState` owns the *entire* shutdown lifecycle, including recognition of its own control `ActionType`s and construction of the resulting `ValidationResult` — exposed as `ValidationResult? TryHandleControlAction(ActionRequest action)`. `ProtectionGate` contains no `"EmergencyShutdown"`/`"EmergencyShutdownCleared"` string literals anywhere; it only delegates: `var shutdownResult = emergencyShutdownState.TryHandleControlAction(action); if (shutdownResult is not null) { Log(...); return shutdownResult; }`. This keeps `ProtectionGate` responsible only for orchestration, tier dispatch, and logging.
8. **Activation and clearing both resolve to `ProtectionVerdict.Allow`** (the administrative action itself succeeds); every other action evaluated while active resolves to `ProtectionVerdict.Defer` (§26.1's literal verdict for held dispatch). No new `ProtectionVerdict` value needed.

## Scope Implemented

- `EmergencyShutdownState` (new) — lock-protected boolean state; owns `"EmergencyShutdown"`/`"EmergencyShutdownCleared"` recognition and the resulting `ValidationResult` construction entirely.
- `ProtectionGate.Validate()` — first line delegates to `emergencyShutdownState.TryHandleControlAction(action)`; if non-null, logs and returns it; otherwise proceeds through the unchanged tiered-dispatch pipeline (with `resourceCeilings` now structurally wired at the Resource Validation step).
- `ResourceCeilings` (new) — six-field immutable record, loaded from `ThresholdsOptions`, structurally present with zero enforcement logic (no requested-amount data exists on `ActionRequest` to compare against).
- `ThresholdsOptions`/`config/Thresholds.json` — six additive resource-ceiling fields.
- `PolicyEngine.cs` — one code-comment correction (no behavioral change): documents that Emergency Policies are represented by `EmergencyShutdownState`, not a fifth tier.
- Test coverage demonstrating all eleven Protection Domains are governable through existing `PolicyEngine`/`ApprovalEngine`/`RiskEngine` machinery, using representative `ActionType` conventions.

## Files Created

- `src/EOS.Gates/EmergencyShutdownState.cs`
- `src/EOS.Gates/ResourceCeilings.cs`
- `tests/EOS.Gates.Tests/EmergencyShutdownTests.cs`
- `tests/EOS.Gates.Tests/ResourceCeilingsTests.cs`
- `tests/EOS.Gates.Tests/ProtectionDomainPolicyTests.cs`
- `docs/WP-013-Implementation-Plan.md`

## Files Modified

- `src/EOS.Gates/ProtectionGate.cs` — delegates to `EmergencyShutdownState`; accepts `ResourceCeilings` (structurally wired, no enforcement logic); public shape (`IProtectionClient.Validate`) unchanged
- `src/EOS.Gates/PolicyEngine.cs` — comment only
- `src/EOS.SharedKernel/Configuration/ThresholdsOptions.cs` — additive resource-ceiling fields
- `config/Thresholds.json` — populated new fields
- `src/EOS.Runner/Program.cs` — additive: constructs `ResourceCeilings`/`EmergencyShutdownState`, wires into `ProtectionGate`
- `tests/EOS.Gates.Tests/ProtectionGateTests.cs` — `CreateGate` helper + two direct-construction sites extended for the two new constructor parameters
- `tests/EOS.Runner.Tests/AskCommandIntegrationTests.cs` — mechanical constructor-argument fix only (two call sites), no logic/assertion changed

## Files Not Modified (confirmed)

`src/EOS.Contracts/*` (all Protection types), `src/EOS.Gates/RuleEngine.cs` (behavior unchanged), `src/EOS.SharedKernel/Configuration/SecurityOptions.cs`, `src/EOS.Resources/**`, `src/EOS.Orchestrator/**`, `src/EOS.Knowledge/**`, `src/EOS.Reasoning/**`, `src/EOS.AIProvider/**`, `src/EOS.SDK/**`, `src/EOS.Runner/Bootstrap/JsonConfigurationLoader.cs`, `src/EOS.Runner/Commands/AskCommand.cs`.

## Dependency Changes

None. `EOS.Gates.csproj` unchanged. Zero `.csproj` files modified anywhere in the solution.

## Package Changes

None.

## Public Contract Changes

None. `IProtectionClient`, `ActionRequest`, `ValidationResult`, `ProtectionVerdict`, `RiskTier` all unchanged.

## Tests Added

16 new tests: 5 unit tests for `EmergencyShutdownState.TryHandleControlAction` (unrelated action passes through, activation, hold-at-Defer while active, clear-and-resume, case-insensitivity) + 1 end-to-end `ProtectionGate` integration test (activate → Defer at every tier → clear → resume); 2 for `ResourceCeilings` (field integrity, record equality); 9 domain-coverage tests in `ProtectionDomainPolicyTests` (`PolicyEngine`-governed domains via `[Theory]`, plus Reasoning via `RiskEngine` and Configuration via `ApprovalEngine`).

## Regression Strategy

Full sequential per-project `dotnet test` run across all existing suites (131 tests as of WP-012's closure) plus the new WP-013 tests.

## Acceptance Criteria (roadmap, verbatim)

"Triggering a simulated Emergency condition halts new action dispatch; clearing it (with justification) resumes normal operation." Satisfied by `EmergencyShutdownTests.ProtectionGate_EndToEnd_ActivateHoldsDispatch_ClearResumesNormalOperation`.

## Risks

- G5 (disclosed): the Rule-Conflict Emergency Shutdown trigger has no real test this WP.
- `resourceCeilings` is intentionally unused beyond structural presence in `ProtectionGate` (no requested-amount data exists to compare against) — a disclosed simplification, not a defect, per G1/G4.

## Rollback Strategy

Standard: revert the merge commit if a post-merge defect is found. No data migration, no schema change beyond additive JSON config fields, no persisted state (`EmergencyShutdownState` is in-memory, process-lifetime only).
