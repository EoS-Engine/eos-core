# ADR-007 — Artifact Applicability Validation Before Registration

## 1. Status

**Accepted** — approved as the Architecture Review / Decision Record "Artifact Applicability Boundary" following the Post-Roadmap WP-A real demonstration, and authorized for implementation as a correction to WP-A (working name; not a roadmap Work Package number).

- **adr_id:** `ADR-007`
- **approver:** Product Owner / Architecture Board, per Decision Matrix §0.6
- **conditions:** one additive `EOS.Contracts.IWorkspaceClient` operation, its `EOS.Infrastructure.WorkspaceReader` implementation, its Protection-gated composition-root adapter, and one additional step in `EOS.SeniorEngineer.SeniorEngineer.ExecuteAsync`. No lifecycle, Scheduler, Artifact Registry, or specification change.

## 2. Finding

The WP-A real demonstration reached `TaskCompleted` with a registered Evidence artifact (`artifact:8d59cecc…128cd`) that `git apply --check` rejected (fabricated hunk context) and whose `src` hunk did not implement the requested change. Structural/security validation of a unified diff (paths, sides, traversal, `/dev/null`, hunks) does not establish that the diff corresponds to the real files it claims to modify.

## 3. Decision

Implementation evidence in the form of a diff (Constitution Part 6 §6.2, `Running → Review`) must be **applicable to the workspace it was generated from** before it is registered (Constitution §0.1.1.1 "evidence over assertion", §0.1.1.6 "reality over simulation"). The executing role therefore performs, in this exact order:

`Read referenced files → ReasonAsync → ExtractDiffFence → ValidateUnifiedDiff (structural/security) → CheckPatchAppliesAsync (applicability) → RegisterAsync → return artifact:<sha256>`

- **Minimum applicability gate:** `git apply --check` against the fixed workspace root, patch on standard input, no shell, no `--index`/`--cached`/`--3way`/`--recount`/`--unsafe-paths`; bounded output, timeout, cancellation-aware; **fail closed** (a check that cannot run is "not applicable").
- **Read-only:** the check never modifies the working tree, the index, or repository metadata.
- **Boundary:** `IWorkspaceClient.CheckPatchAppliesAsync` (Contracts) → `WorkspaceReader` (Infrastructure, Protection §11 "actual file I/O") → gated in the composition root under the Local Files action type `WorkspaceApplicabilityCheck` (Protection §10, structural enforcement).
- **Failure:** the role throws; the unchanged Execution Coordinator path records `Running → Blocked` with the bounded applicability error as `BlockedReason`, publishes/persists `TaskBlocked`, registers **no** artifact, and produces **no** `TaskCompleted` — identical to every other execution failure. Applicability validation runs only after structural validation succeeds.

## 4. Explicit Limits

The new guarantee is only: *the registered artifact is structurally valid and applies to the workspace as-is.* It does **not** establish that the change implements the requested intent, compiles, or passes tests — those remain the human `Review → Testing → Verified` transitions (Part 6 §6.2) and are not automated. No build, test, commit, checkout, patch application, or workspace mutation is performed by EOS.

## 5. Alternatives Rejected

Placing the check in `ArtifactStore` (no workspace access; storage, not judgment), in `ExecutionCoordinator` (would give the Orchestrator a workspace dependency and move engineering judgment out of the role), in `LoopController` (sequencing only); `--recount` (would register artifacts that do not apply with plain `git apply`); automatic semantic/intent validation (new capability, not a correction); registering non-applicable artifacts (not implementation evidence; inconsistent with the structural-failure path).

## 6. Consequences

`tests/EOS.Infrastructure.Tests/WorkspaceReaderTests.cs`, `tests/EOS.SeniorEngineer.Tests/SeniorEngineerTests.cs`, and `tests/EOS.Runner.Tests/FirstExecutionSliceAcceptanceTests.cs` prove the boundary, the fail-closed behaviour, the ordering, the Protection gating, and that a structurally valid but non-applicable diff cannot reach `Review`/`TaskCompleted`. `git` is an existing host prerequisite (Infrastructure Roadmap Phase 2), invoked read-only; no new package or project.
