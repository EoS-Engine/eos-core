# EOS Developer Guide

**Document Type:** Developer guide (not an architecture document)
**Audience:** Developers working inside the EOS codebase
**Authority:** `docs/EOS-Specification.md` (the Constitution) and the subsystem specifications are authoritative for architecture. This guide describes how the architecture is *currently realized in code*. Where the two differ, the specification wins and the difference is a finding to raise, not a licence to change either one.

This guide does not propose a redesign. It explains the code that exists.

---

## 1. Overall Architecture

EOS is a **layered modular monolith** on .NET 10, built as 33 .NET projects plus one Flutter project, with exactly one composition root.

Three rules do most of the structural work:

1. **`EOS.Contracts` is the only cross-subsystem language.** Subsystems talk to each other through interfaces and DTOs declared there — never by referencing each other's implementation types.
2. **`EOS.Runner` is the only project allowed to reference everything.** It is pure wiring; no business logic lives there.
3. **No project may create a dependency cycle.** This is enforced by an automated test, not by convention (`tests/EOS.ArchitectureTests/NoCircularProjectReferencesTests.cs`).

Constitution Part 2 §2.3 names ten fitness-rule identifiers, R-00 through R-09. **Four architecture checks are currently automated** in `EOS.ArchitectureTests` (see §9); this does not imply that all ten constitutional rules are implemented.

### 1.1 Actual dependency direction

The reference graph below is read directly from the `.csproj` files.

```text
EOS.Core            (leaf: no references, no source files)
   ▲
EOS.SharedKernel    → EOS.Core
   ▲
EOS.Contracts       → EOS.SharedKernel
   ▲
EOS.Domain          → EOS.SharedKernel, EOS.Contracts        (no source files)
   ▲
EOS.Application     → EOS.Domain, EOS.Contracts              (no source files)
   ▲
EOS.Infrastructure  → EOS.Application, EOS.Contracts
```

Capability subsystems branch off that spine:

| Project | References |
|---|---|
| `EOS.SDK` | `EOS.Core`, `EOS.SharedKernel`, `EOS.Contracts` |
| `EOS.AIProvider` | `EOS.SDK` |
| `EOS.Reasoning` | `EOS.Contracts`, `EOS.SDK`, `EOS.AIProvider` |
| `EOS.Gates` | `EOS.Contracts` |
| `EOS.Resources` | `EOS.Contracts`, `EOS.SDK` |
| `EOS.KnowledgeGraph` | `EOS.Infrastructure` |
| `EOS.VectorStore` | `EOS.Infrastructure` |
| `EOS.Knowledge` | `EOS.Contracts`, `EOS.KnowledgeGraph`, `EOS.VectorStore` |
| `EOS.Learning` | `EOS.Contracts`, `EOS.Knowledge`, `EOS.SDK` |
| `EOS.Planner` | `EOS.Contracts`, `EOS.Knowledge` |
| `EOS.Orchestrator` | `EOS.Contracts`, `EOS.Application` |
| `EOS.SeniorEngineer` | `EOS.Contracts` **only** |
| `EOS.Dashboard` | `EOS.Contracts` **only** |
| `EOS.Web` | `EOS.Dashboard`, `EOS.Contracts` |
| `EOS.Runner` | 14 projects, including `EOS.SeniorEngineer` — the composition root |
| `EOS.RestoreDrill` | `EOS.Runner` |

`EOS.Reasoning` is the **sole** consumer of `IAIProviderClient.InferAsync`; `EOS.Knowledge` reaches embeddings only through an adapter the composition root supplies. This is enforced structurally by `tests/EOS.ArchitectureTests/OnlyAllowedProjectsMayReferenceAIProviderTests.cs`.

### 1.2 Projects that exist but contain no code

The Constitution's Part 1 §1.1 registers every project below, and each is in `EOS.slnx` and builds — but the following contain **zero C# source files** today:

`EOS.Core`, `EOS.Domain`, `EOS.Application`, `EOS.Tools`, `EOS.Pipeline`, and nine role projects (`EOS.CTO`, `EOS.PrincipalEngineer`, `EOS.TechLead`, `EOS.QA`, `EOS.DevOps`, `EOS.ProductOwner`, `EOS.BusinessAnalyst`, `EOS.AIArchitect`, `EOS.MobileArchitect`). `EOS.SeniorEngineer` is the first role project with production behaviour and is described below.

They carry only the project references the frozen dependency rules permit. They are registration and dependency-boundary declarations, not implementations. `EOS.Pipeline` is explicitly **Deferred (Post-v1)** by Constitution §1.4.

`src/EOS.Mobile/` is a Flutter project (Dart, `pubspec.yaml`) deliberately outside `EOS.slnx` — the cross-runtime boundary the Constitution requires. It is currently an unmodified `flutter create` scaffold (the default counter demo in `lib/main.dart`); **no EOS-specific mobile functionality is currently implemented**.

`benchmarks/`, `scripts/`, and `prompts/`, named in Part 1 §1.1, do not exist in the repository.

---

## 2. Major Projects and Their Responsibilities

| Project | Responsibility | Key types |
|---|---|---|
| `EOS.SharedKernel` | Configuration option records and their validation; base primitives | `ThresholdsOptions`, `ProvidersOptions`, `ConfigurationValidationException`, `Entity`, `ValueObject` |
| `EOS.Contracts` | Every cross-subsystem interface, DTO, event payload shape, and enum | `IReasoningEngineClient`, `IProtectionClient`, `IPlanningClient`, `ILoopControlClient`, `IResourceManagementClient`, `EventEnvelope<T>`, `Decision`, `Plan`, `DispatchedTask` |
| `EOS.SDK` | The narrow AI-provider abstraction | `IAIProviderClient`, `IEmbeddingProviderClient`, `InferenceRequest`, `InferenceResult`, `Vector` |
| `EOS.AIProvider` | Provider registry, routing, health monitoring, failover, Ollama adapters | `AIProviderManager`, `InferenceRouter`, `ProviderRegistry`, `HealthMonitor`, `OllamaProviderAdapter`, `OllamaEmbeddingAdapter` |
| `EOS.Reasoning` | The 12-stage reasoning pipeline | `ReasoningEngine`, `ReasoningEngineOptions`, `IContextAcquisitionProvider` |
| `EOS.Gates` | Protection Layer: policy, rule, risk, approval, emergency shutdown | `ProtectionGate`, `PolicyEngine`, `RuleEngine`, `RiskEngine`, `ApprovalEngine`, `EmergencyShutdownState` |
| `EOS.Infrastructure` | Physical stores, workspace access, isolated gate execution, and health checks | `DataStoreConnectionOptions`, `DataStoreHealthChecker`, `SqlEventStore`, `RedisMemoryStore`, `ArtifactStore`, `WorkspaceReader`, `IsolatedUniversalGateRunner` |
| `EOS.KnowledgeGraph` | Graph node storage and traversal over SQL Server | `KnowledgeGraphStore`, `KnowledgeNode`, `RelationshipEdge`, `ArchivedContentStore` |
| `EOS.VectorStore` | ChromaDB-backed embedding storage and similarity search | `ChromaVectorStore` |
| `EOS.Knowledge` | Memory Management + Knowledge Management client surfaces | `IKnowledgeClient`/`KnowledgeClient`, `IKnowledgeManagementClient`/`KnowledgeManagementClient`, `CompressionSweep`, `RetrievalRanking` |
| `EOS.Learning` | Meta-learning pipeline: ingestion, clustering, stage promotion, ROI gate, fitness functions | `Ingestion`, `ClusterTrigger`, `StageEngine`, `RoiGate`, `FitnessMonitor`, `IntegrityChecker`, `QuarantineClearingService` |
| `EOS.Planner` | Goals, plans, task graphs, dependencies, priorities, replanning | `PlanningEngine`, `GoalManager`, `GoalValidator`, `TaskGraphBuilder`, `DependencyManager`, `PriorityManager` |
| `EOS.Orchestrator` | In-process event mediation, scheduling, execution, retry/rollback, the autonomous loop | `EventMediator`, `Scheduler`, `ExecutionCoordinator`, `RetryManager`, `RollbackManager`, `ProgressMonitor`, `LoopController` |
| `EOS.SeniorEngineer` | Executes one scoped engineering task and registers a candidate diff as evidence | `SeniorEngineer`, `UnifiedDiffBuilder` |
| `EOS.Resources` | Capacity measurement, tier classification, quotas, model residency | `ResourceMonitor`, `CapacityManager`, `QuotaManager`, `BackgroundTaskController`, `ResourceManagementClient` |
| `EOS.Dashboard` | Read-only query aggregation | `DashboardQueryService` |
| `EOS.Web` | HTTP presentation of the Dashboard | `DashboardWebHost` |
| `EOS.Runner` | Bootstrap, configuration loading, CLI commands, composition | `BootstrapRunner`, `JsonConfigurationLoader`, `AskCommand`, `Program.cs` |
| `EOS.RestoreDrill` | Disaster-recovery drill driver that reuses `BootstrapRunner` unchanged | `RestoreDrillRunner` |

---

## 3. Core Domain Concepts

- **`KnowledgeNode`** (`EOS.KnowledgeGraph`) — the durable unit of knowledge. Carries a `KnowledgeNodeType`, content, domain tags, evidence references, and `KnowledgeMetadata` (taxonomy, relationships, lifecycle state, quality profile, version history).
- **`MemoryRef`** (`EOS.Knowledge`) — a `(MemoryType, Key)` pair identifying ephemeral memory (Working / ShortTerm / Session, Redis-backed) for consolidation into a persistent node.
- **`Decision`** (`EOS.Contracts`) — the Reasoning Engine's output: selected hypothesis, rejected hypotheses, evidence refs, confidence, `Explanation`, trade-offs, risk score.
- **`ActionRequest` / `ValidationResult`** (`EOS.Contracts`) — the Protection Layer's input and verdict (`Allow` / `Deny` / `Defer` / `Retry`, plus a `RiskTier`).
- **`Goal` → `Plan` → `PlanTask` → `DispatchedTask`** — the planning-to-execution chain. A `PlanTask` is planning-time only; it becomes Constitution Part 6's Task entity as a `DispatchedTask` when the `Scheduler` dispatches it.
- **`EventEnvelope<TPayload>`** (`EOS.Contracts`) — the Part 3 event envelope: `EventId`, `EventType`, `Version`, `Producer`, `CorrelationId`, `CausationId`, `OccurredAt`, `Payload`.
- **`PipelineRecord` / `PipelineStage`** — the Learning Engine's compounding pipeline record and its stage.
- **`LoopIteration` / `TriggerContext` / `OperationalMode`** — the Autonomous Engineering Loop's iteration record, entry trigger, and the eight named operational modes.

---

## 4. Runtime Flow

### 4.1 Startup

`src/EOS.Runner/Program.cs` is a top-level-statements program. Every invocation does the same first three things:

```csharp
var host = Host.CreateApplicationBuilder(args).Build();      // logging only
var loader = JsonConfigurationLoader.Discover();             // finds <repo>/config
var runner = BootstrapRunner.CreateEosBootstrap(loader, bootstrapLogger);
var results = await runner.RunAsync();
```

`BootstrapRunner` executes ten named steps in order, stopping at the first failure:

`Install` → `Validate` → `Generate Keys` → `Configure Providers` → `Start Infrastructure` → `Health Check` → `Initialize Knowledge` → `Seed Planner` → `Run Validation` → `Ready`

Each step produces a `BootstrapResult` (name, status, timestamps, duration, error). If any step failed, `Program` returns `1` and nothing further runs.

If the argument list is not exactly `["ask", <text>]`, `["compress"]`, `["web"]`, or `["run", <engineering-task>]`, the process returns `0` immediately after bootstrap.

### 4.2 Composition

For the four real commands, `Program.cs` then constructs the entire object graph by hand — no DI container is used for EOS's own types. `Host.CreateApplicationBuilder` exists solely to supply `ILogger<T>` instances.

The wiring order is, in outline: HTTP clients and Ollama adapters → `ProviderRegistry` / `HealthMonitor` / `InferenceRouter` / `AIProviderManager` → `ProtectionGate` → `EventMediator` → resource management → knowledge/graph/vector stores and `KnowledgeClient` → `ReasoningEngine` → Learning Engine components → Knowledge Management → Planner → Scheduler → `ArtifactStore` / protected `WorkspaceReader` → `SeniorEngineer` → `ExecutionCoordinator` / protected Universal Gates → Retry/Rollback/Progress → `LoopController` → `SqlEventStore` persistence subscriptions → Dashboard adapters.

Stores create their own tables at startup — every store exposes `EnsureTableExistsAsync`, called from `Program.cs`. There is no migration tool.

### 4.3 The `ask` path

```
AskCommand.ExecuteAsync(text)
  → ReasoningEngine.ReasonAsync(ReasoningRequest)     // 12 stages, one inference call
      → AIProviderManager.InferAsync                   // routes to a healthy provider/model
          → OllamaProviderAdapter                      // HTTP to localhost:11434
  → ProtectionGate.Validate(ActionRequest)             // risk tier → policy/rule/approval
  → KnowledgeClient.UpdateAsync(...)                   // persists a Decision KnowledgeNode
  → Console.WriteLine(decision.SelectedHypothesis)
```

Non-`Allow` verdicts and reasoning failures both log an error and return exit code `1`.

### 4.4 The `run` engineering path

`run "<engineering task>"` starts one human-issued `ManualRequest` iteration. It is not a continuous background worker:

```
LoopController.RunIterationAsync
  → goal validation and planning
  → Scheduler readiness and ExecutionCoordinator.DispatchNextAsync
  → Ready → Running + TaskStarted
  → SeniorEngineer.ExecuteAsync
      → read only explicitly named src/ or tests/ paths through IWorkspaceClient
      → one ReasonAsync call requesting strict [EDIT] blocks
      → apply those edits to in-memory copies
      → generate a unified diff deterministically
      → git apply --check against the read-only workspace
      → register immutable artifact:<sha256> evidence
  → isolated Universal Gate 1 (build/static analysis)
  → isolated Universal Gate 2 (targeted unit tests, or NotApplicable)
  → TaskCompletion Protection validation
  → Running → Review + TaskCompleted
```

Protection has three distinct outcomes on this path. A non-`Allow` decision at the loop's step-7 `LoopIterationDecision` gate ends the iteration as `Denied` before any Goal or Task exists. A non-`Allow` `TaskDispatch` decision leaves the selected Task `Ready`, publishes no `TaskBlocked`, and—because the current `LoopController` executes only `DispatchOutcome.Dispatched`—the outer iteration can finish as `Completed` without `TaskCompleted`. Once dispatch succeeds and the Task is `Running`, role execution/evidence failure, Universal Gate failure, or non-`Allow` `TaskCompletion` Protection records `Running → Blocked`, publishes `TaskBlocked`, and never publishes `TaskCompleted`; cancellation during role or gate execution records Blocked before it is rethrown.

Gate validation applies the candidate diff only inside a throwaway copy. The real workspace is not changed, and EOS does not commit, push, open or merge a PR. Gates 3–5 and automatic `Review → Testing → Verified` progression remain unimplemented.

### 4.5 Events

`EOS.Orchestrator.EventMediator` is the entire event backbone: an in-process, **synchronous** `Dictionary<Type, List<Delegate>>`. `Publish` invokes every subscriber for the payload type directly, on the calling thread, in registration order.

There is **no RabbitMQ, no message broker, and no asynchronous dispatch** in this repository, although Constitution Part 5 names RabbitMQ as the eventual transport.

`EventMediator` does not isolate subscriber exceptions. Where a handler must not abort its publisher, `Program.cs` wraps the handler body itself — see `RunLoopIterationSafely`.

Nine event payload types are persisted to SQL Server by an ordinary `EventMediator` subscriber in `Program.cs`: `LoopIterationStarted`, `LoopIterationCompleted`, `LoopIterationEvaluated`, `OperationalModeChanged`, `GoalCreated`, `TaskCreated`, `TaskStarted`, `TaskCompleted`, and `TaskBlocked`.

**Composition Root Adapter Pattern (ADR-015-001).** Subsystems declare narrow publisher/query interfaces in their own project; `Program.cs` implements them against `EventMediator` or against another subsystem's store. This is how, for example, `EOS.Orchestrator` reads a `Plan` without referencing `EOS.Planner` (`PlanStorePlanQueryClient`). Nearly all of `Program.cs` below the top-level statements is these adapters.

### 4.6 Components wired but not driven

A number of real, tested components are constructed in `Program.cs` and immediately discarded into a `_ =` variable because no production caller exists yet: `StageEngine`, `FitnessMonitor`, `StallDetector`, `IntegrityChecker`, `QuarantineClearingService`, `KnowledgeManagementClient`, `MemoryExpirationPolicy`, `RetryManager`, `RollbackManager`, and `AutomationVisibilityPublisher`.

Each is documented in place with the reason. This is deliberate and disclosed, not an oversight. `LoopController.RunIterationAsync` is now reachable directly through one human-triggered `run` invocation as well as reactive `EventMediator` subscriptions, but no `BackgroundService`, timer, scheduler process, or continuous execution driver exists.

---

## 5. Configuration Flow

```
config/*.json  →  JsonConfigurationLoader.Load<T>()  →  <T>Options record  →  used at composition
```

- `JsonConfigurationLoader.Discover()` walks up from `AppContext.BaseDirectory` until it finds `EOS.slnx`, then uses `<root>/config`.
- Deserialization is camelCase, case-insensitive, and **`JsonUnmappedMemberHandling.Disallow`** — an unknown key is an error.
- Every record is then validated with `System.ComponentModel.DataAnnotations`.
- Any failure throws `ConfigurationValidationException`.

Options records live in `src/EOS.SharedKernel/Configuration/`. Adding a configuration field means adding a property there **and** a value in the corresponding `config/*.json` — the loader rejects a key with no matching property, and validation rejects a property whose constraints are unmet.

Secrets never appear in `config/`. The three data-store connection strings come only from `EOS_SQLSERVER_CONNECTION_STRING`, `EOS_REDIS_CONNECTION_STRING`, and `EOS_CHROMADB_ENDPOINT`, read by `DataStoreConnectionOptions.FromEnvironment()`, which throws if any is unset.

---

## 6. Knowledge and Memory Architecture

Three projects, three responsibilities:

- **`EOS.KnowledgeGraph`** owns physical graph storage on SQL Server. `KnowledgeGraphStore` upserts and queries `KnowledgeNode` rows and their metadata; `ArchivedContentStore` holds pre-compression originals.
- **`EOS.VectorStore`** owns embeddings. `ChromaVectorStore` talks to ChromaDB over HTTP.
- **`EOS.Knowledge`** owns the client surfaces and the algorithms above the stores.

### 6.1 `IKnowledgeClient` (Memory Management)

Five methods, all real:

| Method | What it does |
|---|---|
| `UpdateAsync` | The **only** write path into the graph. Knowledge Management mutations route through it — never a direct store write (FR-KM1). |
| `QueryAsync` | Graph query by memory type / domain tags / date range. Passing `Working`, `ShortTerm`, or `Session` throws `NotSupportedException` — those are Redis strategies, not `KnowledgeNode`s. |
| `QuerySimilarAsync` | A purely **symbolic** candidate pool: same `KnowledgeNodeType`, the querying node excluded, ordered by the mechanical ranking formula. No embeddings involved. Bounded by `Thresholds.querySimilarMaxCandidates` (ADR-005). |
| `AssembleContextAsync` | Budgeted, ranked context composition. `IncludesEpisodic`/`IncludesSemantic` are honoured; `IncludesWorking`/`IncludesShortTerm` are structurally present but **inert** (ADR-015-005). `ContextPayload.Truncated` is always truthful. |
| `ConsolidateAsync` | Promotes ephemeral memory into a persistent Episodic node, best-effort embedding indexing, emits `LessonLearned` (unless suppressed) and `MemoryConsolidated`. Idempotent: an already-consolidated source returns `Guid.Empty`. |

### 6.2 `IKnowledgeManagementClient`

`ClassifyAsync`, `GetClassificationAsync`, `AddRelationshipAsync`, `NavigateRelationshipsAsync`, `GetQualityAsync`, `SearchAsync`, `RequestGovernanceActionAsync`, `FindDuplicatesAsync`. Governance actions route through `IProtectionClient.Validate` before taking effect. Duplicate detection uses structural signals only — the semantic `ICompareProvider` is wired to a stub that always reports "not similar".

### 6.3 Ranking

`RankingWeights` (vector similarity, recency, domain match, access frequency) and `KnowledgeRankingWeights` (confidence, reliability, relationship relevance, deprecation penalty) come from `Thresholds.json` and `Knowledge.json` respectively. `RetrievalRanking` implements the mechanical formula.

### 6.4 Compression

`CompressionSweep` archives the original content, summarizes it via `ISummarizer` (backed by `ReasoningEngine.SummarizeAsync`), and replaces the node's content. Eligibility is three delegated checks: pipeline stage ≥ `Pattern`, not read recently, no retention hold. Two of those three adapters are honest stubs in `Program.cs` — `NeverReadRecentlyStub` and `NoActiveRetentionHoldsStub` — because no read-tracking or retention-hold mechanism exists anywhere in the codebase.

---

## 7. Learning and Reasoning Responsibilities

### 7.1 Reasoning (`EOS.Reasoning`)

`ReasoningEngine` implements `IReasoningEngineClient` and runs all 12 stages of `ReasonAsync` in order. Practical notes:

- Exactly **one** inference call per `ReasonAsync`. Multiple hypotheses are produced by asking the model to separate genuinely distinct answers with a `===CANDIDATE===` delimiter, then splitting the response — not by additional calls.
- Stage 3 (Intent Analysis) and Stage 5 (Hypothesis Generation) are deliberate no-ops: no specification-given algorithm exists, and inventing a heuristic was rejected.
- Confidence is derived from context completeness only, starting from a fixed `0.5`. `RiskScore` is always `0`.
- Context is acquired only when `ReasoningRequest.ContextScope` is supplied, via `IContextAcquisitionProvider` — whose only implementation is `KnowledgeContextAcquisitionProvider` in `Program.cs`, because `EOS.Reasoning` may not reference `EOS.Knowledge`.
- `GetTrustSignalAsync` always returns the neutral `0.5` with evidence `"no-history-available"` — no historical track-record source is reachable from this project (AG-0003).
- `query_history()` is **not declared** on the interface at all, per AG-0003.

### 7.2 Learning (`EOS.Learning`)

`Ingestion` is subscribed to `LessonLearned` in `Program.cs` and is genuinely live. From there: `IngestionRateGuard` (windowed rate limiting) → `ClusterTrigger` (uses `QuerySimilarAsync` + `ReasoningEngine.CompareAsync` + `ConfidenceGuard`) → promotion to `Pattern`.

Beyond `Pattern`, `StageEngine` drives the compounding pipeline, gated by `RoiGate`. **`RoiGate` never returns a promotable decision** — no `roi_minimum` value exists in any frozen document (WP-027 Decision 1) — so the `GoldenPath → Automation` transition can never succeed today, and therefore neither can `ReusableComponent` or `PlatformCapability`. Everything before that point is real and callable.

### 7.3 Protection (`EOS.Gates`)

`ProtectionGate.Validate` is synchronous and fails closed. It checks emergency shutdown, then rejects an out-of-range `RiskScore` or a malformed action, then dispatches by risk tier:

- **Low** — allow, record, log only.
- **Medium** — policy check, then allow.
- **High** — the six-step full pipeline: measure the real CPU budget from `IResourceManagementClient` (a measurement failure denies), policy, rules, then Decision Matrix approval routing.

Steps 2–4 of the High-tier pipeline pass unconditionally today because `ActionRequest` carries no context, explanation, or confidence data to check.

---

### 7.4 Senior Engineer execution and Universal Gates

`EOS.SeniorEngineer` is the first role project with production behaviour. A task must explicitly name at least one repository-relative path under `src/` or `tests/`; the role neither crawls the repository nor infers additional files. It reads each named file through the Protection-gated `IWorkspaceClient`, then makes exactly one `ReasonAsync` call whose output contract is one or more strict `[EDIT]` blocks.

Those blocks identify a referenced file, a verbatim unique `SEARCH` region, and its `REPLACE` content. The role applies them only to in-memory copies and passes the original and modified text to `UnifiedDiffBuilder`; the model never supplies hunk headers or line counts. The resulting unified diff must be structurally valid and must pass the workspace client's read-only `git apply --check` before it is registered as `artifact:<sha256>` evidence.

The Execution Coordinator then evaluates Universal Gate 1 (build/static analysis) and Gate 2 (targeted unit tests) in an isolated copy. Both are fail-closed; Gate 2 may be `NotApplicable` when no test project owns or covers the changed paths. Only after the Rule Engine accepts the gate result does the coordinator request `TaskCompletion` Protection and persist `Review`. Gates 3–5 remain deferred, and Review remains a human lifecycle state rather than automatic verification.

---

## 8. Infrastructure Boundaries

- Only `EOS.Infrastructure`, `EOS.KnowledgeGraph`, `EOS.VectorStore`, `EOS.Orchestrator`, `EOS.Planner`, and `EOS.Learning` open connections to a physical store. Each takes a connection string in its constructor and owns its own table via `EnsureTableExistsAsync`.
- `EOS.Dashboard` references `EOS.Contracts` and nothing else; `EOS.Web` references `EOS.Dashboard` and `EOS.Contracts`. Neither can reach infrastructure even by accident — the compiler prevents it.
- `EOS.Reasoning` reaches inference only through `EOS.SDK`'s `IAIProviderClient`; no vendor SDK is referenced anywhere outside `EOS.AIProvider`, and the only provider adapter implemented is Ollama's REST API over plain `HttpClient`.
- `EOS.SeniorEngineer` references only `EOS.Contracts`. `WorkspaceReader` performs read-only access under `src/` and `tests/`; its `git apply --check` operation verifies applicability without modifying the workspace. `ArtifactStore` registers exact UTF-8 content under its lowercase SHA-256 identity. `IsolatedUniversalGateRunner` copies the required build inputs to a throwaway directory, applies the candidate there, and runs Gates 1–2 without passing `EOS_*` runtime credentials to child processes.
- NuGet dependencies are deliberately minimal: `Microsoft.Data.SqlClient`, `Microsoft.Data.Sqlite`, `SQLitePCLRaw.bundle_e_sqlite3`, `StackExchange.Redis`, and `Microsoft.Extensions.Logging.Abstractions`. No ORM, no mediator library, no resilience library, no DI-registration helpers.

---

## 9. Testing Architecture

Test projects are organized by tested responsibility; not every source project has a dedicated test assembly. All use xUnit 2.9.3 with `Microsoft.NET.Test.Sdk` 17.14.1.

`EOS.ArchitectureTests` references **no** projects — it parses `.csproj` XML directly and asserts on the reference graph. Its current named rule inventory is:

| Test | Rule |
|---|---|
| `NoCircularProjectReferencesTests` | R-00: the `src/` reference graph is acyclic |
| `OnlyAllowedProjectsMayReferenceAIProviderTests` | Only whitelisted projects may reference `EOS.AIProvider` |
| `OnlyAllowedProjectsMayReferenceEOSGatesTests` | Only whitelisted projects may reference `EOS.Gates` |
| `SeniorEngineerMayReferenceOnlyContractsTests` | R-02: `EOS.SeniorEngineer` may reference only `EOS.Contracts` |

The remaining Constitution R-01 through R-09 rules are not currently implemented as automated tests merely by virtue of being named in the Constitution.

Three test assemblies disable parallelization because they query shared, real SQL Server tables with no per-test isolation: `EOS.Infrastructure.Tests`, `EOS.Learning.Tests`, `EOS.Orchestrator.Tests` (see each project's `AssemblyInfo.cs`).

Full detail — which tests need which infrastructure, and how to run subsets — is in `docs/guides/Testing-Guide.md`.

---

## 10. Extension Points That Actually Exist

These are the seams the current code genuinely offers. Adding a *new* seam is an architecture decision, not an implementation one — see `docs/guides/Engineering-Workflow-Guide.md`.

1. **A new AI provider adapter.** Implement `EOS.SDK.IAIProviderClient` (and/or `IEmbeddingProviderClient`) alongside `OllamaProviderAdapter`, then add the provider to `config/Providers.json` and register it in the adapter dictionary in `Program.cs`. `ProviderRegistry`, `InferenceRouter`, `HealthMonitor`, and failover already handle multiple providers and models.

2. **A new event.** Declare an `internal sealed record <Name>Payload(...)` in `Program.cs`, declare a narrow `I<Name>EventPublisher` interface in the *producing* subsystem, implement it in `Program.cs` against `EventMediator`, and subscribe consumers with `eventMediator.Subscribe<TPayload>(...)`. This is the established Composition Root Adapter Pattern; ~40 examples already exist in `Program.cs`.

3. **A new bootstrap step.** `BootstrapRunner.AddStep(name, func)` in `BootstrapRunner.CreateEosBootstrap`. Steps run in registration order and stop at the first failure.

4. **A new configuration file or field.** Add the option record to `src/EOS.SharedKernel/Configuration/`, the JSON to `config/`, and a `configurationLoader.Load<T>` call to the bootstrap `Validate` step.

5. **A cross-subsystem read that would otherwise break dependency direction.** Declare a narrow query interface in the *consuming* subsystem and implement it in `Program.cs` against the owning subsystem's store — `IPlanQueryClient`, `IGoalPlanQueryClient`, `ILoopStatusQueryClient`, `ITaskStatusQueryClient`, and `IRecentEventsQueryClient` are all existing instances of exactly this.

6. **A new Dashboard read.** Add a method to `DashboardQueryService` backed by a new `EOS.Contracts` read interface, implement it in `Program.cs`, and map a route in `DashboardWebHost.MapRoutes`.

7. **A new CLI command.** Extend the argument pattern match at the top of `Program.cs` and add a command class under `src/EOS.Runner/Commands/`, following `AskCommand`.

Note what is *not* an extension point: there is no plugin system, no DI-based service registration, no reflection-based discovery, and no runtime configuration reload.

---

## 11. Small Runnable Examples

The examples below use only real types and real namespaces. Each states what it needs.

### 11.1 Load and validate configuration

Requires: the repository checkout (no services).

```csharp
using EOS.Runner.Bootstrap;
using EOS.SharedKernel.Configuration;

var loader = JsonConfigurationLoader.Discover();
var thresholds = loader.Load<ThresholdsOptions>("Thresholds.json");
Console.WriteLine(thresholds.QuerySimilarMaxCandidates);   // 500 with the committed config
```

### 11.2 Publish and consume an event

Requires: nothing.

```csharp
using EOS.Contracts;
using EOS.Orchestrator;

internal sealed record GreetingPayload(string Who);

var mediator = new EventMediator();
mediator.Subscribe<GreetingPayload>(e => Console.WriteLine($"{e.EventType}: {e.Payload.Who}"));

mediator.Publish(EventEnvelope<GreetingPayload>.Create(
    eventType: "Greeting", version: "v1", producer: "Example",
    payload: new GreetingPayload("EOS")));
// Greeting: EOS
```

### 11.3 Gate an action through the Protection Layer

Requires: the in-memory engines shown below plus an `IResourceManagementClient` supplied by the caller. In production that interface is backed by `EOS.Resources`; in a tiny example, provide a test double with the three interface members.

```csharp
using EOS.Contracts;
using EOS.Gates;
using Microsoft.Extensions.Logging.Abstractions;

var gate = new ProtectionGate(
    new PolicyEngine([], [], [], []),
    new RuleEngine(),
    new RiskEngine(),
    new ApprovalEngine(),
    new EmergencyShutdownState(),
    new ResourceCeilings(90, 8192, 476000, 100000, 32000, 4),
    resourceManagementClient,          // caller-supplied IResourceManagementClient
    NullLogger<ProtectionGate>.Instance);

var result = gate.Validate(new ActionRequest(Guid.NewGuid(), "Decision", "HumanOperator", RiskScore: 10));
Console.WriteLine($"{result.Verdict} / {result.Tier}");   // Allow / Low
```

### 11.4 Read and write a knowledge node

Requires: a running SQL Server reachable via `EOS_SQLSERVER_CONNECTION_STRING`.

```csharp
using EOS.Infrastructure;
using EOS.KnowledgeGraph;

var connections = DataStoreConnectionOptions.FromEnvironment();
var store = new KnowledgeGraphStore(connections.SqlServerConnectionString);
await store.EnsureTableExistsAsync(CancellationToken.None);

var node = await store.GetByIdAsync(someNodeId, CancellationToken.None);
Console.WriteLine(node?.Content);
```

For a complete, executable end-to-end walkthrough, see `docs/samples/Hello-EOS/README.md`.

---

## 12. Known Structural Limitations

These are properties of the current repository, each disclosed in the code itself:

- **No continuous execution driver.** A human can trigger one iteration with `run`, and configured events can trigger iterations, but no background cadence repeatedly invokes the loop. Retry and rollback are not automatically chained into complete recovery autonomy.
- **No message broker.** `EventMediator` is synchronous and in-process.
- **No migrations.** Tables are created idempotently at startup by each store.
- **The Learning pipeline cannot reach `Automation` or beyond**, because `RoiGate` has no configured `roi_minimum`.
- **Loop step 12 (Measure Outcomes) is structural only** — no Reality Validation or telemetry KPI mechanism is implemented (WP-028 Decision 7).
- **`LoopStatus.LoopHealthScore` is unconditionally `null`** — no aggregation formula exists for the five KPI families (WP-029 Decision 1).
- **Several event-envelope types still have no producer**: `KnowledgeDriftDetected` and `InferenceRouted`/`InferenceCompleted` as real `EventMediator` envelopes. `TaskCompleted` and `TaskBlocked` now have production publishers in the execution path.
- **The composed embedding channel is not reachable under the committed configuration.** `OllamaEmbeddingAdapter` is real and proven end-to-end by `tests/EOS.Knowledge.Tests/EmbeddingGeneratorIntegrationTests.cs` — but that test constructs the adapter directly. In `Program.cs`, `AIProviderManager` is constructed **without** the optional `embeddingAdapters` dictionary, and `config/Providers.json` declares only the `"Chat"` capability. `AIProviderManager.EmbedAsync` therefore finds no routing candidate and throws `InvalidOperationException("No available provider supports the 'Embeddings' capability.")`. This affects `KnowledgeClient.ConsolidateAsync`, which calls the embedding generator when one is supplied; it does **not** affect the `ask` path, because `UpdateAsync` never embeds.
- **`OperationalModeChanged` has no consumer** in `EOS.Gates` — Runtime Policy mapping is explicitly deferred (WP-029 Decision 4).
