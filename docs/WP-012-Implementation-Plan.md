# WP-012 Implementation Plan — Protection Layer: Policy, Rule, Risk & Approval Engines

**Source of Truth (priority order):** `docs/EOS-Specification.md` (Part 0 §0.2/§0.6/§0.6.1/§0.8, Part 2 §2.1-2.3), `docs/Protection-Layer-Specification-v1.0.md` (§10-§20), `docs/EOS-Implementation-Roadmap-v1.0.md` (WP-012 row), the approved WP-012 Architecture Freeze (G1/G2/G3 resolved), this plan.

## Objective (roadmap, verbatim)

Replace WP-006's minimal tiering with the full Policy Engine, Rule Engine, Risk Engine, and Approval Engine.

## Architecture Decisions (frozen, unmodified)

- **G1 (resolved):** `RiskEngine` consumes `ActionRequest.RiskScore` only, never recomputes it (FR-P6). No `EOS.Gates → EOS.Knowledge` dependency — none was added.
- **G2 (resolved):** `SecurityOptions` extended additively with `GlobalPolicies`/`ProjectPolicies`/`UserPolicies`/`RuntimePolicies` (each `IReadOnlyList<PolicyEntry>`); `config/Security.json` populated with empty lists (a policy engine with zero configured policies is vacuously permissive — honest, deterministic default, FR-P7). Same `JsonConfigurationLoader`/`Validate<T>` mechanism, no new configuration system.
- **G3 (resolved):** No `EOS.Contracts` change. `ActionRequest.ActionType`'s existing free-string shape carries Decision Matrix routing via an internal `EOS.Gates` lookup table (`ApprovalEngine`); `ValidationResult.Reason`'s existing free-string field satisfies FR-P3's "structured reason" requirement.

## Discovered During Implementation (resolved without violating any stop condition)

`EOS.Gates` does not reference `EOS.SharedKernel` (Constitution's declared dependency is `EOS.Contracts, EOS.Domain` only) — `PolicyEngine` cannot take `SecurityOptions` directly without introducing a new project dependency, which is explicitly forbidden. Resolved via the established WP-004 (`DataStoreConnectionOptions`)/WP-010 (`IProviderEventLogger`) pattern: `EOS.Gates` defines its own plain `PolicyEntry` record; `Program.cs` (composition root, which already references both `EOS.SharedKernel.Configuration` and `EOS.Gates`) translates `SecurityOptions`'s policy lists into `EOS.Gates.PolicyEntry` instances at construction time. This introduces no new dependency, no new contract, no new configuration mechanism — it is the same pattern already used twice in this codebase for exactly this situation.

## Scope Implemented

Four new internal `EOS.Gates` classes matching §10.1's own architecture diagram:
- **`PolicyEngine`** — Global/Project/User/Runtime precedence (§12.6; Emergency deferred to WP-013), deterministic (FR-P7), wildcard (`"*"`) support.
- **`RuleEngine`** — pass-through this WP (see WP-012 Architecture Freeze §1/§4: no real caller sends a Fitness-Rule-checkable `ActionType`; `EOS.ArchitectureTests` has no production-callable form; building a bridge would introduce a hidden dependency). Structurally present, ready for real rule definitions.
- **`RiskEngine`** — consumes `RiskScore` (FR-P6), classifies tier per §13.1's exact boundaries (0-30 Low / 31-70 Medium / 71-100 High, reusing Constitution §0.6.1's 70/71 boundary verbatim), and implements §13.5's "two consecutive Medium-tier denials per actor/action-type escalates to High" rule via an in-memory `Dictionary` + `lock`, reusing `HealthMonitor`'s (WP-010) exact state-tracking shape. Folds Trust Evaluation's (§10.6) narrow "actor's track record" concern into this same escalation state, per the roadmap's explicit instruction not to build it as a separate class.
- **`ApprovalEngine`** — executes Constitution §0.6's Decision Matrix mechanically via an internal `ActionType` → Human-Required-row lookup (`Constitutional amendment`, `Security-sensitive change`, `Disaster recovery invocation`); everything else resolves to `Allow` (no consensus-role machinery exists in this codebase, and none is roadmap-required this WP).

`ProtectionGate` becomes the §14.1 tiered-dispatch orchestrator: Low → async-log/Allow (never blocks, FR-P1); Medium → quick Policy Engine check; High → six-step Full Pipeline (§14.2) then Decision Matrix routing. Its public shape (`IProtectionClient.Validate`) is unchanged.

**Honest data-substrate note (not a new architectural gap):** §14.2's Context/Knowledge/Decision/Resource Validation steps (2-5) pass through this WP — `ActionRequest` carries no `ContextPayload`/`Explanation`/`Confidence`/resource-budget data (by design, per G3), and no real Scheduler budget infrastructure exists anywhere in this codebase yet (Constitution Part 7). Consistent with this session's established pattern, these steps are implemented honestly against the data that exists (none), not stubbed with invented infrastructure.

## Files Created

- `src/EOS.Gates/PolicyEngine.cs`, `RuleEngine.cs`, `RiskEngine.cs`, `ApprovalEngine.cs`
- `tests/EOS.Gates.Tests/PolicyEngineTests.cs`, `RuleEngineTests.cs`, `RiskEngineTests.cs`, `ApprovalEngineTests.cs`
- `docs/WP-012-Implementation-Plan.md`

## Files Modified

- `src/EOS.Gates/ProtectionGate.cs` — tiered-dispatch orchestrator
- `src/EOS.SharedKernel/Configuration/SecurityOptions.cs` — additive `PolicyEntry`/policy-tier fields
- `config/Security.json` — populated new fields (empty lists)
- `src/EOS.Runner/Program.cs` — additive: loads `SecurityOptions`, constructs the four engines, passes them into `ProtectionGate`
- `tests/EOS.Gates.Tests/ProtectionGateTests.cs` — rewritten to reflect real (not fail-closed-placeholder) behavior
- `tests/EOS.Runner.Tests/AskCommandIntegrationTests.cs` — mechanical constructor-argument update only (two call sites), no logic or assertion changed

## Files Not Modified (confirmed)

`src/EOS.Contracts/*`, `src/EOS.Knowledge/**`, `src/EOS.KnowledgeGraph/**`, `src/EOS.Reasoning/**`, `src/EOS.AIProvider/**`, `src/EOS.SDK/**`, `src/EOS.Runner/Bootstrap/JsonConfigurationLoader.cs`, `src/EOS.Runner/Commands/AskCommand.cs`.

## Dependency Changes

None. `EOS.Gates.csproj` unchanged (`EOS.Contracts` + `Microsoft.Extensions.Logging.Abstractions` only).

## Package Changes

None.

## Public Contract Changes

None. `IProtectionClient`, `ActionRequest`, `ValidationResult`, `ProtectionVerdict`, `RiskTier` all unchanged.
