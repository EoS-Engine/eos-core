# WP-002 Implementation Plan — Configuration Strategy & Bootstrap Sequence

**Revision:** 2 (refinement pass — supersedes the chat-only draft in full)
**Basis:** `EOS-Specification.md` Part 10 (Configuration Strategy), Part 12 (Bootstrap System); `EOS-Implementation-Roadmap-v1.0.md` WP-002 definition.

## Phase 1 Findings (carried forward, unchanged)

Constitution Part 10 §10.2 states configuration is "cached in Redis" at Bootstrap, but Redis connectivity doesn't exist until WP-004. WP-002 holds validated configuration in memory only; the Redis-backed cache is wired in by the WP that first has real Redis connectivity, following the roadmap's own established stub-then-retrofit pattern. This is an interpretation of sequencing, not a documentation change (Part 10.3 already delegates field-level schema to WP-002's own implementation task).

## Objective
Implement the ten Constitution Part 10 configuration files with a real, minimal, fail-fast-validated schema, and a small, explicit Bootstrap orchestrator (`BootstrapRunner`) that executes all ten Part 12 steps, each producing a real `BootstrapResult`, reaching Ready.

## Scope

**Included:**
- `config/*.json` × 10 — real JSON content, each matching exactly one Options record (no extra properties — required, since unknown properties are rejected).
- `EOS.SharedKernel/Configuration/`: `EosOptions`, `PlannerOptions`, `InferenceOptions`, `ProvidersOptions`, `ThresholdsOptions`, `SecurityOptions`, `DashboardOptions`, `KnowledgeOptions`, `StorageOptions`, `FeatureFlagsOptions` — one record per file, `.NET` Options naming convention, validated via `DataAnnotations`. Plus `ConfigurationValidationException`.
- `EOS.Runner/Bootstrap/`: exactly three orchestration types — `BootstrapResult` (StepName, Status, StartedAt, FinishedAt, Duration, Error), `BootstrapStep` (Name + execution delegate), `BootstrapRunner` (ordered step list, timed/logged execution, fail-fast on first failure). A static `BootstrapRunner.CreateEosBootstrap(...)` factory method (still part of the `BootstrapRunner` type, not a new class) composes the ten named steps. `BootstrapRunner` executes the pipeline and reports results — it never decides the process exit code (see Program Responsibility below).
- `EOS.Runner/Bootstrap/IConfigurationLoader` + one implementation (`JsonConfigurationLoader`) — loads and validates one Options file from `config/`. JSON-only; no other source.
- Every one of the ten Part 12 steps executes real code against already-loaded configuration state (no bare log-only stubs) — see step table below.
- `Program.cs` reduced to: build host → run `BootstrapRunner` → translate the final bootstrap status into the process exit code.

**Program Responsibility (clarified):** `BootstrapRunner` is responsible only for executing the pipeline and reporting the final bootstrap status (the list of `BootstrapResult`s and whether all succeeded). It never calls `Environment.Exit` or returns an exit code itself. `Program.cs` is the only component that reads that status and decides the process exit code (0 if every step succeeded, non-zero otherwise) — this keeps "did bootstrap succeed" (an orchestration concern) separate from "what does the OS process return" (a host concern).
- `tests/EOS.Runner.Tests`: config validation tests (malformed JSON, missing required value, unknown property, missing file) + one idempotency test (Bootstrap run twice consecutively, same outcome both times).

**Explicitly excluded (unchanged from Revision 1):** Redis-backed caching, real provider connectivity (WP-005), real data-store connections (WP-004), Knowledge Graph init, key/cert generation, hot-reload, `.env`/secrets-manager work. WP-002 invokes no infrastructure — every step below is a configuration-state check, never a network/file-store call.

## Step-by-Step Design (all ten execute; none are bare log lines)

| # | Step | What it actually checks |
|---|---|---|
| 1 | Install | `config/` directory exists and is readable |
| 2 | Validate | Loads + validates all ten Options files (fail closed on any error) — the substantial step |
| 3 | Generate Keys | `SecurityOptions` loaded successfully (no real key material yet — none is specified beyond `SecretsProvider: local`) |
| 4 | Configure Providers | `ProvidersOptions` has ≥1 entry with a syntactically valid endpoint URI |
| 5 | Start Infrastructure | `StorageOptions.DataDirectory` is a syntactically valid path (no connection attempt — infra doesn't exist yet) |
| 6 | Health Check | `ThresholdsOptions` internal consistency (`ResourceCriticalPercent > ResourceWarningPercent`) |
| 7 | Initialize Knowledge | `KnowledgeOptions.VectorStoreCollection` is non-empty |
| 8 | Seed Planner | `PlannerOptions.ReplanningCadenceMinutes > 0` |
| 9 | Run Validation | Aggregate check: every prior step in this run succeeded |
| 10 | Ready | Final state — reports that all prior steps succeeded and the system has reached Ready (`BootstrapRunner` reports this state only; it does not decide the process exit code — see Program Responsibility) |

## Projects Affected
`EOS.Runner`, `EOS.SharedKernel`, `tests/EOS.Runner.Tests` (new).

## Files to Create
- `config/*.json` × 10
- `src/EOS.SharedKernel/Configuration/*Options.cs` × 10, `ConfigurationValidationException.cs`
- `src/EOS.Runner/Bootstrap/BootstrapResult.cs`, `BootstrapStep.cs`, `BootstrapRunner.cs`, `IConfigurationLoader.cs`, `JsonConfigurationLoader.cs`
- `tests/EOS.Runner.Tests/EOS.Runner.Tests.csproj`, `ConfigurationValidationTests.cs`, `BootstrapRunnerTests.cs`

## Files to Modify
- `src/EOS.Runner/Program.cs` (thin: host → runner → exit)
- `src/EOS.Runner/EOS.Runner.csproj` (add `ProjectReference` → `EOS.SharedKernel`; `PackageReference` → `Microsoft.Extensions.Hosting`)
- `EOS.slnx` (add new test project)

## Public Interfaces
`IConfigurationLoader` (internal to `EOS.Runner` — not an `EOS.Contracts` boundary; no cross-subsystem contract is introduced).

## Configuration Changes
The ten `config/*.json` files — this WP's entire purpose.

## Database Changes
None.

## Tests
- Malformed JSON → rejected.
- Missing required value → rejected.
- Unknown JSON property → rejected (`JsonSerializerOptions.UnmappedMemberHandling = Disallow`).
- **Missing config file → `BootstrapRunner` fails with a clear validation error** (new, per review comment 5).
- Bootstrap run twice consecutively → identical outcome both times (idempotency).

## Acceptance Criteria
`dotnet run --project EOS.Runner` logs all ten steps (step number, name, success/failure, duration) and reaches Ready.

## Risks
Unchanged from Revision 1 (schema-shape-is-a-WP-002-decision; in-memory-vs-Redis deviation, both already flagged and accepted).

## Definition of Done
`dotnet restore/build/test/format` all succeed; zero warnings; `dotnet run --project EOS.Runner` logs ten steps and reaches Ready; repository clean; one commit.

## Future Evolution

The bootstrap pipeline (`BootstrapRunner`, `BootstrapStep`, `BootstrapResult`, the ten-step sequence, and the orchestration/exit-code separation established above) is intentionally designed to remain stable. Future Work Packages may extend the **internal implementation** of individual steps as real infrastructure and capabilities become available (e.g., "Start Infrastructure" gains a real SQL Server/Redis/ChromaDB connection check once WP-004 exists; "Configure Providers" gains a real Ollama connectivity check once WP-005 exists). They must not redesign the orchestration model, execution flow, or public behavior WP-002 establishes — no new step-runner abstraction, no change to `BootstrapResult`'s shape, no change to the fail-fast/exit-code contract. This protects architectural stability while allowing incremental evolution, consistent with the roadmap's own stub-then-retrofit pattern used throughout later milestones.

**Rule:** The bootstrap pipeline is considered stable. Future Work Packages may extend the internal implementation of existing bootstrap steps. Introducing new bootstrap steps, or changing the bootstrap execution pipeline itself, requires an approved ADR or an approved revision of the EOS Constitution. The default assumption is that the bootstrap pipeline remains fixed.
