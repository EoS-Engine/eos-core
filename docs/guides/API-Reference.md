# EOS API Reference

**Document Type:** Developer reference (not an architecture document)
**Scope:** The public API surface that is **currently implemented** in this repository. APIs named in the specifications but not yet implemented are listed in §12 rather than described as if they exist.
**Source of truth:** the C# source files linked from each entry. Where a doc comment in the source and this page differ, the source is correct.

Every type below is `public` and reachable from outside its declaring project. Private implementation detail is deliberately omitted.

---

## 1. Reading This Reference

- All async methods take an optional trailing `CancellationToken cancellationToken = default`; it is omitted from the signatures below for brevity **except** where the parameter is required (no default).
- `EOS.Contracts` types are the cross-subsystem language. A type outside `EOS.Contracts` is only reachable by projects that reference its declaring project.
- Constructors matter here: EOS uses **no DI container for its own types**. Everything is constructed explicitly in `src/EOS.Runner/Program.cs`.

---

## 2. Entry Points

### 2.1 `EOS.Runner` — the command line

`src/EOS.Runner/Program.cs`

The primary application entry point and composition-root CLI. It always runs the ten-step bootstrap first, then dispatches on `args`. `EOS.RestoreDrill` (§2.2) is a separate recovery executable, not part of this command surface.

| `args` | Behaviour | Exit code |
|---|---|---|
| *(anything not matching below)* | Bootstrap only | `0` on success, `1` if bootstrap failed |
| `["ask", <text>]` | Bootstrap, then `AskCommand.ExecuteAsync(<text>)` | `AskCommand`'s result |
| `["compress"]` | Bootstrap, then a Protection-gated `CompressionSweep.RunAsync()` | `0`, or `1` if Protection denies |
| `["web"]` | Bootstrap, then `DashboardWebHost.RunAsync(...)` | `0` on graceful shutdown |
| `["run", <engineering-task>]` | Bootstrap, then one human-issued `ManualRequest` loop iteration | `0` when the iteration outcome is `Completed`; otherwise `1` |

```bash
dotnet run --project src/EOS.Runner
dotnet run --project src/EOS.Runner -- ask "explain the SOLID principles"
dotnet run --project src/EOS.Runner -- compress
dotnet run --project src/EOS.Runner -- web
dotnet run --project src/EOS.Runner -- run "Make the requested scoped change in src/EOS.SeniorEngineer/SeniorEngineer.cs."
```

There is no argument parser, no `--help`, and no option flags. The dispatch is a literal C# list pattern. The `run` task must name at least one path under `src/` or `tests/`. It creates candidate evidence and validates it in isolation; it does not apply the candidate to the real workspace or commit, push, or merge it.

### 2.2 `EOS.RestoreDrill` — the disaster-recovery driver

`src/EOS.RestoreDrill/RestoreDrillRunner.cs`

```csharp
public static class RestoreDrillRunner
{
    public static Task<int> RunAsync(string[] args);                       // args: [drillRoot]
    public static Task<IReadOnlyList<BootstrapResult>> RunBootstrapAsync(string configDirectory);
    public static bool IsCompleteSuccess(IReadOnlyList<BootstrapResult> results);
    public static string? RedactSensitiveContent(string? message);
}
```

`RunAsync` expects exactly one argument, a drill root containing a `config/` directory. Exit codes: `2` wrong argument count, `3` config directory missing, `1` bootstrap did not fully succeed, `0` success. It prints one `<StepName>: PASS|FAIL` line per bootstrap step, with `password=`/`pwd=` values redacted by `RedactSensitiveContent` before any error text reaches stdout.

`IsCompleteSuccess` requires exactly 10 results, all successful, with the last named `Ready`.

Normally invoked by `deploy/restore-drill.sh`, not directly.

---

## 3. Bootstrap and Configuration — `EOS.Runner.Bootstrap`

### `IConfigurationLoader`

`src/EOS.Runner/Bootstrap/IConfigurationLoader.cs`

```csharp
public interface IConfigurationLoader
{
    string ConfigDirectory { get; }
    T Load<T>(string fileName) where T : class;
}
```

### `JsonConfigurationLoader`

`src/EOS.Runner/Bootstrap/JsonConfigurationLoader.cs`

```csharp
public sealed class JsonConfigurationLoader(string configDirectory) : IConfigurationLoader
{
    public static JsonConfigurationLoader Discover();
    public string ConfigDirectory { get; }
    public T Load<T>(string fileName) where T : class;
}
```

`Discover()` walks up from `AppContext.BaseDirectory` until it finds `EOS.slnx`, then returns a loader over `<root>/config`.

`Load<T>` deserializes with camelCase naming, case-insensitive matching, and `JsonUnmappedMemberHandling.Disallow`, then validates with `System.ComponentModel.DataAnnotations`. For `ProvidersOptions` it additionally validates each nested `ProviderEntry`.

**Throws** `ConfigurationValidationException` when: the repository root cannot be located; the file does not exist; the file cannot be read (`IOException` inner); the JSON is malformed (`JsonException` inner); the payload deserializes to `null`; or validation fails.

```csharp
using EOS.Runner.Bootstrap;
using EOS.SharedKernel.Configuration;

var loader = JsonConfigurationLoader.Discover();
var inference = loader.Load<InferenceOptions>("Inference.json");
Console.WriteLine(inference.DefaultModel);      // qwen2.5-coder:7b
```

### `BootstrapRunner`

`src/EOS.Runner/Bootstrap/BootstrapRunner.cs`

```csharp
public sealed class BootstrapRunner(ILogger<BootstrapRunner> logger)
{
    public static BootstrapRunner CreateEosBootstrap(IConfigurationLoader loader, ILogger<BootstrapRunner> logger);
    public BootstrapRunner AddStep(string name, Func<CancellationToken, Task> execute);
    public Task<IReadOnlyList<BootstrapResult>> RunAsync(CancellationToken cancellationToken = default);
}
```

`AddStep` returns `this` for chaining. `RunAsync` executes steps in registration order, logs `[n/total] <name> - Success (<ms>ms)` or `- Failed (...)`, and **stops at the first failure** — so the returned list may be shorter than the registered step count.

`CreateEosBootstrap` registers the ten canonical steps: `Install`, `Validate`, `Generate Keys`, `Configure Providers`, `Start Infrastructure`, `Health Check`, `Initialize Knowledge`, `Seed Planner`, `Run Validation`, `Ready`.

### `BootstrapResult` / `BootstrapStep`

```csharp
public sealed record BootstrapResult(
    string StepName, bool Status, DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt, TimeSpan Duration, string? Error);

public sealed class BootstrapStep(string name, Func<CancellationToken, Task> execute)
{
    public string Name { get; }
    public Task ExecuteAsync(CancellationToken cancellationToken);
}
```

### `AskCommand`

`src/EOS.Runner/Commands/AskCommand.cs`

```csharp
public sealed class AskCommand(
    IReasoningEngineClient reasoningEngine,
    IProtectionClient protectionClient,
    IKnowledgeClient knowledgeClient,
    ILogger<AskCommand> logger)
{
    public Task<int> ExecuteAsync(string text, CancellationToken cancellationToken = default);
}
```

Returns `1` when `text` is null/whitespace, when `ReasoningFailedException` is thrown, or when Protection's verdict is not `Allow`. Otherwise persists the decision as a `KnowledgeNodeType.Decision` node, writes `decision.SelectedHypothesis` to stdout, and returns `0`. The requesting role is the constant `"HumanOperator"`.

---

## 4. Configuration Options — `EOS.SharedKernel.Configuration`

Each record maps 1:1 to a file in `config/`. All properties are `required` unless the source says otherwise, and are validated by data annotations.

| Record | File | Selected properties |
|---|---|---|
| `EosOptions` | `EOS.json` | `SystemName`, `Environment`, `Version` |
| `PlannerOptions` | `Planner.json` | `DefaultRiskTolerance`, `ReplanningCadenceMinutes` |
| `InferenceOptions` | `Inference.json` | `DefaultModel`, `MaxTokens`, `Temperature` |
| `ProvidersOptions` | `Providers.json` | `Providers` (`IReadOnlyList<ProviderEntry>`) |
| `ProviderEntry` | *(nested)* | `Name`, `Endpoint`, `Priority`, `Models` (`IReadOnlyList<ModelEntry>`) |
| `ModelEntry` | *(nested)* | `Name`, `Capabilities` |
| `ThresholdsOptions` | `Thresholds.json` | ~70 numeric thresholds — capacity tiers, quotas, ranking weights, retry, `QuerySimilarMaxCandidates` |
| `SecurityOptions` | `Security.json` | `SecretsProvider`, `GlobalPolicies`, `ProjectPolicies`, `UserPolicies`, `RuntimePolicies` |
| `PolicyEntry` | *(nested)* | `ActionType`, `Verdict`, `Reason` |
| `DashboardOptions` | `Dashboard.json` | `Title` |
| `KnowledgeOptions` | `Knowledge.json` | `VectorStoreCollection`, `DependsOnDisallowedTargetTypes`, `GovernanceApprovalRequiredRelationshipTypes`, `FreshnessDecayHalfLifeDays`, `FreshnessTypeWeights`, `FreshnessExpirationThreshold`, four `Ranking*Weight` values |
| `StorageOptions` | `Storage.json` | `DataDirectory` (leading `~` expanded at use) |
| `FeatureFlagsOptions` | `FeatureFlags.json` | `EnableAutonomousLoop` |

```csharp
public sealed class ConfigurationValidationException : Exception   // message + optional inner
```

> Note: `EOS.SharedKernel.Configuration.PolicyEntry` (configuration DTO) and `EOS.Gates.PolicyEntry` (engine input) are **distinct types with the same name**. `Program.cs` maps between them explicitly.

---

## 5. Cross-Subsystem Contracts — `EOS.Contracts`

### 5.1 `IReasoningEngineClient`

`src/EOS.Contracts/IReasoningEngineClient.cs` — implemented by `EOS.Reasoning.ReasoningEngine`.

```csharp
Task<Decision[]> ReasonAsync(ReasoningRequest request, ...);
Task<ConfidenceGuardResult> CompareAsync(PipelineRecord subject, IEnumerable<PipelineRecord> candidates, ...);
Task<TrustSignal> GetTrustSignalAsync(string sourceRole, ...);
Task<Summary> SummarizeAsync(string content, int? sizeBudget = null, ...);
```

- `ReasonAsync` — runs all 12 stages. Returns one `Decision` normally; more than one signals a ranked, tied hypothesis set. **Throws** `ReasoningFailedException` for an unsupported `ReasoningType` (`InternalError`), an empty `Goal` (`InvalidGoal`), insufficient context after expansion (`MissingContext`), or unusable provider output (`InternalError`).
- `CompareAsync` — structural comparison only (shared `KnowledgeGraphRef` or overlapping `DomainTags`). **Throws** `ArgumentException` if `subject` is `Quarantined`, or if any candidate is `Quarantined`/`Archived`.
- `GetTrustSignalAsync` — currently always returns `new TrustSignal(sourceRole, 0.5, "no-history-available")`; no historical source is reachable from `EOS.Reasoning` (see `docs/Architecture-Gaps/AG-0003-WP020-QueryHistory-DataAccess-Gap.md`). **Throws** `ArgumentException` on an empty `sourceRole`.
- `SummarizeAsync` — one real inference call. **Throws** `ArgumentException` on empty content, `ArgumentOutOfRangeException` on a non-positive `sizeBudget`, and `ReasoningFailedException` if the model returns nothing usable or exceeds `sizeBudget`.

`query_history()` is intentionally **not declared** on this interface (AG-0003).

### 5.2 `IProtectionClient`

```csharp
public interface IProtectionClient
{
    ValidationResult Validate(ActionRequest action);   // synchronous
}
```

Implemented by `EOS.Gates.ProtectionGate`.

### 5.3 `IPlanningClient`

```csharp
Task<Plan> SubmitGoalAsync(Goal goal, ...);
Task<GoalStatus> GetGoalStatusAsync(string goalId, ...);
Task CancelGoalAsync(string goalId, string reason, ...);
```

Implemented by `EOS.Planner.PlanningEngine`. `query_generated_tasks`, `pause_workflow`, and `resume_workflow` are intentionally absent — not stubbed, not declared.

### 5.4 `ILoopControlClient`

```csharp
Task<LoopStatus> GetCurrentStatusAsync(...);
Task<ValidationResult> SetOperationalModeAsync(OperationalMode mode, string requestedBy, ...);
Task<ValidationResult> EmergencyStopAsync(string requestedBy, string reason, ...);
```

Implemented by `EOS.Orchestrator.LoopController`. Both write methods route through `IProtectionClient.Validate`; on a non-`Allow` verdict nothing changes and the verdict is returned. `EmergencyStopAsync`'s `reason` is accepted for caller-side traceability only — `ActionRequest` has no field to carry it, so it is not threaded into the Protection call.

### 5.5 `IResourceManagementClient`

```csharp
double GetCurrentBudget(ResourceType resourceType);          // synchronous throughout
CapacityTier GetCurrentTier(ResourceType resourceType);
ModelResidencyStatus GetModelResidency(string modelId);
void RequestBackgroundSlot(string jobId, ResourceClass resourceClass);
```

Implemented by `EOS.Resources.ResourceManagementClient`. `RequestBackgroundSlot` returns `void` by design — the outcome arrives as a `BackgroundJobGranted` or `BackgroundJobDeferred` event.

### 5.6 Dashboard read contracts

```csharp
public interface ILoopStatusQueryClient
{ Task<LoopStatus> GetCurrentStatusAsync(...); }

public interface ITaskStatusQueryClient
{
    Task<IReadOnlyList<DispatchedTask>> GetByStateAsync(TaskLifecycleState state, ...);
    Task<int> CountByStateAsync(TaskLifecycleState state, ...);
}

public interface IRecentEventsQueryClient
{ Task<IReadOnlyList<RecentEventSummary>> GetRecentAsync(int count, ...); }
```

Deliberately narrower than the write-capable interfaces they project. Implemented by adapters in `Program.cs`.

### 5.7 Core DTOs and enums

```csharp
public sealed record EventEnvelope<TPayload>(
    Guid EventId, string EventType, string Version, string Producer,
    Guid CorrelationId, Guid? CausationId, DateTimeOffset OccurredAt, TPayload Payload)
{
    public static EventEnvelope<TPayload> Create(
        string eventType, string version, string producer, TPayload payload,
        Guid? correlationId = null, Guid? causationId = null);
}

public sealed record ReasoningRequest(
    Guid RequestId, Guid CorrelationId, string Goal, string RequestingRole,
    ReasoningType? ReasoningType = null, string[]? Constraints = null,
    ReasoningContextScope? ContextScope = null);

public sealed record ReasoningContextScope(string[]? DomainTags, string[]? ProjectScope, int? Budget);

public sealed record Decision(
    Guid DecisionId, Guid RequestId, ReasoningType ReasoningTypeApplied,
    string SelectedHypothesis, string[] RejectedHypotheses, string[] EvidenceRefs,
    double Confidence, Explanation Explanation, string TradeOffs,
    double RiskScore, bool Reproducible, DateTimeOffset OccurredAt);

public sealed record Explanation(
    string Why, string[] EvidenceUsed, string[] Assumptions,
    (string Hypothesis, string Reason)[] AlternativesRejected,
    string ConfidenceRationale, string[] Risks);

public sealed record ActionRequest(Guid ActionId, string ActionType, string Actor, int RiskScore);
public sealed record ValidationResult(ProtectionVerdict Verdict, RiskTier Tier, string? Reason);

public sealed record Goal(
    Guid GoalId, string Statement, Guid? ParentGoalId, string[] DomainTags,
    string SubmittedByActor, GoalLifecycleState State, Guid? PlanId);

public sealed record Plan(
    Guid PlanId, Guid GoalId, PlanTask[] Tasks, double EstimatedResourceCost,
    double RiskAdjustedConfidenceScore, Guid? PreviousPlanId);

public sealed record PlanTask(
    Guid TaskId, string Description, string[] CompetencyRequirements, Guid[] DependsOnTaskIds);

public sealed record DispatchedTask(
    Guid TaskId, Guid PlanId, Guid GoalId, string Description,
    string[] CompetencyRequirements, Guid[] DependsOnTaskIds, int Priority,
    TaskLifecycleState State, SchedulingMode SchedulingMode,
    DateTimeOffset? NotBefore, DateTimeOffset? RunningAt, bool EventObserved,
    int RetryCount, string? BlockedReason);

public sealed record LoopStatus(Guid? CurrentIterationId, OperationalMode CurrentMode, double? LoopHealthScore);
public sealed record TriggerContext(string TriggerSource, string? SourcePayloadRef);
public sealed record RecentEventSummary(
    Guid EventId, string EventType, string Producer, DateTimeOffset OccurredAt, string PayloadJson);
public sealed record Summary(string Content);
public sealed record TrustSignal(string SourceRole, double Score, string EvidenceRef);
public sealed record GoalStatus(Guid GoalId, GoalLifecycleState State, Guid? PlanId);
public sealed record ModelResidencyStatus(string ModelId, ModelResidencyState State, double? RamFootprintMegabytes);

public sealed class ReasoningFailedException(ReasoningFailureMode failureMode, string message) : Exception
{ public ReasoningFailureMode FailureMode { get; } }
```

Enums: `ProtectionVerdict` (`Allow`, `Deny`, `Defer`, `Retry`) · `RiskTier` (`Low`, `Medium`, `High`) · `ReasoningType` (13 values) · `ReasoningFailureMode` (`MissingContext`, `ConflictingEvidence`, `InvalidGoal`, `AmbiguousRequest`, `UnsupportedTask`, `InternalError`) · `TaskLifecycleState` (13 values) · `GoalLifecycleState` (9 values) · `OperationalMode` (`Manual`, `Assisted`, `SemiAutonomous`, `Autonomous`, `Safe`, `Recovery`, `Learning`, `Maintenance`) · `ResourceType` (7 values) · `ResourceClass` · `CapacityTier` · `SchedulingMode` · `PipelineStage` · `PipelineRecordStatus` · `ModelResidencyState`.

**`TriggerContext.TriggerSource`** accepts exactly seven strings: `UserRequest`, `ManualRequest`, `ScheduledTask`, `LearningOpportunity`, `KnowledgeUpdate`, `PerformanceDegradation`, `Failure`. Anything else throws `ArgumentException` from `LoopController.RunIterationAsync`.

### 5.8 Execution, workspace, and evidence contracts

```csharp
public interface IArtifactRegistryClient
{
    Task<ArtifactRecord> RegisterAsync(
        string type, string producer, string content,
        string? previousVersionHash = null,
        CancellationToken cancellationToken = default);
    Task<ArtifactRecord?> GetByHashAsync(
        string contentHash, CancellationToken cancellationToken = default);
}

public sealed record ArtifactRecord(
    string ContentHash, string Type, string Producer, string Content,
    string? PreviousVersionHash, DateTimeOffset RegisteredAt);

public interface IWorkspaceClient
{
    Task<string?> ReadFileAsync(
        string relativePath, CancellationToken cancellationToken = default);
    Task<PatchApplicabilityResult> CheckPatchAppliesAsync(
        string unifiedDiff, CancellationToken cancellationToken = default);
}

public sealed record PatchApplicabilityResult(bool Applies, string? Error);

public interface ITaskExecutionClient
{
    Task<TaskExecutionResult> ExecuteAsync(
        DispatchedTask task, CancellationToken cancellationToken = default);
}

public sealed record TaskExecutionResult(string[] EvidenceRefs);

public interface IUniversalGateClient
{
    Task<UniversalGateDecision> EvaluateAsync(
        DispatchedTask task, IReadOnlyList<string> evidenceRefs,
        CancellationToken cancellationToken = default);
}

public enum GateStepStatus { Passed, Failed, NotApplicable }
public sealed record GateStepResult(GateStepStatus Status, string? Detail);
public sealed record UniversalGateResult(GateStepResult BuildGate, GateStepResult TestGate);
public sealed record UniversalGateDecision(
    bool Passed, string? FailureReason, UniversalGateResult Result);
```

Artifact identity is the lowercase, 64-character SHA-256 of the content's exact UTF-8 bytes, with no normalization. Re-registering byte-identical content is idempotent; evidence references use `artifact:<sha256>`. `IWorkspaceClient` is read-only and accepts repository-relative forward-slash paths under `src/` or `tests/`. Applicability means only that the diff passes `git apply --check`; it does not establish intent, compilation, or test success.

---

## 6. AI Provider Layer

### 6.1 `EOS.SDK` — the abstraction

`src/EOS.SDK/`

```csharp
public interface IAIProviderClient
{
    Task<InferenceResult> InferAsync(InferenceRequest request, ...);
    CapabilitySet DiscoverCapabilities(string? capabilityFilter) => new([]);   // default impl
}

public interface IEmbeddingProviderClient
{
    Task<Vector> EmbedAsync(string content, ...);
}

public sealed record InferenceRequest(
    Guid RequestId, Guid CorrelationId, string CapabilityRequired, string Payload,
    string? ContextPayloadRef, int TokenBudgetEstimate, int Priority, string Caller);

public sealed record InferenceResult(
    bool Success, string? Output, string? Model, int? PromptTokens, int? CompletionTokens,
    TimeSpan? Latency, InferenceErrorType? ErrorType, string? ErrorMessage);

public sealed record Vector(IReadOnlyList<float> Values);
public sealed record CapabilityEntry(string ProviderName, string ModelName, IReadOnlyList<string> Capabilities);
public sealed record CapabilitySet(IReadOnlyList<CapabilityEntry> Entries);

public enum InferenceErrorType
{ ProviderUnavailable, CapabilityUnsupported, ContextTooLarge, MalformedResponse, Timeout }
```

`InferAsync` reports failure through `InferenceResult.Success == false` plus `ErrorType`/`ErrorMessage` — it does not throw for provider-level failures.

### 6.2 `EOS.AIProvider` — the implementation

```csharp
public sealed record ProviderProfile(string Name, string Endpoint, int Priority, IReadOnlyList<ModelProfile> Models);
public sealed record ModelProfile(string Name, IReadOnlyList<string> Capabilities);
public sealed record HealthThresholds(int FailureThreshold, TimeSpan RecoveryProbeInterval);
public sealed record RoutingCandidate(ProviderProfile Provider, ModelProfile Model);

public sealed class ProviderRegistry(IReadOnlyList<ProviderProfile> providers)
{
    public IReadOnlyList<ProviderProfile> Providers { get; }
    public IReadOnlyList<ProviderProfile> FindByCapability(string capability);
}

public sealed class HealthMonitor(HealthThresholds thresholds, IProviderEventLogger logger)
{
    public bool IsAvailable(string providerName);
    public void RecordSuccess(string providerName, TimeSpan? latency = null);
    public void RecordFailure(string providerName, TimeSpan? latency = null);
}

public sealed class InferenceRouter(ProviderRegistry registry, HealthMonitor healthMonitor)
{
    public IReadOnlyList<RoutingCandidate> Route(string capabilityRequired);
}

public sealed class AIProviderManager(...)          // : IAIProviderClient, IEmbeddingProviderClient
{
    public Task<InferenceResult> InferAsync(InferenceRequest request, ...);
    public CapabilitySet DiscoverCapabilities(string? capabilityFilter);
    public Task<Vector> EmbedAsync(string content, ...);
}

public sealed class OllamaProviderAdapter(HttpClient httpClient, string model, int maxTokens, double temperature)
    : IAIProviderClient;

public sealed class OllamaEmbeddingAdapter(HttpClient httpClient, string model)
    : IEmbeddingProviderClient;

public interface IProviderEventLogger
{
    void LogEvent(string message);
    void LogWarning(string message);
}
```

`AIProviderManager` selects a `RoutingCandidate` via `InferenceRouter`, delegates to the matching adapter keyed by `(providerName, modelName)`, records health outcomes, and fails over to the next candidate.

**Ollama is the only provider adapter implemented.** `OllamaProviderAdapter` posts to `/api/generate`; `OllamaEmbeddingAdapter` posts to `/api/embeddings`.

```csharp
using EOS.AIProvider;
using EOS.SDK;

using var http = new HttpClient { BaseAddress = new Uri("http://localhost:11434") };
var adapter = new OllamaProviderAdapter(http, "qwen2.5-coder:7b", maxTokens: 16, temperature: 0.2);

var result = await adapter.InferAsync(new InferenceRequest(
    RequestId: Guid.NewGuid(), CorrelationId: Guid.NewGuid(),
    CapabilityRequired: "Chat", Payload: "Reply with exactly the word OK and nothing else.",
    ContextPayloadRef: null, TokenBudgetEstimate: 8, Priority: 0, Caller: "EOS.Reasoning"));

Console.WriteLine($"{result.Success} {result.Model} {result.Output}");
```
*Requires a running Ollama with the model pulled. This is the exact shape of `tests/EOS.AIProvider.Tests/OllamaProviderAdapterIntegrationTests.cs`.*

---

## 7. Reasoning — `EOS.Reasoning`

```csharp
public sealed class ReasoningEngine(
    IAIProviderClient aiProviderClient,
    IContextAcquisitionProvider contextAcquisitionProvider,
    ReasoningEngineOptions options,
    IDecisionMadeEventPublisher decisionMadeEventPublisher,
    ILowConfidenceDecisionFlaggedEventPublisher lowConfidenceDecisionFlaggedEventPublisher,
    IContextExpansionRequestedEventPublisher contextExpansionRequestedEventPublisher,
    ILogger<ReasoningEngine> logger) : IReasoningEngineClient
{
    public const int DefaultContextBudget = 2048;
}

public sealed record ReasoningEngineOptions(int ContextExpansionCap, double LowConfidenceFloor);
public sealed record AcquiredContext(IReadOnlyList<string> Items, bool Truncated);

public interface IContextAcquisitionProvider
{ Task<AcquiredContext> AcquireContextAsync(ReasoningContextScope scope, ...); }
```

Publisher interfaces declared here and implemented in `Program.cs`: `IDecisionMadeEventPublisher`, `ILowConfidenceDecisionFlaggedEventPublisher`, `IContextExpansionRequestedEventPublisher`.

Behavioural facts worth knowing before calling it:

- Exactly **one** inference call per `ReasonAsync`.
- Context is acquired **only** when `ReasoningRequest.ContextScope` is non-null.
- `Decision.RiskScore` is always `0`; `Decision.Reproducible` is always `false`.
- `Decision.Confidence` starts at `0.5` and is adjusted only by context completeness.

---

## 8. Protection — `EOS.Gates`

```csharp
public sealed class ProtectionGate(
    PolicyEngine policyEngine, RuleEngine ruleEngine, RiskEngine riskEngine,
    ApprovalEngine approvalEngine, EmergencyShutdownState emergencyShutdownState,
    ResourceCeilings resourceCeilings, IResourceManagementClient resourceManagementClient,
    ILogger<ProtectionGate> logger) : IProtectionClient
{
    public ValidationResult Validate(ActionRequest action);
}

public sealed record PolicyEntry(string ActionType, string Verdict, string Reason);
public sealed record PolicyDecision(bool Allow, string? Reason);
public sealed class PolicyEngine(
    IReadOnlyList<PolicyEntry> global, IReadOnlyList<PolicyEntry> project,
    IReadOnlyList<PolicyEntry> user, IReadOnlyList<PolicyEntry> runtime)
{ public PolicyDecision Evaluate(string actionType); }

public sealed record RuleDecision(bool Allow, string? Reason);
public sealed class RuleEngine { public RuleDecision Evaluate(string actionType); }

public sealed record RiskAssessment(RiskTier Tier, bool Escalated);
public sealed class RiskEngine { /* Assess, RecordAllow, RecordMediumTierDenial */ }

public sealed record ApprovalDecision(ProtectionVerdict Verdict, string? Reason);
public sealed class ApprovalEngine { public ApprovalDecision Resolve(string actionType); }

public sealed class EmergencyShutdownState { /* TryHandleControlAction */ }

public sealed record ResourceCeilings(
    int CpuCeilingPercent, int RamCeilingMegabytes, int DiskCeilingMegabytes,
    int ModelUsageCeilingTokens, int ContextSizeCeilingTokens, int BackgroundTasksCeilingCount);
```

`Validate` **fails closed**: an out-of-range `RiskScore` (outside 0–100), a blank `Actor` or `ActionType`, and a CPU-measurement exception all produce `Deny` at `RiskTier.High`.

Every call logs `Protection validate: ActionId=… ActionType=… Actor=… RiskScore=… Tier=… Verdict=…`.

---

## 9. Knowledge, Memory, and Storage

### 9.1 `IKnowledgeClient` — `EOS.Knowledge`

```csharp
Task UpdateAsync(Guid nodeId, KnowledgeNodeType nodeType, string content,
                 string[] domainTags, string[] evidenceRefs,
                 KnowledgeMetadata? metadata = null, ...);

Task<IEnumerable<KnowledgeNode>> QueryAsync(MemoryType? type, string[]? domainTags, DateRange? range, ...);

Task<IEnumerable<KnowledgeNode>> QuerySimilarAsync(Guid nodeId, ...);

Task<ContextPayload> AssembleContextAsync(ContextRequest request, ...);

Task<Guid> ConsolidateAsync(MemoryRef source, string reason, string[] evidenceRefs,
                            bool suppressLessonLearned = false, ...);
```

- `UpdateAsync` is the **only** write path into the graph. `metadata: null` leaves existing metadata untouched.
- `QueryAsync` **throws `NotSupportedException`** for `MemoryType.Working`, `ShortTerm`, or `Session` — those are Redis strategies, never `KnowledgeNode`s.
- `QuerySimilarAsync` **throws `ArgumentException`** when `nodeId` does not resolve. Returns a purely symbolic candidate pool (same `KnowledgeNodeType`, querying node excluded), ordered by the mechanical ranking formula, **bounded** by `querySimilarMaxCandidates` (ADR-005).
- `AssembleContextAsync` honours `IncludesEpisodic`/`IncludesSemantic`; `IncludesWorking`/`IncludesShortTerm` are structurally present but **inert**. `ContextPayload.Truncated` is always truthful.
- `ConsolidateAsync` is **idempotent** — an already-consolidated source returns `Guid.Empty`, emits no events, and logs a warning.

Constructor:

```csharp
public sealed class KnowledgeClient(
    KnowledgeGraphStore store,
    RankingWeights rankingWeights,
    ChromaVectorStore vectorStore,
    IMemorySourceStore memorySourceStore,
    IContextAssemblyEventPublisher? contextAssemblyEventPublisher = null,
    IEmbeddingGenerator? embeddingGenerator = null,
    ILessonLearnedEventPublisher? lessonLearnedEventPublisher = null,
    IMemoryConsolidatedEventPublisher? memoryConsolidatedEventPublisher = null,
    int querySimilarMaxCandidates = 500) : IKnowledgeClient;
```

The `500` default is an implementation value only; production always passes `Thresholds.QuerySimilarMaxCandidates`.

### 9.2 `IKnowledgeManagementClient` — `EOS.Knowledge`

```csharp
Task ClassifyAsync(Guid nodeId, TaxonomyClassification taxonomy, ...);
Task<TaxonomyClassification?> GetClassificationAsync(Guid nodeId, ...);
Task AddRelationshipAsync(Guid sourceNodeId, RelationshipEdge edge, ...);
Task<IReadOnlyList<RelationshipEdge>> NavigateRelationshipsAsync(Guid nodeId, RelationshipType? type = null, ...);
Task<QualityProfile?> GetQualityAsync(Guid nodeId, ...);
Task<IReadOnlyList<KnowledgeSearchResult>> SearchAsync(SearchRequest request, ...);
Task<ValidationResult> RequestGovernanceActionAsync(Guid nodeId, GovernanceActionType action, string justification, ...);
Task<IReadOnlyList<DuplicateCandidate>> FindDuplicatesAsync(Guid nodeId, ...);
```

`ClassifyAsync`, `AddRelationshipAsync`, `RequestGovernanceActionAsync`, and `FindDuplicatesAsync` **throw `ArgumentException`** when `nodeId` does not resolve; `AddRelationshipAsync` also throws when the edge violates an ontology constraint. `GetQualityAsync` returns `null` for an unknown node. `RequestGovernanceActionAsync` returns Protection's verdict — on a non-`Allow` verdict the action is **not** applied.

### 9.3 Supporting types — `EOS.Knowledge`

```csharp
public sealed record MemoryRef(MemoryType Type, string Key);
public enum MemoryType { Working, ShortTerm, Session, Episodic, Semantic, LongTerm, Project }
public sealed record DateRange(DateTimeOffset? From, DateTimeOffset? To);

public sealed record ContextRequest(
    int TokenOrSizeBudget, bool IncludesWorking, bool IncludesShortTerm,
    bool IncludesEpisodic, bool IncludesSemantic, string[]? ProjectScope,
    DateRange? Filters, Guid? TaskId);
public sealed record ContextPayload(IReadOnlyList<KnowledgeNode> Items, bool Truncated);

public sealed record SearchRequest
{ public MemoryType? Type { get; init; } public string[]? DomainTags { get; init; }
  public DateRange? Range { get; init; } public Guid? RelationshipContextNodeId { get; init; } }

public sealed record KnowledgeSearchResult(KnowledgeNode Node, double Score);
public sealed record DuplicateCandidate(Guid NodeId, string SimilaritySource);
public sealed record RankingWeights(double VectorSimilarity, double Recency, double DomainMatch, double AccessFrequency);
public sealed record KnowledgeRankingWeights(double Confidence, double Reliability, double RelationshipRelevance, double DeprecationPenalty);
public sealed record OntologyValidationResult(bool IsValid, string? Reason);
public enum GovernanceActionType { /* see source */ }

public static class RetrievalRanking { /* the mechanical §19 ranking formula */ }
public sealed class FreshnessCalculator { /* decay half-life + per-taxonomy weights */ }
public sealed class OntologyValidator(...) ;
public sealed class DuplicateDetector(KnowledgeGraphStore store, ICompareProvider compareProvider);
public sealed class MemoryExpirationPolicy(int shortTermExpirationSeconds, int sessionIdleTimeoutSeconds);
public sealed class CompressionSweep(...) { public Task<int> RunAsync(...); }   // returns count compressed
```

Adapter interfaces declared here for the composition root to implement: `IEmbeddingGenerator`, `IMemorySourceStore`, `ISummarizer`, `IReadRecencyTracker`, `IRetentionHoldPolicy`, `ICompareProvider`, `IPipelineStageStore`, `IBackgroundSlotRequester`, plus eight `IKnowledge*EventPublisher` / `IMemory*EventPublisher` interfaces.

### 9.4 `EOS.KnowledgeGraph`

```csharp
public sealed record KnowledgeNode(
    Guid NodeId, KnowledgeNodeType NodeType, string Content, string[] DomainTags,
    string[] EvidenceRefs, DateTimeOffset CreatedAt, KnowledgeMetadata? Metadata = null);

public enum KnowledgeNodeType { Fact, Lesson, Pattern, Decision, Risk }

public sealed class KnowledgeGraphStore(string connectionString)
{
    public Task EnsureTableExistsAsync(CancellationToken cancellationToken);
    public Task UpsertAsync(KnowledgeNode node, CancellationToken cancellationToken);
    public Task ReplaceContentAsync(Guid nodeId, string newContent, CancellationToken cancellationToken);
    public Task<KnowledgeNode?> GetByIdAsync(Guid nodeId, CancellationToken cancellationToken);
    public Task<IReadOnlyList<KnowledgeNode>> QueryAsync(
        IReadOnlyList<KnowledgeNodeType> nodeTypes, DateTimeOffset? createdFrom,
        DateTimeOffset? createdTo, CancellationToken cancellationToken,
        int? maxResults = null, Guid? excludeNodeId = null);
}

public sealed class ArchivedContentStore(string connectionString);   // pre-compression originals
```

Also: `KnowledgeMetadata`, `RelationshipEdge`, `RelationshipType`, `TaxonomyClassification`, `QualityProfile`, `VerificationStatus`, `VersionRecord`, `KnowledgeLifecycleState`.

Note that these `CancellationToken` parameters are **required** — no default.

### 9.5 `EOS.VectorStore`

```csharp
public sealed class ChromaVectorStore(string chromaDbEndpoint)
{
    public Task IndexAsync(Guid id, IReadOnlyList<float> embedding, CancellationToken cancellationToken = default);
}
```

### 9.6 `EOS.Infrastructure`

```csharp
public sealed record DataStoreConnectionOptions(
    string SqlServerConnectionString, string RedisConnectionString, string ChromaDbEndpoint)
{
    public static DataStoreConnectionOptions FromEnvironment();
}
```

**Throws `InvalidOperationException`** naming the first missing variable of `EOS_SQLSERVER_CONNECTION_STRING`, `EOS_REDIS_CONNECTION_STRING`, `EOS_CHROMADB_ENDPOINT`.

```csharp
public sealed record StoreHealthResult(string StoreName, bool Healthy, string? Error);

public sealed class DataStoreHealthChecker(DataStoreConnectionOptions connectionOptions, string sqliteDataDirectory)
{
    public Task<IReadOnlyList<StoreHealthResult>> CheckAllAsync(CancellationToken cancellationToken);
    public Task<StoreHealthResult> CheckSqlServerAsync(CancellationToken cancellationToken);
    public Task<StoreHealthResult> CheckRedisAsync(CancellationToken cancellationToken);
    public Task<StoreHealthResult> CheckChromaDbAsync(CancellationToken cancellationToken);
    public Task<StoreHealthResult> CheckSqliteAsync(CancellationToken cancellationToken);
}

public sealed record StoredEvent(
    Guid EventId, string EventType, string Version, string Producer,
    Guid CorrelationId, Guid? CausationId, DateTimeOffset OccurredAt, string PayloadJson);

public sealed class SqlEventStore(string connectionString)
{
    public Task EnsureTableExistsAsync(CancellationToken cancellationToken);
    public Task AppendAsync(StoredEvent storedEvent, CancellationToken cancellationToken);
    public Task<StoredEvent?> ReadByIdAsync(Guid eventId, CancellationToken cancellationToken);
    public Task<IReadOnlyList<StoredEvent>> GetRecentAsync(int count, CancellationToken cancellationToken);
}

public sealed class RedisMemoryStore(string connectionString)
{
    public Task SetAsync(string key, string value, TimeSpan? timeToLive, CancellationToken cancellationToken = default);
    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);
}

public sealed class ArtifactStore(string connectionString) : IArtifactRegistryClient
{
    public Task EnsureTableExistsAsync(CancellationToken cancellationToken);
    public static string ComputeContentHash(string content);
    public Task<ArtifactRecord> RegisterAsync(
        string type, string producer, string content,
        string? previousVersionHash = null,
        CancellationToken cancellationToken = default);
    public Task<ArtifactRecord?> GetByHashAsync(
        string contentHash, CancellationToken cancellationToken = default);
}

public sealed class WorkspaceReader(string rootDirectory) : IWorkspaceClient
{
    public Task<string?> ReadFileAsync(
        string relativePath, CancellationToken cancellationToken = default);
    public Task<PatchApplicabilityResult> CheckPatchAppliesAsync(
        string unifiedDiff, CancellationToken cancellationToken = default);
    public static bool IsPermittedRelativePath(string relativePath);
}

public sealed class IsolatedUniversalGateRunner
{
    public IsolatedUniversalGateRunner(
        string rootDirectory, TimeSpan? stepTimeout = null,
        string dotnetExecutable = "dotnet");
    public Task<UniversalGateResult> RunAsync(
        string unifiedDiff, CancellationToken cancellationToken = default);
}
```

The health checkers **never throw** for an unreachable store — they return `StoreHealthResult(healthy: false, error: <message>)`. `CheckSqliteAsync` expands a leading `~` in `sqliteDataDirectory` and creates the directory if absent.

---

## 10. Orchestration, Planning, Learning, Resources

### 10.1 `EOS.Orchestrator`

```csharp
public sealed class EventMediator
{
    public void Subscribe<TPayload>(Action<EventEnvelope<TPayload>> handler);
    public void Publish<TPayload>(EventEnvelope<TPayload> envelope);
}
```

**Synchronous and in-process.** `Publish` invokes every subscriber for `TPayload` on the calling thread, in registration order, and **does not isolate exceptions** — a throwing handler propagates back to the publisher. Multiple handlers per payload type are supported. `Publish` for a type with no subscribers is a no-op.

```csharp
public sealed class Scheduler(...)
{
    public void OnTaskCreated(Guid taskId, int priority);
    public Task OnPlannerGeneratedAsync(Guid planId, CancellationToken cancellationToken);
    public Task ScheduleAsync(DispatchedTask task, CancellationToken cancellationToken);
    public Task MarkEventObserved(Guid taskId, CancellationToken cancellationToken);
    public Task<IReadOnlyList<DispatchedTask>> EvaluateReadinessAsync(CancellationToken cancellationToken);
    public Task<DispatchedTask?> SelectNextDispatchableTaskAsync(CancellationToken cancellationToken);
}

public sealed class ExecutionCoordinator(...)
{
    public Task<DispatchResult> DispatchNextAsync(CancellationToken cancellationToken = default);
    public Task<ExecutionResult> ExecuteAndCompleteAsync(
        DispatchedTask task, CancellationToken cancellationToken = default);
}

public sealed record DispatchResult(DispatchOutcome Outcome, DispatchedTask? Task);
public enum ExecutionOutcome { Completed, ProtectionDenied, ExecutionFailed }
public sealed record ExecutionResult(
    ExecutionOutcome Outcome, DispatchedTask Task,
    string[] EvidenceRefs, string? Error);

public sealed class LoopController(...) : ILoopControlClient
{
    public Task RunIterationAsync(TriggerContext trigger, ...);
    public Task<ValidationResult> SetOperationalModeAsync(OperationalMode mode, string requestedBy, ...);
    public Task<ValidationResult> EmergencyStopAsync(string requestedBy, string reason, ...);
    public Task<LoopStatus> GetCurrentStatusAsync(...);
}

public sealed record LoopIteration(...);
public sealed record GoalProgress(...);
public sealed class DispatchedTaskStore(string connectionString);
public sealed class LoopIterationStore(string connectionString) : ILoopIterationStore;
public sealed class OperationalModeStore(string connectionString) : IOperationalModeStore;
public sealed class RetryManager(...);
public sealed class RollbackManager(...);
public sealed class ProgressMonitor(DispatchedTaskStore store, IGoalPlanQueryClient goalPlanQueryClient);
```

Consumer-side query interfaces declared here for `Program.cs` to implement: `IPlanQueryClient`, `IGoalPlanQueryClient`, `IReplanRequestClient`.

`ExecuteAndCompleteAsync` accepts only a `Running` task. Successful role evidence is evaluated by Universal Gates 1–2 and then by the `TaskCompletion` Protection action before the task is persisted as `Review` and `TaskCompleted` is published. Execution, gate, Protection, and in-process cancellation failures are persisted as `Blocked` and publish `TaskBlocked`. Gates 3–5 and automatic progression beyond Review are not implemented.

### 10.2 `EOS.SeniorEngineer`

```csharp
public sealed class SeniorEngineer(
    IReasoningEngineClient reasoningEngineClient,
    IWorkspaceClient workspaceClient,
    IArtifactRegistryClient artifactRegistryClient) : ITaskExecutionClient
{
    public const string RoleName = "SeniorEngineer";
    public const string ProducerName = "EOS.SeniorEngineer";
    public const string EvidenceArtifactType = "Evidence";

    public Task<TaskExecutionResult> ExecuteAsync(
        DispatchedTask task, CancellationToken cancellationToken = default);

    public sealed record EditBlock(string Path, string Search, string Replace);
}
```

The role extracts only explicitly referenced `src/` or `tests/` paths, reads them through `IWorkspaceClient`, and makes one reasoning call requesting strict `[EDIT]` blocks. It applies the blocks to in-memory text and uses local deterministic code to generate and validate the unified diff before checking applicability and registering evidence. It never writes or applies the candidate to the real workspace.

### 10.3 `EOS.Planner`

```csharp
public sealed class PlanningEngine(...) : IPlanningClient
{
    public Task<Plan> SubmitGoalAsync(Goal goal, ...);
    public Task<GoalStatus> GetGoalStatusAsync(string goalId, ...);
    public Task CancelGoalAsync(string goalId, string reason, ...);
    public Task<Plan> ReplanAfterFailureAsync(Guid goalId, ...);   // beyond IPlanningClient
}

public sealed class GoalManager(...);
public sealed class GoalValidator(IProtectionClient protectionClient, IGoalValidatedEventPublisher publisher);
public sealed record GoalValidationResult(bool Feasible, string? Reason);
public sealed class TaskGraphBuilder(IKnowledgeClient knowledgeClient, IReasoningEngineClient reasoningEngineClient);
public sealed class DependencyManager(GoalDependencyStore goalDependencyStore, GoalStore goalStore);
public sealed class PriorityManager;
public sealed class GoalStore(string connectionString);
public sealed class PlanStore(string connectionString);
public sealed class GoalDependencyStore(string connectionString);
```

### 10.4 `EOS.Learning`

```csharp
public sealed class Ingestion(...)
{ public Task OnLessonLearnedAsync(Guid episodicEntryId, string source, ...); }

public sealed class StageEngine(...)
{
    public Task<StagePromotionResult> PromoteToBestPracticeAsync(...);
    public Task<StagePromotionResult> PromoteToPrincipleAsync(...);
    public Task<StagePromotionResult> PromoteToGoldenPathAsync(...);
    public Task<StagePromotionResult> PromoteToAutomationAsync(...);
    public Task<StagePromotionResult> PromoteToReusableComponentAsync(...);
    public Task<StagePromotionResult> PromoteToPlatformCapabilityAsync(...);
}

public sealed record StagePromotionResult(bool Promoted, PipelineStage? ResultingStage, string Reason);
public sealed class RoiGate { /* evaluates RoiEvaluationInput -> RoiGateResult */ }
public sealed record RoiGateResult(RoiGateDecision Decision, double? Score, string Reason);

public sealed class ClusterTrigger(...);
public sealed class ConfidenceGuard;
public sealed class IngestionRateGuard(...);
public sealed class FitnessMonitor(IFitnessFunctionViolatedEventPublisher publisher);
public sealed record FitnessCheckResult(string FitnessFunctionId, double ObservedValue, double Threshold, bool Violated);
public sealed class StallDetector(...);
public sealed class IntegrityChecker(...);
public sealed class QuarantineClearingService(...);
public sealed class FeedbackLoopGuard(...);
public sealed class AutomationVisibilityPublisher(IKnowledgeClient knowledgeClient);
public sealed class PipelineRecordStore(string connectionString) : IPipelineRecordStore;
public sealed class TransitionRecordStore(string connectionString) : ITransitionRecordStore;
public sealed class IngestionRateGuardStore(string connectionString) : IIngestionRateGuardStore;
public sealed class PipelineStageStoreAdapter(IPipelineRecordStore pipelineRecordStore) : IPipelineStageStore;
public static class PipelineStateMachine;
public static class IntegrityHashCalculator;
```

> **Behavioural limit:** `RoiGate` never returns a promotable decision — no `roi_minimum` value exists in any frozen document (WP-027 Decision 1). `PromoteToAutomationAsync` therefore cannot currently succeed, and neither can the two stages beyond it.

### 10.5 `EOS.Resources`

```csharp
public sealed class ResourceMonitor(
    int samplingIntervalSeconds, int modelIdleResidencyTimeoutSeconds,
    IModelLoadedEventPublisher modelLoadedEventPublisher,
    IModelUnloadedEventPublisher modelUnloadedEventPublisher);

public sealed class CapacityManager(CapacityThresholds thresholds, ...);
public sealed record CapacityThresholds(
    ResourceTierBoundaries Cpu, ResourceTierBoundaries Ram, ResourceTierBoundaries Disk,
    ResourceTierBoundaries ModelUsage, ResourceTierBoundaries QueueLength,
    ResourceTierBoundaries BackgroundTasks, ResourceTierBoundaries CacheUsage);
public sealed record ResourceTierBoundaries(double Warning, double Critical, double Emergency);

public sealed class QuotaManager(ResourceClassQuotas quotas, int starvationDenialCountThreshold, int windowSeconds, ...);
public sealed record ResourceClassQuota(double CpuPercent, double RamMegabytes, int ModelSlotCount);
public sealed record ResourceClassQuotas(
    ResourceClassQuota UserRequests, ResourceClassQuota InteractiveSessions,
    ResourceClassQuota AutonomousTasks, ResourceClassQuota BackgroundMaintenance,
    ResourceClassQuota LearningActivities);

public sealed class BackgroundTaskController(Func<CapacityTier> currentCpuTier, QuotaManager quotaManager, ...);

public sealed class ResourceManagementClient(
    ResourceMonitor resourceMonitor, CapacityManager capacityManager,
    BackgroundTaskController backgroundTaskController) : IResourceManagementClient;
```

`ResourceMonitor` samples the **real host** CPU, RAM, and disk.

---

## 11. Dashboard and Web

```csharp
// EOS.Dashboard
public sealed class DashboardQueryService(
    ILoopStatusQueryClient loopStatusQueryClient,
    ITaskStatusQueryClient taskStatusQueryClient,
    IRecentEventsQueryClient recentEventsQueryClient)
{
    public Task<LoopStatus> GetLoopStatusAsync(...);
    public Task<IReadOnlyList<DispatchedTask>> GetTasksByStateAsync(TaskLifecycleState state, ...);
    public Task<int> CountTasksByStateAsync(TaskLifecycleState state, ...);
    public Task<IReadOnlyList<RecentEventSummary>> GetRecentEventsAsync(int count, ...);
}

// EOS.Web
public static class DashboardWebHost
{
    public static Task RunAsync(DashboardQueryService svc, DashboardOptions options, string[] args);
    public static void MapRoutes(IEndpointRouteBuilder app, DashboardQueryService svc, DashboardOptions options);
}
```

Goal lifecycle status is deliberately **absent** from `DashboardQueryService` — no approved read path provides it (WP-030 decision).

### HTTP endpoints

Started with `dotnet run --project src/EOS.Runner -- web`. There is no `launchSettings.json` and no `appsettings.json`, so the host uses the ASP.NET Core default binding — **`http://localhost:5000`** — overridable with `ASPNETCORE_URLS`.

| Route | Returns |
|---|---|
| `GET /` | A minimal HTML page titled from `Dashboard.json`'s `title`, which fetches the three API routes **once** on load. No auto-refresh, no polling, no WebSockets. |
| `GET /api/loop-status` | `LoopStatus` as JSON |
| `GET /api/tasks?state=<TaskLifecycleState>` | `DispatchedTask[]`. `state` is **required** — omitting it is a 400. |
| `GET /api/recent-events?count=<n>` | `RecentEventSummary[]`; `count` defaults to `50` |

Verified responses:

```bash
$ curl -s http://localhost:5000/api/loop-status
{"currentIterationId":"57a3e560-8fea-420e-a4ec-481b26fd4f15","currentMode":1,"loopHealthScore":null}

$ curl -s 'http://localhost:5000/api/recent-events?count=1'
[{"eventId":"141e991d-...","eventType":"SampleEvent","producer":"EOS.Runner.Tests",
  "occurredAt":"2026-09-10T13:15:07.5602478+00:00","payloadJson":"{\"Id\":\"...\",\"Name\":\"approved\"}"}]
```

Enums serialize as **numbers**, not names (`"currentMode":1` is `OperationalMode.Assisted`) — no custom `JsonStringEnumConverter` is configured.

There is **no authentication, no authorization, and no HTTPS redirection** on these endpoints.

---

## 12. Named in the Specifications, Not Currently Implemented

Do not call these — they do not exist as API in this repository:

| Capability | Status |
|---|---|
| `IReasoningEngineClient.query_history()` | Not declared. Deferred per `AG-0003` — no reachable data source. |
| `IPlanningClient.query_generated_tasks()`, `pause_workflow()`, `resume_workflow()` | Not declared. |
| `ILearningEnginePublicApi` | *Not currently implemented / not currently verifiable* — the Learning Engine has no callable client interface; it is reached only through events. |
| Prompt Registry (Constitution Part 9) | No implementation; no `prompts/` directory. |
| Reality Validation (§0.15) / Telemetry KPIs (§16) | Not implemented — this is why Loop step 12 is structural only. |
| Competency Graph (§0.3), Decision Matrix data (§0.6), Daily Reports (§0.9), KPI engine (§0.13) | No implementation. |
| `EOS.Pipeline` | Deferred (Post-v1) by Constitution §1.4. Project skeleton only. |
| Remaining role project APIs (`EOS.CTO`, `EOS.QA`, …) | Nine role projects remain source-empty skeletons; `EOS.SeniorEngineer` is implemented. |
| `EOS.Core`, `EOS.Domain`, `EOS.Application`, `EOS.Tools` | Registered and building; zero source files. |
| `EOS.Mobile` API surface | Unmodified `flutter create` scaffold; no EOS-specific code. |
| RabbitMQ / any message broker | Not present. `EventMediator` is the entire event backbone. |
| A scheduler, daemon, or hosted service driving cycles | Not present. See `docs/Governance-Change-Proposal-001.md` Item 2. |

---

## 13. See Also

- `docs/guides/Developer-Guide.md` — how these pieces fit together
- `docs/samples/Hello-EOS/README.md` — a runnable walkthrough of `ask`
- `docs/guides/Testing-Guide.md` — how each of these is verified
- The subsystem specifications in `docs/` — the authoritative contracts these APIs realize
