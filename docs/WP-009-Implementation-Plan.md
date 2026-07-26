# WP-009 Implementation Plan — First Vertical Slice Integration

**Revision:** 1 (Final, Approved)
**Source of Truth (priority order):** `docs/Development-Workflow.md`, `docs/EOS-Specification.md`, `docs/EOS-Implementation-Roadmap-v1.0.md` (WP-009 row), `docs/EOS-System-Architecture-Specification-v1.0.md` (§8, §11, §12), the approved WP-009 Architecture Gap Analysis, this plan.

## Objective (roadmap, verbatim)

Wire WP-005 through WP-008 together behind a single CLI entry point, proving the full User Request → AI Provider → Reasoning → Memory → Response path.

## Architecture Decisions (from the approved Gap Analysis, not reopened)

1. CLI wiring uses direct, sequential method calls (`ReasonAsync` → `Validate` → `UpdateAsync`) — no event emission (Gap 1).
2. `EOS.Tools` is not modified; all real wiring lives in `EOS.Runner` (Gap 2).
3. `AskCommand` independently loads `Providers.json`/`Inference.json` via a second `JsonConfigurationLoader` call; `BootstrapRunner` is not modified (Gap 3).
4. `EOS.Runner.csproj` gains `ProjectReference`s to `EOS.Contracts`, `EOS.Reasoning`, `EOS.AIProvider`, `EOS.Gates`, `EOS.Knowledge`, `EOS.KnowledgeGraph`; both AI-Provider and Gates architecture whitelist tests extended to include `EOS.Runner`/`EOS.Runner.Tests` (Gap 4).
5. `Decision → ActionRequest`: `RiskScore = (int)Math.Round(decision.RiskScore)`, `ActionType = "Decision"`, `Actor = request.RequestingRole` (Gap 5).
6. `Decision → KnowledgeNode`: `NodeId = decision.DecisionId`, `NodeType = KnowledgeNodeType.Decision`, `Content = decision.SelectedHypothesis`, `DomainTags = []`, `EvidenceRefs = decision.EvidenceRefs` (Gap 6).
7. `ReasoningRequest.RequestingRole = "HumanOperator"` (Gap 7).
8. New `AskCommand` class in `src/EOS.Runner/Commands/`, constructor-injected with the four real dependencies; `Program.cs` becomes a thin dispatcher (Gap 8).

Implementation-time verification (pre-code) confirmed zero drift between these decisions and current repository state.

## Included Scope (roadmap, verbatim)

A minimal CLI command (`eos ask "<text>"`); the wiring calling Reasoning → Protection (`validate()`) → Memory (`update()`) in sequence; structured error handling for a malformed request.

## Explicitly Excluded Scope (roadmap, verbatim)

Any Planning & Execution Engine involvement (Milestone 6); any Learning Engine involvement (Milestone 6).

## Vertical Slice Definition

CLI `eos ask "<text>"` → Bootstrap (unchanged 10 steps) → `AskCommand.ExecuteAsync(text)` → `ReasoningEngine.ReasonAsync()` (real Ollama) → `Decision` → `ProtectionGate.Validate()` (real, synchronous) → on `Allow` → `KnowledgeClient.UpdateAsync()` (real SQL Server) → structured console output + exit code.

## Projects Affected

`EOS.Runner` only (`EOS.Tools` explicitly not touched, per Gap 2).

## Files to Create

- `src/EOS.Runner/Commands/AskCommand.cs`
- `tests/EOS.Runner.Tests/AskCommandIntegrationTests.cs`
- `docs/work-packages/WP-009-Completion-Report.md` (at closure)

## Files to Modify

- `src/EOS.Runner/Program.cs` — thin dispatcher.
- `src/EOS.Runner/EOS.Runner.csproj` — add six `ProjectReference`s.
- `tests/EOS.ArchitectureTests/OnlyAllowedProjectsMayReferenceAIProviderTests.cs` — whitelist extended.
- `tests/EOS.ArchitectureTests/OnlyAllowedProjectsMayReferenceEOSGatesTests.cs` — whitelist extended.
- `tests/EOS.Runner.Tests/EOS.Runner.Tests.csproj` — add `ProjectReference`s for the new integration test.

## Files That Must NOT Change

`src/EOS.Runner/Bootstrap/**`, `src/EOS.Tools/**`, `src/EOS.Reasoning/**`, `src/EOS.Gates/**`, `src/EOS.Knowledge/**`, `src/EOS.KnowledgeGraph/**`, `src/EOS.AIProvider/**`, `src/EOS.SDK/**`, `config/*.json`, any specification/roadmap/Constitution document.

## Dependency Changes

`EOS.Runner → EOS.Contracts, EOS.Reasoning, EOS.AIProvider, EOS.Gates, EOS.Knowledge, EOS.KnowledgeGraph` (all new, Constitution Part 1 §1.3-permitted). `EOS.Runner.Tests` gains the same set.

## Package Changes

None.

## Public Contracts

```csharp
public sealed class AskCommand(
    IReasoningEngineClient reasoningEngine,
    IProtectionClient protectionClient,
    IKnowledgeClient knowledgeClient,
    ILogger<AskCommand> logger)
{
    public async Task<int> ExecuteAsync(string text, CancellationToken cancellationToken = default);
}
```

No new `EOS.Contracts` type.

## Test Strategy

Integration only, real Ollama + real SQL Server, zero mocks: `AskCommand.ExecuteAsync("explain the SOLID principles")` returns success; the resulting `KnowledgeNode` is independently queryable via `KnowledgeGraphStore.GetByIdAsync()` with `NodeType == Decision` and non-empty `Content`.

## Acceptance Criteria (roadmap, verbatim)

`eos ask "explain the SOLID principles"` succeeds with networking disconnected; the interaction is queryable in SQL Server afterward.

## Implementation Sequence

1. Feature branch `wp-009-first-vertical-slice` (created).
2. This plan document.
3. `EOS.Runner.csproj` — add six `ProjectReference`s.
4. Extend both architecture whitelist tests.
5. `src/EOS.Runner/Commands/AskCommand.cs`.
6. `Program.cs` — thin dispatch.
7. `tests/EOS.Runner.Tests/AskCommandIntegrationTests.cs`.
8. `EOS.Runner.Tests.csproj` — add references.
9. Full Local Verification.
10. Architecture Gate self-review.
11. Stop for approval before push/PR.
