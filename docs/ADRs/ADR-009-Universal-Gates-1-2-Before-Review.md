# ADR-009 — Universal Gates 1–2 Before `Running → Review`

## 1. Status

**Accepted** — approved as the Engineering Correctness Boundary review of WP-A (working name; not a roadmap Work Package number) and authorized for implementation.

- **adr_id:** `ADR-009`
- **approver:** Product Owner / Architecture Board, per Decision Matrix §0.6
- **conditions:** Gates 1 and 2 only; no retry, repair, or self-healing; no new agent, role, service, or subsystem; no Git mutation of the real repository; no invented coverage threshold. ADR-006/007/008 unchanged.

## 2. Problem

Constitution Part 6 §6.2 requires the Universal Gates (§0.8.1) as the gate of `Running → Review`, and §0.15.1 requires `TaskCompleted` to reference *passing* evidence. WP-A applied only the Protection Layer's `TaskCompletion` validation. The real-model Attempt-3 artifact (`db434678…`) passed ADR-007's `git apply --check`, reached `Review`, and produced `TaskCompleted` — yet does not compile (CS1519: its test hunk drops a method signature). Applicability is not correctness.

## 3. Decision

Between artifact registration and the `TaskCompletion` validation, `ExecutionCoordinator.ExecuteAndCompleteAsync` calls `IUniversalGateClient.EvaluateAsync` (EOS.Contracts) with the task and its evidence refs.

- **Measurement (EOS.Infrastructure, `IsolatedUniversalGateRunner`):** copies `src/`, `tests/`, `config/`, `Directory.Build.props`, `global.json`, `EOS.slnx`, `.editorconfig` — never `.git`, `bin`, `.env` — into a throwaway temp directory (existing `obj/` is copied so the build runs `--no-restore`, offline and deterministic); applies the registered diff there with `git apply --` (stdin); derives the affected projects deterministically from the diff paths (`src/P/…` → `src/P/P.csproj`, `tests/T/…` → `tests/T/T.csproj`, plus `tests/P.Tests` when it exists); runs **Gate 1** `dotnet build <project> --no-restore` (this repository's static analysis: `TreatWarningsAsErrors` + analyzers) and **Gate 2** `dotnet test <testproject> --no-build --no-restore`. `ProcessStartInfo`, no shell, fixed working directory, bounded output, per-step timeout, cancellation kills the process tree, copy deleted in all cases. The real working tree, index, and status are never touched.
- **Decision (EOS.Gates, `RuleEngine.EvaluateUniversalGates`, Protection §10.3):** Allow iff Gate 1 `Passed` and Gate 2 `Passed` or `NotApplicable` (no test project owns or covers the changed paths). A step that could not run is `Failed`, never a pass (§0.8.3, fail closed).
- **Composition root (`Program.cs`, ADR-015-001):** `ProtectionGatedUniversalGateClient` validates the run as Local Files action `UniversalGateRun` (Protection §11), resolves `artifact:<sha256>` through the Artifact Registry (Part 8), runs the measurement, and hands the result to the Rule Engine.
- **Outcome:** gate failure ⇒ `Running → Blocked` (`BlockedReason` = the Rule Engine's reason naming the failing gate, plus the evidence ref), `TaskBlocked`, no `TaskCompleted`. Gate pass ⇒ the existing `TaskCompletion` validation, `Review`, `TaskCompleted`. `TaskCompleted` now means: valid, applicable, registered, **compiled, and tested**.

## 4. Consequences

- Attempt-3's artifact is blocked at Gate 1 with CS1519 (regression test in `EOS.Infrastructure.Tests` and `EOS.Runner.Tests`).
- Gates 3–5 (§0.8.1) remain a disclosed, unimplemented limitation; no coverage threshold is asserted because none is specified.
- A gate run costs a workspace copy plus a build of the affected projects (tens of seconds on this host); the step timeout is a constructor parameter (default 10 minutes), not a configuration file entry.
- Review remains human; EOS now only submits candidates that build and pass their targeted tests.
