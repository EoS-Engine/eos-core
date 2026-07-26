# WP-006 Implementation Plan — Protection Layer: Minimal Validation Gate

**Revision:** 1 (Final, Approved)
**Source of Truth:** `docs/EOS-Implementation-Roadmap-v1.0.md` (WP-006 row), `docs/Protection-Layer-Specification-v1.0.md` §13.1/§14.1/§20/§23.1/§26, `docs/EOS-Specification.md` Part 1 §1.2 (`EOS.Gates` dependency declaration), `docs/Development-Workflow.md`.

## Objective (roadmap, verbatim)

Implement `IProtectionClient.validate()` with real tiering logic (Low/Medium/High) but a deliberately conservative, always-safe policy set — real structure, minimal policy content.

## Final Architecture Decisions

### Decision 1 — Contract placement: `EOS.Contracts`

`IProtectionClient`, `ActionRequest`, `ValidationResult`, `ProtectionVerdict`, `RiskTier` live in `EOS.Contracts`. Constitution Part 1 §1.2 declares `EOS.Gates`'s dependency shape as `EOS.Contracts, EOS.Domain (read)` — `EOS.SDK` is not listed, unlike `EOS.AIProvider`/`EOS.Reasoning`/`EOS.Learning`. `EOS.Planner` (a declared future Protection consumer) has no `EOS.SDK` dependency at all, so `EOS.SDK` would not be reachable from every future consumer. `EOS.Contracts` is Constitution §1.3's declared "only cross-role dependency surface" and is already a dependency of every future consumer (`EOS.Reasoning`, `EOS.Learning`, `EOS.Knowledge`, `EOS.Planner`).

### Decision 2 — `EOS.Gates.csproj`: drop `EOS.Domain`, keep `EOS.Contracts`

`EOS.Domain` has zero source files — nothing for a hardcoded-conservative tiering algorithm to consume. Constitution's dependency table is a ceiling, not a floor (precedent: WP-005 removed `EOS.AIProvider`'s unused `EOS.Contracts` reference under the same reasoning).

### Decision 3 — Medium and High tier default verdict: Deny (fail-closed)

Both Medium and High return `Deny` in WP-006; only Low returns `Allow`. §14.1 requires Medium to run a permission/resource-budget quick-check and High to run the full six-step pipeline — both depend entirely on components explicitly excluded (Policy/Rule/Risk/Approval Engines). §26's Failure Handling states: "Policy Failure... Fail-closed... never fail-open to unrestricted allow." Fabricating a passing check for infrastructure that doesn't exist would be fail-open, which the specification forbids. Low tier is structurally different — §13.1 defines it as "lightweight async validation only — logged, not blocking," i.e. it never gates by design.

### Decision 4 — `IProtectionClient` surface: `validate()` only

`check_approval()`/`report_outcome()` are omitted — their backing (Approval Engine, Trust Evaluation) is entirely excluded, and no verdict this WP produces is ever `Defer`, so `check_approval()` would have nothing to poll. Direct precedent: WP-005 omitted `discover_capabilities()` from `IAIProviderClient` for the identical reason.

### Decision 5 — Architecture fitness test: whitelist pattern

`OnlyAllowedProjectsMayReferenceEOSGatesTests` whitelists `EOS.Gates`, `EOS.Gates.Tests`, `EOS.ArchitectureTests`, plus the three pre-existing, Constitution-declared dependents already scaffolded since WP-001 (`EOS.PrincipalEngineer`, `EOS.QA`, `EOS.Pipeline` — Constitution Part 1 §1.2 lists `EOS.Gates` in each of their `Depends On` columns) — directly mirroring WP-005's `OnlyAllowedProjectsMayReferenceAIProviderTests` pattern, extended to reflect the repository's actual current state rather than a stricter rule than the Constitution itself declares.

### Decision 6 — Risk-tier thresholds: hardcoded constants

`0–30 = Low`, `31–70 = Medium`, `71–100 = High` as named constants in code. §13.1 states these are "reused verbatim" from Constitution §0.6.1 — a Constitutional constant, not tunable policy. `Thresholds.json` is not modified; no consumer of a new config field exists in this WP's scope.

### Decision 7 — Contract shapes

```csharp
public sealed record ActionRequest(Guid ActionId, string ActionType, string Actor, int RiskScore);
public sealed record ValidationResult(ProtectionVerdict Verdict, RiskTier Tier, string? Reason);
public enum ProtectionVerdict { Allow, Deny, Defer, Retry }
public enum RiskTier { Low, Medium, High }
```

`RiskScore` is supplied by the caller — §10.5 states that where an acting subsystem has already computed its own `risk_score`, the Risk Engine consumes it directly rather than recomputing; WP-006 has no Risk Engine, so computing one is out of scope. `ProtectionVerdict` models all four verdicts the roadmap names ("the four verdicts"), even though this WP's logic only ever produces two.

### Decision 8 — `Validate()` is synchronous

`ProtectionVerdict`-bearing `ValidationResult Validate(ActionRequest action)` — no `Task`, no `CancellationToken`. Nothing in this WP's scope performs real asynchronous work; an `async`/`Task` signature with nothing to await would be needless ceremony (KISS outranks blind convention-consistency per the stated decision priority).

## Included Scope (roadmap, verbatim)

The tiering algorithm (§14.1) with a minimal, hardcoded-conservative rule set; the four verdicts; structural wiring so every risk-bearing call in later WPs routes through this interface (§10.9).

## Explicitly Excluded Scope (roadmap, verbatim)

Policy Engine, Rule Engine, Risk Engine, Approval Engine, Trust Evaluation, Safety Gates, Governance Layer (all full implementations deferred to WP-012/WP-013); Emergency Shutdown (WP-013); all eleven Protection Domains beyond a generic pass-through (WP-013).

## Vertical Slice Definition

`ActionRequest` (`EOS.Contracts`) → `IProtectionClient.Validate()` → `ProtectionGate` (`EOS.Gates`) → tier computed from `RiskScore` against hardcoded thresholds → `ValidationResult` returned, with the decision logged via `ILogger<ProtectionGate>`. Proven by unit tests confirming Low-tier routing yields Allow and a deliberately high-risk test action is not auto-allowed (roadmap's acceptance criterion).

## Projects Affected

`EOS.Contracts` (new contract types), `EOS.Gates` (new implementation).

## Files to Create

- `src/EOS.Contracts/IProtectionClient.cs`, `ActionRequest.cs`, `ValidationResult.cs`, `ProtectionVerdict.cs`, `RiskTier.cs`
- `src/EOS.Gates/ProtectionGate.cs`
- `tests/EOS.Gates.Tests/EOS.Gates.Tests.csproj`, `ProtectionGateTests.cs`
- `tests/EOS.ArchitectureTests/OnlyAllowedProjectsMayReferenceEOSGatesTests.cs`
- `docs/work-packages/WP-006-Completion-Report.md` (at closure)

## Files to Modify

- `src/EOS.Gates/EOS.Gates.csproj` — remove unused `EOS.Domain` reference; add `Microsoft.Extensions.Logging.Abstractions`.
- `EOS.slnx` — register `tests/EOS.Gates.Tests`.

## Files That Must NOT Change

`src/EOS.Runner/**`, `src/EOS.Reasoning/**`, `src/EOS.Knowledge/**`, `src/EOS.Learning/**`, `src/EOS.Planner/**`, `config/*.json`, `src/EOS.SharedKernel/Configuration/**`, `src/EOS.SDK/**`, `src/EOS.Domain/**`, any specification/roadmap document.

## Dependency Changes

- `EOS.Gates → EOS.Contracts` (kept, now genuinely consumed).
- `EOS.Gates → EOS.Domain` (removed, unused).
- No project gains a reference to `EOS.Gates` other than `EOS.Gates.Tests`/`EOS.ArchitectureTests`.

## Package Changes

`Microsoft.Extensions.Logging.Abstractions` added to `EOS.Gates.csproj` — the minimal logging abstraction already used elsewhere in the solution (`EOS.Runner`, `EOS.Runner.Tests`).

## Test Strategy

Unit only (no external service involved): Low→Allow, Medium→Deny, High→Deny (roadmap-named "high-risk test action not auto-allowed" test), boundary tests at 30/31/70/71, denied actions carry a `Reason` (FR-P3), a logging test, and the architecture fitness test.

## Acceptance Criteria (roadmap, verbatim)

The vertical slice's request visibly passes through `validate()` (via log output) and receives Allow.

## Definition of Done

Per `docs/Development-Workflow.md` §14 in full.

## Future WP Boundaries

WP-012 (full Policy/Rule/Risk/Approval Engines, replacing this WP's hardcoded path), WP-013 (Emergency Shutdown, eleven Protection Domains), WP-007/WP-008 (first real consumers of `IProtectionClient`).

## Proposed Implementation Sequence

1. Feature branch `wp-006-protection-minimal-gate` (created).
2. This plan document.
3. `EOS.Contracts` types.
4. `ProtectionGate` in `EOS.Gates`; update `EOS.Gates.csproj`.
5. `EOS.Gates.Tests`.
6. `OnlyAllowedProjectsMayReferenceEOSGatesTests`.
7. Add test project to `EOS.slnx`.
8. Full Local Verification.
9. Architecture Gate self-review.
10. Push, PR, real CodeRabbit review, fix VALID findings only.
11. Wait for explicit approval before merge/tag/closure.
