# EOS Testing Guide

**Document Type:** Developer guide (not an architecture document)
**Audience:** Anyone verifying a change to EOS
**Authority:** `docs/Development-Workflow.md` §8 defines the mandatory Local Verification Checklist. This guide explains how to run it and how to interpret the results.

The governing rule, quoted from `Development-Workflow.md` §8:

> A verification step that cannot run because a real dependency (a database, a running container) is unavailable is reported as **not run**, never silently skipped and never faked as passing.

That rule is why this guide is explicit about which tests need which infrastructure.

---

## 1. The Complete Verification Suite

Run in full, from the repository root, before any Architecture Gate or pull request:

```bash
dotnet restore
dotnet build                        # zero errors, zero warnings
dotnet test                         # every existing test plus every new test
dotnet format --verify-no-changes
git diff --check
```

Plus, where applicable to the change:

- `dotnet run --project src/EOS.Runner` reaching `[10/10] Ready` — required whenever the change touches `EOS.Runner` or `BootstrapRunner`
- the relevant Docker containers verified healthy **before** integration tests are run, never assumed

### Prerequisites

`dotnet test` is **not** self-contained. Before running it:

```bash
docker compose up -d && docker compose ps          # all three healthy
ollama list                                        # qwen2.5-coder:7b present

export EOS_SQLSERVER_CONNECTION_STRING='Server=localhost,1433;Database=master;User Id=sa;Password=<your-password>;TrustServerCertificate=True'
export EOS_REDIS_CONNECTION_STRING='localhost:6379'
export EOS_CHROMADB_ENDPOINT='http://localhost:8000'
```

Test projects that need these variables also load `.env` themselves through the test-only helper `tests/EOS.Infrastructure.Tests/EnvFileLoader.cs` — but not every project does, and `EOS.Runner.Tests` calls `DataStoreConnectionOptions.FromEnvironment()` directly. Exporting is the reliable path.

---

## 2. Build Commands

```bash
dotnet restore                          # whole solution (EOS.slnx is auto-discovered)
dotnet build                            # Debug
dotnet build -c Release
dotnet build src/EOS.Knowledge/EOS.Knowledge.csproj    # one project
```

`Directory.Build.props` sets `TreatWarningsAsErrors=true`, `EnableNETAnalyzers=true`, and `Nullable=enable` for every project. **Any warning is a build failure.** A clean build reports:

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

---

## 3. Test Commands

```bash
dotnet test                                                     # entire solution
dotnet test --no-build                                          # reuse a prior build
dotnet test tests/EOS.Gates.Tests/EOS.Gates.Tests.csproj        # one project
```

### Running focused tests

```bash
# One test class
dotnet test tests/EOS.Gates.Tests/EOS.Gates.Tests.csproj \
  --filter "FullyQualifiedName~RiskEngineTests"

# One test method
dotnet test tests/EOS.Runner.Tests/EOS.Runner.Tests.csproj \
  --filter "FullyQualifiedName~AskCommandIntegrationTests.ExecuteAsync_ReturnsNonZero_WhenTextIsEmpty"

# Selected projects that avoid Ollama (fast feedback loop)
dotnet test tests/EOS.Gates.Tests/EOS.Gates.Tests.csproj
dotnet test tests/EOS.Resources.Tests/EOS.Resources.Tests.csproj
dotnet test tests/EOS.ArchitectureTests/EOS.ArchitectureTests.csproj
dotnet test tests/EOS.Dashboard.Tests/EOS.Dashboard.Tests.csproj

# More verbose output when a failure is opaque
dotnet test --logger "console;verbosity=detailed"
```

There is no `[Trait]`/category convention in this repository — filtering is by fully-qualified name or by project.

---

## 4. Test Project Organization

The current named inventory contains 17 test projects, all xUnit 2.9.3 on `Microsoft.NET.Test.Sdk` 17.14.1, each referencing only what it exercises:

| Test project | Under test | External dependencies |
|---|---|---|
| `EOS.ArchitectureTests` | The `.csproj` reference graph | **None** |
| `EOS.Gates.Tests` | Protection Layer engines | **None** |
| `EOS.SeniorEngineer.Tests` | Scoped path extraction, `[EDIT]` processing, deterministic diffs, applicability and evidence | **None** (test doubles and temporary workspaces) |
| `EOS.Resources.Tests` | Capacity, quotas, residency, background tasks | **None** (reads host CPU/RAM/disk) |
| `EOS.Dashboard.Tests` | `DashboardQueryService` | **None** (test doubles) |
| `EOS.Web.Tests` | `DashboardWebHost` route mapping | **None** (test doubles) |
| `EOS.VectorStore.Tests` | `ChromaVectorStore` | ChromaDB |
| `EOS.Infrastructure.Tests` | Connectivity, `SqlEventStore`, `RedisMemoryStore` | SQL Server, Redis, ChromaDB, SQLite |
| `EOS.Knowledge.Tests` | Memory + Knowledge Management clients, and `EOS.KnowledgeGraph`'s stores (which have no test project of their own) | SQL Server, ChromaDB, Ollama (embeddings) |
| `EOS.Learning.Tests` | Ingestion, clustering, stages, ROI, fitness, integrity | SQL Server, Ollama |
| `EOS.Planner.Tests` | Goals, plans, task graphs, replanning | SQL Server, Ollama |
| `EOS.Orchestrator.Tests` | Mediator, scheduler, execution outcomes, retry/rollback, loop | SQL Server |
| `EOS.Reasoning.Tests` | The 12-stage pipeline | Ollama |
| `EOS.AIProvider.Tests` | Registry, routing, health, failover, adapters | Ollama (integration tests only) |
| `EOS.Runner.Tests` | Bootstrap, config validation, `ask`, composition, execution-slice acceptance and isolated gates | SQL Server, Redis, ChromaDB, Ollama; individual classes may be deterministic |
| `EOS.Deploy.Tests` | `deploy/backup.sh` | `bash`, `tar` (Linux) |
| `EOS.RestoreDrill.Tests` | `deploy/restore-drill.sh` + `RestoreDrillRunner` | `bash`, Docker (one test) |

---

## 5. Architecture Tests

`tests/EOS.ArchitectureTests` is the fitness suite. It references **no** projects — it locates the repository root by walking up to `EOS.slnx`, parses `.csproj` XML directly, and asserts on the reference graph. It is fully deterministic and needs no infrastructure.

```bash
dotnet test tests/EOS.ArchitectureTests/EOS.ArchitectureTests.csproj
```

The current named rule inventory is:

| Test | Enforces |
|---|---|
| `NoCircularProjectReferencesTests.ProjectReferenceGraph_HasNoCircularReferences` | Constitution R-00 — the `src/` graph is acyclic |
| `OnlyAllowedProjectsMayReferenceAIProviderTests` | Only whitelisted projects may reference `EOS.AIProvider` |
| `OnlyAllowedProjectsMayReferenceEOSGatesTests` | Only whitelisted projects may reference `EOS.Gates` |
| `SeniorEngineerMayReferenceOnlyContractsTests.EOSSeniorEngineer_ReferencesExactlyEOSContracts` | Constitution R-02 — `EOS.SeniorEngineer` references exactly `EOS.Contracts` |

The two whitelist tests carry inline comments recording *why* each entry is allowed and which Work Package resolved it. If you add a reference that trips one of these, the fix is normally an architectural conversation — not an edit to the whitelist.

Constitution Part 2 §2.3 names R-01 through R-09 as well. R-02 now has the concrete Senior Engineer rule above; the remaining named rules are not automatically covered merely because they appear in the Constitution.

---

## 6. Deterministic Tests

These projects need no EOS datastore or Ollama service:

- `EOS.ArchitectureTests`
- `EOS.Gates.Tests`
- `EOS.Dashboard.Tests`
- `EOS.Web.Tests`
- `EOS.SeniorEngineer.Tests`

`EOS.Resources.Tests` needs no external service, but it is not purely deterministic in the strictest sense: `ResourceMonitor` samples the **real** host CPU, RAM, and disk. Its tests are written against that reality rather than fixed values.

### Execution-slice and Universal Gate coverage

`EOS.SeniorEngineer.Tests` covers explicit path scoping, strict `[EDIT]` parsing and application, deterministic unified-diff generation, structural validation, read-only applicability checks, and artifact evidence. `EOS.Orchestrator.Tests` covers `Running → Review`/`TaskCompleted` and failure-to-`Blocked`/`TaskBlocked` outcomes. `EOS.Runner.Tests` contains execution-slice acceptance coverage, including isolated Gate 1 build/static analysis, Gate 2 targeted tests, gate failure, and the rule that gate child processes do not receive EOS runtime credentials.

Some Runner and Orchestrator acceptance tests use real SQL Server stores. Do not infer that an entire project is service-free because a particular execution test uses test doubles.

Within the infrastructure-dependent projects, many individual test classes are themselves deterministic (pure algorithm tests over test doubles) — for example `RetrievalRankingTests`, `RankingWeightsTests`, `FreshnessCalculatorTests`, `IntegrityHashCalculatorTests`, `RoiGateTests`, `ConfidenceGuardTests`, `PriorityManagerTests`, `EventMediatorTests`, and `EventEnvelopeTests`. They live in projects whose *other* classes need infrastructure, so running the whole project still requires it.

---

## 7. Infrastructure-Dependent Tests

Tests that require SQL Server, Redis, or ChromaDB **fail** when the store is unreachable. They do not skip. `DataStoreConnectionOptions.FromEnvironment()` throws `InvalidOperationException` when a variable is unset, and connection failures surface as ordinary assertion or connection errors.

This is deliberate — `Development-Workflow.md` §2 requires that "where a WP's acceptance criteria require a real store or service, tests run against that real service. A mock is never the sole evidence that connectivity works."

### Test isolation expectations

There is **no per-test data isolation convention** in this repository. Tests write to the same shared, real SQL Server tables and generally do not clean up after themselves.

Three assemblies therefore disable xUnit's parallel execution outright, each with an explanatory `AssemblyInfo.cs`:

```csharp
[assembly: CollectionBehavior(DisableTestParallelization = true)]
```

- `EOS.Infrastructure.Tests`
- `EOS.Learning.Tests` — "PipelineRecord/IngestionRateGuardState tests query real, shared SQL Server tables with no per-test isolation"
- `EOS.Orchestrator.Tests` — "Scheduler/ExecutionCoordinator tests query DispatchedTask state globally across the whole real SQL Server table"

**Consequences you should expect:**

- Rows accumulate in the dev database across runs. A test that counts globally is written to tolerate that; one that does not would be a defect.
- Test projects still run in parallel *with each other* by default. Where that matters, run the project alone.
- If you add a test that queries a shared table globally, follow the existing precedent and add the same assembly attribute rather than inventing a cleanup convention.

---

## 8. Environment-Dependent Tests

Tests that call Ollama depend on the host's inference speed. On CPU-only hardware a single `qwen2.5-coder:7b` completion commonly takes 30–60 seconds.

Several integration tests construct an `HttpClient` **without** overriding `Timeout`, so they inherit the .NET default of **100 seconds** — for example `tests/EOS.Runner.Tests/AskCommandIntegrationTests.cs`. (The production path in `Program.cs` sets the timeout from `Thresholds.inferenceTimeoutSeconds`, which is also `100`.)

Under load — notably when the whole suite runs and `EOS.Reasoning.Tests` is driving Ollama concurrently — a single inference can exceed that 100-second budget, and the affected test fails with an inference error rather than a logic error.

**Observed on the reference machine (Ubuntu, CPU-only inference), 2026-09-10:**

- Full-suite run: `AskCommandIntegrationTests.ExecuteAsync_ExplainSOLIDPrinciples_SucceedsAndPersistsARealQueryableKnowledgeNode` **failed** after 1 m 43 s (`Assert.Equal() Failure: Expected 0, Actual 1` — `AskCommand` returns `1` when reasoning fails).
- The same test re-run **in isolation** immediately afterwards: **passed** in 57 s.

That is contention, not a defect. Diagnose this class of failure by re-running the single test alone before concluding anything else:

```bash
dotnet test tests/EOS.Runner.Tests/EOS.Runner.Tests.csproj --no-build \
  --filter "FullyQualifiedName~AskCommandIntegrationTests"
```

**Docker-dependent test.** `EOS.RestoreDrill.Tests` contains one test that runs a real, end-to-end restore drill: it executes `deploy/backup.sh`, then `deploy/restore-drill.sh`, which starts an isolated Docker Compose stack (distinct container names and ports from the live one) and runs the real `BootstrapRunner` against it. It probes `docker info` first and calls `Assert.Fail("Docker unavailable — real restore-drill not executed.")` when Docker is absent — an explicit, visible failure rather than a silent skip, exactly as the workflow requires. This test takes several minutes because it starts a fresh SQL Server container and waits for its healthcheck.

---

## 9. Formatting and Static Verification

```bash
dotnet format --verify-no-changes     # fails if any file would be reformatted
dotnet format                         # applies the formatting
git diff --check                      # trailing whitespace / conflict markers
```

Formatting rules come from `.editorconfig`: LF line endings, final newline, trimmed trailing whitespace, UTF-8; 4-space indent for `.cs`, 2-space for `.json`/`.yml`/`.yaml`; and `csharp_using_directive_placement = outside_namespace:warning`.

Static analysis is on by default through `EnableNETAnalyzers=true` in `Directory.Build.props`, and every analyzer warning is an error. There is no separate lint step — analysis happens during `dotnet build`.

**There is no CI configuration in this repository.** No `.github/` directory exists. Every verification step above is run locally by the developer, which is precisely why `Development-Workflow.md` §8 spells the checklist out and forbids reporting an unrun step as passed.

---

## 10. Diagnosing Test Failures

Work through these in order:

**1. Is the failure infrastructural?**

```bash
docker compose ps                                    # all three healthy?
for name in EOS_SQLSERVER_CONNECTION_STRING EOS_REDIS_CONNECTION_STRING EOS_CHROMADB_ENDPOINT; do
  test -n "${!name:-}" && echo "$name is set" || echo "$name is not set"
done
curl -s http://localhost:8000/api/v2/heartbeat       # ChromaDB
docker exec eos-redis redis-cli ping                 # Redis
ollama list                                          # model present
```

A message like `Required environment variable 'EOS_...' is not set.` or a SQL pre-login handshake error means the environment, not the code.

**2. Does it reproduce in isolation?** Run the single test class alone. If it passes alone and fails in the full suite, you are looking at either Ollama contention (§8) or shared-table interference (§7) — not a logic defect.

**3. Is it shared state?** Check whether the test asserts on a global count or on "the newest row". Prior runs leave data behind.

**4. Get the real message.**

```bash
dotnet test <project> --no-build --logger "console;verbosity=detailed" \
  --filter "FullyQualifiedName~<Class>.<Method>"
```

**5. Is it an architecture test?** Read the whitelist comments in the failing test file. They record which Work Package authorized each entry. A new violation usually means a reference was added that the dependency rules do not permit.

**6. Is it a build failure disguised as a test failure?** `dotnet test` builds first. `TreatWarningsAsErrors=true` means a new analyzer warning stops the run before any test executes.

---

## 11. Reporting Results

Test-case counts and durations change as the implementation and environment change; they are not repository invariants. Report the command, commit, environment prerequisites, passed/failed/skipped outcome, and any project that was not run. Do not copy an old aggregate count forward as the expected current total.

For infrastructure-backed verification, record the service health and relevant environment-variable presence without printing secret values. If a dependency is unavailable, report the affected command as **not run — environment blocked**, not as passed or skipped.

---

## 12. Known Limitations

- **No CI.** Verification is local-only.
- **No per-test data isolation.** Shared real tables, no cleanup convention; three assemblies serialize themselves to compensate.
- **Integration tests fail rather than skip** when infrastructure is missing. That is intentional, and it means a red suite on a machine without Docker or Ollama says nothing about the code.
- **Ollama-backed tests are timing-sensitive** on CPU-only hardware, against a 100-second default `HttpClient` timeout.
- **Constitution fitness rules R-01 through R-09 are not automated.** Only R-00 plus two dependency whitelists are covered.
- **The real restore drill takes minutes** and needs a working Docker daemon; it fails loudly if Docker is unavailable.
- **`EOS.Mobile` has no test coverage in this suite.** It is a Flutter project outside `EOS.slnx`, currently an unmodified `flutter create` scaffold; `dotnet test` never touches it.
