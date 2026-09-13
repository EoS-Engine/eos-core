# Hello EOS — The Smallest Meaningful Path Through the System

**Document Type:** Sample / walkthrough
**Status:** Every command, output, and query on this page was executed against this repository on 2026-09-10.

This sample takes you through the smallest `ask` path that genuinely exercises EOS end to end: a question in, a real local model inference, a real Protection Layer verdict, a real row in the Knowledge Graph, and an answer out. It is intentionally separate from the newer `run "<engineering task>"` path.

---

## 1. Why There Is No Separate Sample Project

**The honest answer first, because it shapes everything below.**

A conventional "hello world" sample would be a new `.csproj` referencing the EOS libraries. **This repository cannot have one**, and creating one would violate the architecture this sample is supposed to demonstrate:

1. **Constitution Part 1 §1.1** fixes the solution structure. It enumerates the top-level directories (`src/`, `tests/`, `benchmarks/`, `docs/`, `config/`, `scripts/`, `deploy/`, `prompts/`) and every project in `src/`. A `samples/` tree, or a 34th project, is not among them.
2. **Constitution Part 1 §1.2** assigns an owning role and a permitted dependency set to every project. A new project has neither.
3. **Fitness rule R-09** requires that "every new project declares an owning role in Part 1 ownership table." Satisfying it would mean editing the Constitution — which requires the formal amendment process of §0.1.3 (an ADR tagged `constitutional`, CTO and Principal Engineer review, a passing fitness run, and a Knowledge Graph entry).
4. **Constitution Part 1 §1.3** states that `EOS.Runner` is the **only** project allowed to reference everything — it is the single composition root. A sample project wiring up `ReasoningEngine`, `ProtectionGate`, and `KnowledgeClient` together would be a *second* composition root.

`docs/Development-Workflow.md` §6 is explicit that new structure requires an approved plan, and this is a documentation task. So this sample **adds no project, no file to `src/`, and no dependency.**

**What it does instead:** the smallest meaningful path through EOS already exists and already ships — it is the `ask` command. That is not a workaround. `docs/Infrastructure-and-Implementation-Roadmap-v1.0.md` Phase 5 defines this exact path as the system's first vertical slice, with `eos ask "<question>"` as its named entry point:

> Prove the smallest possible end-to-end path through the real architecture — User Request → AI Provider → Reasoning → Memory → Response.

This sample is a guided run of that slice, with the complete source of every component it touches.

---

## 2. Setup

Complete `docs/guides/Quick-Start.md` first. The specific preconditions for this sample:

| Requirement | Check |
|---|---|
| .NET SDK 10.0.1xx | `dotnet --list-sdks` |
| SQL Server, Redis, ChromaDB running | `docker compose up -d && docker compose ps` — all three `healthy` |
| Ollama running with the configured model | `ollama list` shows `qwen2.5-coder:7b` |
| The three connection variables exported | `env \| grep '^EOS_'` shows three entries |

```bash
cd <repository-root>

export EOS_SQLSERVER_CONNECTION_STRING='Server=localhost,1433;Database=master;User Id=sa;Password=<your-password>;TrustServerCertificate=True'
export EOS_REDIS_CONNECTION_STRING='localhost:6379'
export EOS_CHROMADB_ENDPOINT='http://localhost:8000'

dotnet build
```

> Do **not** use `source .env` — the SQL Server connection string contains spaces, and `source` will truncate it. See `docs/guides/Troubleshooting-Guide.md` §5.2.

---

## 3. Run It

```bash
dotnet run --project src/EOS.Runner -- ask "Reply with exactly the word OK and nothing else."
```

A short, highly constrained question is used deliberately: it keeps the model's output to a single token so the interesting part — the pipeline around it — is easy to read.

### Expected output

```
info: EOS.Runner.Bootstrap.BootstrapRunner[0]
      [1/10] Install - Success (0.8505ms)
info: EOS.Runner.Bootstrap.BootstrapRunner[0]
      [2/10] Validate - Success (290.7503ms)
info: EOS.Runner.Bootstrap.BootstrapRunner[0]
      [3/10] Generate Keys - Success (0.2623ms)
info: EOS.Runner.Bootstrap.BootstrapRunner[0]
      [4/10] Configure Providers - Success (9.3111ms)
info: EOS.Runner.Bootstrap.BootstrapRunner[0]
      [5/10] Start Infrastructure - Success (1731.0001ms)
info: EOS.Runner.Bootstrap.BootstrapRunner[0]
      [6/10] Health Check - Success (3.4098ms)
info: EOS.Runner.Bootstrap.BootstrapRunner[0]
      [7/10] Initialize Knowledge - Success (0.3087ms)
info: EOS.Runner.Bootstrap.BootstrapRunner[0]
      [8/10] Seed Planner - Success (0.1611ms)
info: EOS.Runner.Bootstrap.BootstrapRunner[0]
      [9/10] Run Validation - Success (0.1887ms)
info: EOS.Runner.Bootstrap.BootstrapRunner[0]
      [10/10] Ready - Success (0.0796ms)
info: EOS.Reasoning.ReasoningEngine[0]
      ReasoningType EngineeringReasoning pipeline emphasis (§11): Balanced use of all stages
info: EOS.AIProvider.AIProviderManager[0]
      InferenceRouted: ollama/qwen2.5-coder:7b (correlationId=748e8670-0db3-4321-9189-aa7b3a27edd2)
info: EOS.AIProvider.AIProviderManager[0]
      InferenceCompleted: ollama/qwen2.5-coder:7b (9683ms, correlationId=748e8670-0db3-4321-9189-aa7b3a27edd2)
info: EOS.Gates.ProtectionGate[0]
      Protection validate: ActionId=3df8153a-bcba-42bc-9c72-9a36e2c81f2d ActionType=Decision Actor=HumanOperator RiskScore=0 Tier=Low Verdict=Allow
OK
```

Exit code `0`. Total wall clock on the reference machine: **19.9 s**, of which 9.7 s was the model.

**What will differ on your machine:** every GUID, every duration, and — for any less constrained question — the answer text, which is model output. **What will not differ:** the ten bootstrap steps in that order, the four log lines after them in that order, the answer on its own line at the end, and exit code `0`.

---

## 4. Prove It Actually Persisted

The answer on stdout is the least interesting part. The point of the slice is that the decision became a real, queryable row in the Knowledge Graph — not a log line.

Take the `ActionId` from the Protection log line (it **is** the `DecisionId`, and it becomes the `NodeId`) and query SQL Server directly:

```bash
PW=$(grep '^MSSQL_SA_PASSWORD=' .env | cut -d= -f2-)

docker exec eos-sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "$PW" -C -d master -W -s "|" \
  -Q "SET NOCOUNT ON; SELECT TOP 1 NodeId, NodeType, Content, EvidenceRefsJson, CreatedAt
      FROM KnowledgeNode WHERE NodeType='Decision' ORDER BY CreatedAt DESC;"
```

### Actual result from the run above

```
NodeId|NodeType|Content|EvidenceRefsJson|CreatedAt
------|--------|-------|----------------|---------
3DF8153A-BCBA-42BC-9C72-9A36E2C81F2D|Decision|OK|["inference:8dac4c62-b9bb-4fe5-bf04-96322d26afa1"]|2026-09-10 13:36:54.5117773 +00:00
```

Three things to notice:

- **`NodeId` equals the `ActionId` in the log line.** `AskCommand` passes `decision.DecisionId` as the node id, so the audit trail from log to database is direct.
- **`EvidenceRefsJson` is a real evidence reference** — `inference:<RequestId>` — pointing at the specific inference call that produced the content. This is Constitution §0.1.1.1 ("Evidence over assertion") realized at the smallest possible scale.
- **`NodeType` is `Decision`**, one of the five `KnowledgeNodeType` values, written through the real schema — not a throwaway table.

---

## 5. What Happens Internally

### 5.1 The whole path

```
dotnet run --project src/EOS.Runner -- ask "<question>"
│
├─ Program.cs
│    ├─ Host.CreateApplicationBuilder(args).Build()      // ILogger<T> only — no DI for EOS types
│    ├─ JsonConfigurationLoader.Discover()               // walks up to EOS.slnx, reads <root>/config
│    ├─ BootstrapRunner.CreateEosBootstrap(...).RunAsync()
│    │     └─ 10 steps; any failure ⇒ exit 1, nothing else runs
│    ├─ pattern-match args ⇒ ["ask", text]
│    └─ construct the object graph by hand, then:
│
└─ AskCommand.ExecuteAsync(text)
     │
     ├─ 1. new ReasoningRequest(RequestId, CorrelationId, Goal: text, RequestingRole: "HumanOperator")
     │
     ├─ 2. ReasoningEngine.ReasonAsync(request)          // all 12 stages, in order
     │       ├─ Stage 1  Context Processing              — skipped: ContextScope is null
     │       ├─ Stage 2  Goal Understanding              — rejects empty goals
     │       ├─ Stage 3  Intent Analysis                 — deliberate no-op (no spec'd algorithm)
     │       ├─ Stage 4  Constraint Evaluation           — none supplied
     │       ├─ Stage 5  Hypothesis Generation           — realized by the ===CANDIDATE=== split
     │       ├─ Stage 6  Multi-Step Reasoning            — exactly ONE inference call
     │       │      └─ AIProviderManager.InferAsync
     │       │            ├─ InferenceRouter.Route("Chat")     → ranked candidates by priority
     │       │            ├─ HealthMonitor.IsAvailable(...)    → filters unhealthy providers
     │       │            ├─ OllamaProviderAdapter.InferAsync  → POST localhost:11434/api/generate
     │       │            └─ HealthMonitor.RecordSuccess(...)
     │       ├─ Stage 7  Decision Making                 — selects hypotheses[0]
     │       ├─ Stage 8  Alternative Exploration         — empty (single hypothesis)
     │       ├─ Stage 9  Trade-off Analysis              — reported as not applicable
     │       ├─ Stage 10 Confidence Evaluation           — 0.5 (no context requested)
     │       ├─ Stage 11 Explainability                  — builds the Explanation record
     │       └─ Stage 12 Decision Validation             — evidence non-empty, explanation non-blank
     │
     ├─ 3. new ActionRequest(decision.DecisionId, "Decision", "HumanOperator", RiskScore: 0)
     │      └─ ProtectionGate.Validate(...)              // fails closed on anything unexpected
     │            └─ RiskEngine.Assess ⇒ Low ⇒ allow + record + log
     │
     ├─ 4. KnowledgeClient.UpdateAsync(nodeId: decision.DecisionId, nodeType: Decision, ...)
     │      └─ KnowledgeGraphStore.UpsertAsync ⇒ INSERT/UPDATE on dbo.KnowledgeNode
     │
     └─ 5. Console.WriteLine(decision.SelectedHypothesis) ⇒ exit 0
```

### 5.2 The complete source of the command

This is `src/EOS.Runner/Commands/AskCommand.cs` in full — the whole sample is 60 lines of real production code:

```csharp
using EOS.Contracts;
using EOS.Knowledge;
using EOS.KnowledgeGraph;
using Microsoft.Extensions.Logging;

namespace EOS.Runner.Commands;

public sealed class AskCommand(
    IReasoningEngineClient reasoningEngine,
    IProtectionClient protectionClient,
    IKnowledgeClient knowledgeClient,
    ILogger<AskCommand> logger)
{
    private const string RequestingRole = "HumanOperator";

    public async Task<int> ExecuteAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            logger.LogError("Malformed request: no text was provided to 'ask'.");
            return 1;
        }

        var request = new ReasoningRequest(
            RequestId: Guid.NewGuid(),
            CorrelationId: Guid.NewGuid(),
            Goal: text,
            RequestingRole: RequestingRole);

        Decision decision;
        try
        {
            var decisions = await reasoningEngine.ReasonAsync(request, cancellationToken);
            decision = decisions[0];
        }
        catch (ReasoningFailedException ex)
        {
            logger.LogError("Reasoning failed: {FailureMode} - {Message}", ex.FailureMode, ex.Message);
            return 1;
        }

        var actionRequest = new ActionRequest(
            ActionId: decision.DecisionId,
            ActionType: "Decision",
            Actor: request.RequestingRole,
            RiskScore: (int)Math.Round(decision.RiskScore));

        var validationResult = protectionClient.Validate(actionRequest);

        if (validationResult.Verdict != ProtectionVerdict.Allow)
        {
            logger.LogError(
                "Decision {DecisionId} was not allowed: {Verdict} - {Reason}",
                decision.DecisionId, validationResult.Verdict, validationResult.Reason);
            return 1;
        }

        await knowledgeClient.UpdateAsync(
            nodeId: decision.DecisionId,
            nodeType: KnowledgeNodeType.Decision,
            content: decision.SelectedHypothesis,
            domainTags: [],
            evidenceRefs: decision.EvidenceRefs,
            cancellationToken: cancellationToken);

        Console.WriteLine(decision.SelectedHypothesis);
        return 0;
    }
}
```

Note what this class depends on: **three interfaces from `EOS.Contracts`/`EOS.Knowledge`, and nothing else.** It never names `ReasoningEngine`, `ProtectionGate`, `AIProviderManager`, `KnowledgeGraphStore`, or `OllamaProviderAdapter`. The composition root supplies the implementations. That is the architecture's central rule in a single readable file — which is exactly why this makes a better sample than a new project would.

### 5.3 The wiring that makes it work

`src/EOS.Runner/Program.cs` constructs the graph by hand. The parts this path touches:

```csharp
// AI Provider Layer — one Ollama adapter per (provider, model) from config/Providers.json
var providerRegistry = new ProviderRegistry(providerProfiles);
var healthMonitor    = new HealthMonitor(healthThresholds, new LoggerProviderEventLogger(...));
var inferenceRouter  = new InferenceRouter(providerRegistry, healthMonitor);
var aiProviderClient = new AIProviderManager(
    inferenceRouter, healthMonitor, adapters, new LoggerProviderEventLogger(...),
    providerRegistry: providerRegistry);

// Protection Layer
var protectionGate = new ProtectionGate(
    policyEngine, ruleEngine, riskEngine, approvalEngine,
    emergencyShutdownState, resourceCeilings, resourceManagementClient, logger);

// Knowledge / Memory
var knowledgeClient = new KnowledgeClient(
    knowledgeGraphStore, rankingWeights, vectorStore, new RedisMemorySourceStore(redisMemoryStore),
    new EventMediatorContextAssemblyEventPublisher(eventMediator), embeddingGenerator,
    new EventMediatorLessonLearnedEventPublisher(eventMediator),
    new EventMediatorMemoryConsolidatedEventPublisher(eventMediator),
    thresholdsOptions.QuerySimilarMaxCandidates);

// Reasoning — note IContextAcquisitionProvider: EOS.Reasoning may not reference EOS.Knowledge,
// so the composition root bridges them (ADR-015-001, Composition Root Adapter Pattern).
var reasoningEngine = new ReasoningEngine(
    aiProviderClient,
    new KnowledgeContextAcquisitionProvider(knowledgeClient),
    new ReasoningEngineOptions(
        ContextExpansionCap: thresholdsOptions.ReasoningContextExpansionCap,
        LowConfidenceFloor: thresholdsOptions.ReasoningLowConfidenceFloor),
    /* three event publishers */ ..., logger);

// The command itself
var askCommand = new AskCommand(reasoningEngine, protectionGate, knowledgeClient, logger);
return await askCommand.ExecuteAsync(args[1]);
```

### 5.4 Which configuration values this path reads

| Value | File | Effect on this run |
|---|---|---|
| `providers[].endpoint` | `Providers.json` | `http://localhost:11434` — the `HttpClient` base address |
| `providers[].models[].name` / `.capabilities` | `Providers.json` | `qwen2.5-coder:7b` advertising `Chat`; routing matches on `Chat` |
| `defaultModel`, `maxTokens`, `temperature` | `Inference.json` | Passed to `OllamaProviderAdapter` |
| `inferenceTimeoutSeconds` | `Thresholds.json` | `100` — the `HttpClient` timeout |
| `providerFailureThreshold`, `providerRecoveryProbeIntervalSeconds` | `Thresholds.json` | `3` / `30` — `HealthMonitor` failover policy |
| `reasoningContextExpansionCap`, `reasoningLowConfidenceFloor` | `Thresholds.json` | `1` / `0.3` |
| `globalPolicies` … `runtimePolicies` | `Security.json` | All empty ⇒ `PolicyEngine` allows |
| `querySimilarMaxCandidates` | `Thresholds.json` | `500` — passed to `KnowledgeClient` (unused on this path) |

---

## 6. Try These Variations

All use the same, unmodified repository.

**A different question** — with a longer answer, you may see the model emit multiple candidates separated by `===CANDIDATE===`, which `ReasoningEngine` splits into a ranked, tied `Decision[]`. `AskCommand` prints `decisions[0]`:

```bash
dotnet run --project src/EOS.Runner -- ask "What is the smallest safe first change to a new codebase?"
```

**Trigger the malformed-request path** — exercises the first guard clause, returns `1`, and never reaches the model:

```bash
dotnet run --project src/EOS.Runner -- ask "   "
# Malformed request: no text was provided to 'ask'.
```

**See the decision on the Dashboard** — `/api/recent-events` reads the same SQL Server the decision was written to:

```bash
dotnet run --project src/EOS.Runner -- web
# then, in another shell:
curl -s http://localhost:5000/api/loop-status
curl -s 'http://localhost:5000/api/recent-events?count=5'
```

**Bootstrap only** — the fastest way to confirm the whole environment is healthy:

```bash
dotnet run --project src/EOS.Runner
```

---

## 7. What This Sample Deliberately Does Not Show

Stated plainly so nothing here is mistaken for a complete picture of EOS:

- **No planning or engineering execution in this sample.** `ask` does not create a `Goal`, a `Plan`, or a `DispatchedTask`. `PlanningEngine`, `Scheduler`, and `ExecutionCoordinator` are all constructed in `Program.cs` but are not on this path. The separate `run` command triggers one human-issued engineering iteration; follow the [Quick Start](../../guides/Quick-Start.md#run-one-engineering-task) for that workflow.
- **No autonomous loop on the `ask` path.** Nothing in this sample calls `LoopController.RunIterationAsync` or fires one of its configured triggers.
- **No learning.** The Learning Engine's `Ingestion` is subscribed to `LessonLearned`, which `ask` never publishes.
- **No context assembly.** `AskCommand` supplies no `ContextScope`, so Stage 1 is skipped and the Knowledge Graph is never *read* — only written.
- **No embeddings.** `UpdateAsync` does not embed. (`AIProviderManager.EmbedAsync` is in any case not reachable under the committed configuration — see `docs/guides/Troubleshooting-Guide.md` §7.3.)
- **No event persistence.** None of the event types wired to `SqlEventStore` is published on this path, so `/api/recent-events` will not show this decision — it shows *events*, and `ask` produces a *node*.
- **Confidence and risk are not meaningful yet.** `Decision.Confidence` is a fixed `0.5` when no context is requested, and `Decision.RiskScore` is always `0`.

For where each of those actually lives, see `docs/guides/Developer-Guide.md`.

---

## 8. Troubleshooting This Sample

| Symptom | See |
|---|---|
| `Required environment variable 'EOS_...' is not set.` | Troubleshooting §5.1 |
| `User: command not found` after `source .env` | Troubleshooting §5.2 |
| `[5/10] Start Infrastructure - Failed` | Troubleshooting §5.3 |
| `Reasoning failed: InternalError - ...` | Troubleshooting §7.1 |
| The command exits `0` immediately, printing nothing | Troubleshooting §6.1 — the argument list did not match; quote the question as one argument |
| Inference takes 30–60 s | Expected on CPU-only hardware; the budget is 100 s |

Full guide: `docs/guides/Troubleshooting-Guide.md`.

---

## 9. Related Reading

- [Quick Start](../../guides/Quick-Start.md) — getting to a working environment and using the separate `run` command
- `docs/guides/Developer-Guide.md` — how the whole system is organized
- `docs/guides/API-Reference.md` — the interfaces used above, in detail
- `docs/Infrastructure-and-Implementation-Roadmap-v1.0.md` Phase 5 — the original definition of this vertical slice
- `docs/Reasoning-Engine-Specification-v1.0.md` §10–§13 — the 12 stages
- `docs/Protection-Layer-Specification-v1.0.md` §14 — the tiered validation pipeline
- `docs/Memory-Management-Specification-v1.0.md` §20.1 — `IKnowledgeClient`
