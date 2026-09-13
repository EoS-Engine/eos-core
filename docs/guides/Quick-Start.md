# EOS Quick Start Guide

**Document Type:** Developer guide (not an architecture document)
**Audience:** A developer with a fresh checkout who wants a working EOS environment
**Authority:** This guide describes only what the repository currently does. `docs/EOS-Specification.md` (the Constitution) and the subsystem specifications remain authoritative for architecture; `docs/Development-Workflow.md` remains authoritative for process.

This guide takes you from a fresh clone to a verified, running EOS environment and a first real end-to-end execution. Every command below has been executed against this repository.

---

## 1. What You Are Setting Up

EOS is a .NET 10 modular monolith with a single composition root (`src/EOS.Runner`). It depends on four external runtime dependencies:

| Dependency | Purpose | How it runs |
|---|---|---|
| SQL Server 2022 | Knowledge Graph, event store, plan/goal/task/loop persistence | Docker container (`docker-compose.yml`) |
| Redis 7 | Working / Short-term / Session memory store | Docker container (`docker-compose.yml`) |
| ChromaDB | Embedding storage and similarity search | Docker container (`docker-compose.yml`) |
| Ollama | Local LLM inference and embeddings | Host service (not containerized in this repository) |

SQLite requires no provisioning — it is consumed as a library and is exercised only by the bootstrap health check, which creates its probe database under the directory named in `config/Storage.json`.

**Bootstrap will not reach `Ready` unless SQL Server, Redis, ChromaDB, and the SQLite data directory are all reachable.** Ollama is not checked during bootstrap; it is required only for the commands that actually perform inference (`ask`, and indirectly `compress`).

---

## 2. Prerequisites

### 2.1 Supported environment

`docs/Infrastructure-and-Implementation-Roadmap-v1.0.md` targets a single-developer Ubuntu 24.04 LTS workstation, offline-first, and that is the environment the repository's tooling and scripts assume:

- Ubuntu 24.04 LTS (the shell scripts under `deploy/` are `bash`, and `docker-compose.yml` uses Linux bind mounts)
- Docker Engine with the Compose v2 plugin (`docker compose`, not the legacy `docker-compose` v1 binary)
- Git

Other Linux distributions will very likely work; Windows and macOS are *not currently verifiable* from this repository — nothing here has been validated against them.

### 2.2 Required SDK / runtime versions

`global.json` pins the SDK:

```json
{
  "sdk": {
    "version": "10.0.110",
    "rollForward": "latestFeature"
  }
}
```

`Directory.Build.props` sets the target framework for every project:

```xml
<TargetFramework>net10.0</TargetFramework>
```

So you need a **.NET SDK 10.0.1xx** (10.0.110 or a later 10.0.1xx feature band). Verify:

```bash
dotnet --list-sdks
```

The solution file is `EOS.slnx` (the XML solution format), which requires a .NET 10 SDK — an older SDK cannot open it.

`Directory.Build.props` also sets `TreatWarningsAsErrors=true` and `EnableNETAnalyzers=true`, so **any** compiler or analyzer warning fails the build. This is intentional.

---

## 3. Repository Setup

```bash
git clone https://github.com/EoS-Engine/eos-core.git
cd eos-core
```

### 3.1 Environment file

Copy the template and fill in real local values:

```bash
cp .env.example .env
```

`.env` is git-ignored and must never be committed. It declares five variables:

| Variable | Consumed by | Purpose |
|---|---|---|
| `MSSQL_SA_PASSWORD` | `docker-compose.yml` | Initializes the SQL Server container |
| `EOS_SQLSERVER_CONNECTION_STRING` | `EOS.Infrastructure` (`DataStoreConnectionOptions.FromEnvironment`) | SQL Server connection |
| `EOS_REDIS_CONNECTION_STRING` | `EOS.Infrastructure` | Redis connection |
| `EOS_CHROMADB_ENDPOINT` | `EOS.Infrastructure` | ChromaDB base URL |
| `EOS_DATA_DIR` | `docker-compose.yml`, `deploy/backup.sh` | Host directory for the containers' bind-mounted data |

Set `EOS_DATA_DIR` to a real absolute path and create its subdirectories before starting Compose:

```bash
mkdir -p "$HOME/eos/data"/{sql,redis,chroma}
```

> **Important:** `docker compose` reads `.env` automatically. **`EOS.Runner` does not.** It reads the three `EOS_*` connection variables from the *process* environment via `DataStoreConnectionOptions.FromEnvironment()`, which throws if any of them is unset. You must export them into your shell before running the application or the integration tests. See §6.

### 3.2 Start the data stores

```bash
docker compose up -d
docker compose ps
```

Wait until all three services report `healthy` — SQL Server takes tens of seconds on first start (its healthcheck has a 30-second `start_period`).

### 3.3 Install Ollama and pull the configured model

`config/Providers.json` declares exactly one provider and one model, and `config/Inference.json` names the same model as the default:

```json
{ "name": "ollama", "endpoint": "http://localhost:11434", "priority": 1,
  "models": [ { "name": "qwen2.5-coder:7b", "capabilities": ["Chat"] } ] }
```

```bash
curl -fsSL https://ollama.com/install.sh | sh
ollama pull qwen2.5-coder:7b
```

Verify:

```bash
ollama list
curl -s http://localhost:11434/api/tags
```

If you want to use a different model, change **both** `config/Providers.json` and `config/Inference.json` — see §7.

---

## 4. Restore, Build, Test

Run from the repository root; the .NET CLI discovers `EOS.slnx` automatically.

```bash
dotnet restore
dotnet build
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

```bash
dotnet test
```

The test suite is **not** self-contained. Most projects run against the real SQL Server, Redis, ChromaDB, and Ollama, and they **fail** rather than skip when a dependency is unavailable. Export the connection variables first (§6) and make sure Compose and Ollama are up. See `docs/guides/Testing-Guide.md` for the deterministic-versus-infrastructure-dependent breakdown.

---

## 5. Run

`src/EOS.Runner` is the primary application entry point and composition-root CLI. It always executes the ten-step bootstrap sequence first, then dispatches on the command-line arguments. `EOS.RestoreDrill` is a separate recovery executable, not part of this normal command surface.

| Invocation | Behaviour |
|---|---|
| `dotnet run --project src/EOS.Runner` | Bootstrap only, then exit `0` |
| `dotnet run --project src/EOS.Runner -- ask "<question>"` | Bootstrap, then the full Reasoning → Protection → Knowledge slice |
| `dotnet run --project src/EOS.Runner -- compress` | Bootstrap, then a gated memory-compression sweep |
| `dotnet run --project src/EOS.Runner -- web` | Bootstrap, then host the read-only Dashboard |
| `dotnet run --project src/EOS.Runner -- run "<engineering task>"` | Bootstrap, then run one human-issued engineering iteration |

Any other argument list is treated the same as no arguments: bootstrap runs, and the process exits `0` without doing anything else. There is no `--help` and no argument parser — the dispatch is a literal pattern match in `src/EOS.Runner/Program.cs`.

If any bootstrap step fails, the process exits `1` and no command runs.

---

## 6. First Successful Execution

Export the connection variables, then run the bootstrap:

```bash
export EOS_SQLSERVER_CONNECTION_STRING='Server=localhost,1433;Database=master;User Id=sa;Password=<your-password>;TrustServerCertificate=True'
export EOS_REDIS_CONNECTION_STRING='localhost:6379'
export EOS_CHROMADB_ENDPOINT='http://localhost:8000'

dotnet run --project src/EOS.Runner
```

> Do **not** try to load `.env` with `source .env` — `EOS_SQLSERVER_CONNECTION_STRING` contains spaces (`User Id=sa`), and `source` will split it and fail with `User: command not found`. Export the values explicitly, or use a loader that quotes correctly:
>
> ```bash
> while IFS= read -r line; do
>   case "$line" in \#*|"") continue;; esac
>   export "${line%%=*}=${line#*=}"
> done < .env
> ```

### Expected output

```
info: EOS.Runner.Bootstrap.BootstrapRunner[0]
      [1/10] Install - Success (0.85ms)
info: EOS.Runner.Bootstrap.BootstrapRunner[0]
      [2/10] Validate - Success (290.75ms)
info: EOS.Runner.Bootstrap.BootstrapRunner[0]
      [3/10] Generate Keys - Success (0.26ms)
info: EOS.Runner.Bootstrap.BootstrapRunner[0]
      [4/10] Configure Providers - Success (9.31ms)
info: EOS.Runner.Bootstrap.BootstrapRunner[0]
      [5/10] Start Infrastructure - Success (1731.00ms)
info: EOS.Runner.Bootstrap.BootstrapRunner[0]
      [6/10] Health Check - Success (3.41ms)
info: EOS.Runner.Bootstrap.BootstrapRunner[0]
      [7/10] Initialize Knowledge - Success (0.31ms)
info: EOS.Runner.Bootstrap.BootstrapRunner[0]
      [8/10] Seed Planner - Success (0.26ms)
info: EOS.Runner.Bootstrap.BootstrapRunner[0]
      [9/10] Run Validation - Success (0.28ms)
info: EOS.Runner.Bootstrap.BootstrapRunner[0]
      [10/10] Ready - Success (0.49ms)
```

Exact durations vary. Reaching `[10/10] Ready - Success` with exit code `0` is the success condition.

### The full vertical slice

```bash
dotnet run --project src/EOS.Runner -- ask "What is the smallest safe first change to a new codebase?"
```

After the ten bootstrap lines you will see the reasoning, routing, and protection log lines, then the answer on stdout:

```
info: EOS.Reasoning.ReasoningEngine[0]
      ReasoningType EngineeringReasoning pipeline emphasis (§11): Balanced use of all stages
info: EOS.AIProvider.AIProviderManager[0]
      InferenceRouted: ollama/qwen2.5-coder:7b (correlationId=...)
info: EOS.AIProvider.AIProviderManager[0]
      InferenceCompleted: ollama/qwen2.5-coder:7b (43964ms, correlationId=...)
info: EOS.Gates.ProtectionGate[0]
      Protection validate: ActionId=... ActionType=Decision Actor=HumanOperator RiskScore=0 Tier=Low Verdict=Allow
The smallest safe first change to a new codebase is typically:
...
```

The answer text itself is model output and will differ every run. On CPU-only hardware a single inference commonly takes 30–60 seconds; `config/Thresholds.json` sets `inferenceTimeoutSeconds` to `100`.

A walkthrough of what happens inside this command is in `docs/samples/Hello-EOS/README.md`.

### Run one engineering task

With the same datastore variables, healthy services, and configured Ollama model, pass the task as one quoted argument. The task must explicitly name every file it may inspect under `src/` or `tests/`:

```bash
dotnet run --project src/EOS.Runner -- run "Make the requested scoped change in src/EOS.SeniorEngineer/SeniorEngineer.cs"
```

This example demonstrates syntax and scope only; it is not a claim that this particular request was executed successfully. One invocation creates a `ManualRequest`, plans and dispatches a task, and asks `SeniorEngineer` to produce candidate evidence. The role reads only the named paths, turns model `[EDIT]` blocks into a locally generated unified diff, verifies that it applies, and registers it as an `artifact:<sha256>` evidence reference.

Before a normally completed task reaches `Review`, EOS applies the candidate only in an isolated throwaway copy and runs Universal Gate 1 (build/static analysis) and Gate 2 (targeted unit tests). The current outcomes are:

| Outcome | Task/event result | `run` exit |
|---|---|---:|
| Normal task completion | Task reaches `Review`; `Task completed (Review)` is printed and `TaskCompleted` is published | `0` |
| Loop-level Protection denial | Denied before Goal/Task creation; no task event | `1` |
| Task-dispatch Protection denial | Task remains `Ready`; no `TaskBlocked` and no `TaskCompleted` | Currently may be `0`, because the outer iteration can finish as `Completed` without executing the task |
| Failure after the Task reaches `Running` | Task becomes `Blocked`; `Task blocked` is printed and `TaskBlocked` is published; no `TaskCompleted` | `1` for the failed/denied outer iteration; an in-process cancellation is rethrown after Blocked is recorded |

Therefore, exit `0` alone is not proof that an engineering task reached Review: confirm the `Task completed (Review)` output and evidence reference. The dispatch-denial behavior above describes the current implementation; it is not an instruction to bypass Protection.

> **Current safety and autonomy boundary:** EOS does not apply the generated edit to the real workspace, commit it, push it, open or merge a PR, or automatically advance `Review` to `Testing` or `Verified`. Universal Gates 3–5 and complete automatic retry/rollback recovery are not implemented. Human review remains required.

For the architecture see `docs/guides/Developer-Guide.md`; for failures see `docs/guides/Troubleshooting-Guide.md`.

---

## 7. Configuration

All configuration is externalized into ten JSON files under `config/`. They are loaded by `JsonConfigurationLoader`, which locates the repository root by walking up from the running assembly until it finds `EOS.slnx`, then reads `<root>/config`.

| File | Bound to | Notable contents |
|---|---|---|
| `EOS.json` | `EosOptions` | `systemName`, `environment`, `version` |
| `Planner.json` | `PlannerOptions` | `defaultRiskTolerance`, `replanningCadenceMinutes` |
| `Inference.json` | `InferenceOptions` | `defaultModel`, `maxTokens`, `temperature` |
| `Providers.json` | `ProvidersOptions` | Provider list, endpoints, priorities, model capabilities |
| `Thresholds.json` | `ThresholdsOptions` | ~70 numeric thresholds: capacity tiers, quotas, retry, ranking weights |
| `Security.json` | `SecurityOptions` | `secretsProvider` plus four policy lists (all empty by default) |
| `Dashboard.json` | `DashboardOptions` | `title` |
| `Knowledge.json` | `KnowledgeOptions` | Vector store collection, ontology constraints, freshness/ranking weights |
| `Storage.json` | `StorageOptions` | `dataDirectory` (a leading `~` is expanded to the user's home directory) |
| `FeatureFlags.json` | `FeatureFlagsOptions` | `enableAutonomousLoop` |

Loading is strict, and deliberately so:

- **Unknown properties are rejected.** The deserializer uses `JsonUnmappedMemberHandling.Disallow`, so a typo'd or extra key fails the load rather than being ignored.
- **Property names are camelCase**, matched case-insensitively.
- **Every option record is validated** with `System.ComponentModel.DataAnnotations` after deserialization.
- **A missing, unreadable, empty, or malformed file** throws `ConfigurationValidationException` and fails bootstrap step 2 (`Validate`).

Beyond schema validation, bootstrap step 6 (`Health Check`) enforces cross-field invariants in `Thresholds.json`:

- `resourceCriticalPercent` must be greater than `resourceWarningPercent`
- for each of the seven resource types, `Warning < Critical < Emergency`
- for each of the three quota-bearing resource types, the five resource-class quotas must be **non-increasing** by rank (UserRequests ≥ InteractiveSessions ≥ AutonomousTasks ≥ BackgroundMaintenance ≥ LearningActivities)

Violating any of these fails bootstrap with an explicit message naming the offending field.

Secrets never live in `config/`. The three data-store connection strings come exclusively from environment variables.

---

## 8. Important Directories

```text
.
├── src/                  Production source, one project per subsystem/layer
│   └── EOS.Runner/       Primary application entry point and composition root
├── tests/                One test project per implemented source project
│   └── EOS.ArchitectureTests/   Dependency-direction and fitness-rule checks
├── config/               The ten Part 10 configuration files
├── deploy/               backup.sh, restore-drill.sh, restore-drill Compose file
├── docs/                 Constitution, subsystem specifications, ADRs, governance,
│                         work-package plans/reports, and these guides
├── docker-compose.yml    SQL Server + Redis + ChromaDB
├── global.json           SDK pin
├── Directory.Build.props Shared TFM / nullable / warnings-as-errors settings
└── EOS.slnx              Solution file
```

`benchmarks/`, `scripts/`, and `prompts/` are named in the Constitution's Part 1 §1.1 layout but **do not currently exist** in the repository.

---

## 9. Common First-Run Problems

| Symptom | Cause | Fix |
|---|---|---|
| `Required environment variable 'EOS_SQLSERVER_CONNECTION_STRING' is not set.` | The `EOS_*` variables were not exported into the process environment | Export them (§6). `.env` alone is not enough for `EOS.Runner`. |
| `User: command not found` after `source .env` | The connection string contains spaces | Use the quoted loader in §6 or export explicitly |
| `[5/10] Start Infrastructure - Failed ... One or more data stores are unreachable: SQL Server: ...` | Containers not up, still starting, or wrong password | `docker compose ps`; wait for `healthy`; check `MSSQL_SA_PASSWORD` matches the password in the connection string |
| `[5/10] ... ChromaDB: ...` | ChromaDB not reachable at `EOS_CHROMADB_ENDPOINT` | `curl http://localhost:8000/api/v2/heartbeat` |
| `[1/10] Install - Failed: Configuration directory not found` | Running from outside the repository tree, or `EOS.slnx` not found while walking up | Run from the repository root |
| `[2/10] Validate - Failed: Configuration file failed validation` / `is malformed` | A `config/*.json` edit introduced an unknown key, wrong type, or invalid value | Revert the edit; remember unknown properties are rejected |
| `[6/10] Health Check - Failed: ThresholdsOptions...` | A threshold edit broke tier ordering or quota ranking | Restore the ordering invariants (§7) |
| `ask` hangs, then fails with a provider error | Ollama not running, or `qwen2.5-coder:7b` not pulled | `ollama list`; `ollama pull qwen2.5-coder:7b` |
| Build fails on an apparently harmless warning | `TreatWarningsAsErrors=true` | Fix the warning; do not suppress it |
| `dotnet build` cannot open `EOS.slnx` | SDK older than .NET 10 | Install a 10.0.1xx SDK |

More failure modes, with verification commands, are in `docs/guides/Troubleshooting-Guide.md`.

---

## 10. Where To Go Next

- `docs/guides/Developer-Guide.md` — how the codebase is organized and how to work inside it
- `docs/guides/Engineering-Workflow-Guide.md` — the governance and Work Package discipline this project runs on
- `docs/guides/Testing-Guide.md` — how to verify EOS
- `docs/guides/API-Reference.md` — the currently implemented public API surface
- `docs/samples/Hello-EOS/README.md` — the minimal `ask` path through EOS, explained
- §6 above — the separate human-triggered engineering `run` path
