# EOS Troubleshooting Guide

**Document Type:** Developer guide (not an architecture document)
**Scope:** Failure modes that follow from how this repository actually behaves. Every entry names the code path that produces it.

Nothing in this guide is speculative. If a failure mode is not listed here, that does not mean it cannot happen — it means it was not established from the repository.

**When to escalate** is stated per entry. The general rule from `docs/Development-Workflow.md` §8: a verification step that cannot run because a real dependency is unavailable is reported as **not run** — never silently skipped, never reported as passing. Do not work around a failing gate; report it.

---

## 1. First-Response Checklist

Run this before investigating anything specific. It separates "the environment is wrong" from "the code is wrong" in about thirty seconds.

```bash
cd <repository-root>

dotnet --list-sdks                                   # expect a 10.0.1xx SDK
docker compose ps                                    # expect 3 services, all healthy
for name in EOS_SQLSERVER_CONNECTION_STRING EOS_REDIS_CONNECTION_STRING EOS_CHROMADB_ENDPOINT; do
  test -n "${!name:-}" && echo "$name is set" || echo "$name is not set"
done
curl -s http://localhost:8000/api/v2/heartbeat       # ChromaDB
docker exec eos-redis redis-cli ping                 # expect PONG
ollama list                                          # expect qwen2.5-coder:7b
git status                                           # expect a clean, expected working tree
```

Then the single most informative command in the repository:

```bash
dotnet run --project src/EOS.Runner
```

Bootstrap is ten ordered steps that stop at the first failure, and **the number of the failing step tells you where the problem is**:

| Step | Name | What a failure here means |
|---|---|---|
| 1 | Install | The `config/` directory could not be found |
| 2 | Validate | A `config/*.json` file is missing, malformed, or fails validation |
| 3 | Generate Keys | `Security.json` did not load |
| 4 | Configure Providers | No providers declared, or a provider endpoint is not an absolute URI |
| 5 | Start Infrastructure | SQL Server, Redis, ChromaDB, or SQLite is unreachable |
| 6 | Health Check | A `Thresholds.json` ordering invariant is violated |
| 7 | Initialize Knowledge | `Knowledge.json`'s `vectorStoreCollection` is empty |
| 8 | Seed Planner | `Planner.json`'s `replanningCadenceMinutes` is not greater than zero |
| 9 | Run Validation | Some option object did not load (should not occur if 2–8 passed) |
| 10 | Ready | — |

---

## 2. Repository Setup

### 2.1 `dotnet build` cannot open the solution

**Symptom.** The CLI does not recognize `EOS.slnx`, or reports that no project or solution file was found.

**Likely cause.** An SDK older than .NET 10. `EOS.slnx` is the XML solution format, which .NET 10 introduced.

**Verify.**
```bash
dotnet --list-sdks
cat global.json
```

**Resolution.** Install a 10.0.1xx SDK. `global.json` pins `10.0.110` with `rollForward: latestFeature`, so any 10.0.1xx feature band satisfies it.

**Escalate** if a correct SDK is installed and the solution still will not load — that is a toolchain problem, not a repository problem.

### 2.2 A directory named in the Constitution does not exist

**Symptom.** `benchmarks/`, `scripts/`, or `prompts/` are absent, though Constitution Part 1 §1.1 lists them.

**Likely cause.** They have never been created. No Work Package produced them.

**Resolution.** None needed. This is the current, expected state. Do not create them speculatively — that would be new structure without an approved plan.

---

## 3. Restore and Build

### 3.1 Build fails on a warning

**Symptom.** `dotnet build` reports errors that read like warnings (`CS…`, `CA…`, `IDE…`).

**Likely cause.** `Directory.Build.props` sets `TreatWarningsAsErrors=true` and `EnableNETAnalyzers=true` for every project. Every warning is a build failure by design.

**Verify.**
```bash
dotnet build 2>&1 | grep -E "warning|error"
```

**Resolution.** Fix the underlying issue. Do **not** add a suppression, a `#pragma`, or a `NoWarn` entry — that is a change to the project's quality posture and needs approval, not a local workaround.

**Escalate** if the only fix would require suppressing an analyzer.

### 3.2 Restore fails offline

**Symptom.** `dotnet restore` cannot reach a NuGet feed.

**Likely cause.** EOS is designed offline-first at *runtime*, but restore itself needs the packages present — five of them: `Microsoft.Data.SqlClient`, `Microsoft.Data.Sqlite`, `SQLitePCLRaw.bundle_e_sqlite3`, `StackExchange.Redis`, `Microsoft.Extensions.Logging.Abstractions` (plus the xUnit/test SDK packages).

**Verify.**
```bash
ls ~/.nuget/packages | head
```

**Resolution.** Restore once while online. Afterwards the local package cache serves subsequent restores.

---

## 4. Configuration

### 4.1 `Configuration directory not found`

**Symptom.**
```
[1/10] Install - Failed: Configuration directory not found: <path>/config
```

**Likely cause.** `JsonConfigurationLoader.Discover()` walks up from the running assembly looking for `EOS.slnx`. If it cannot find one, it throws `Could not locate repository root (EOS.slnx not found).`; if it finds a root whose `config/` is absent, you get the message above.

**Verify.**
```bash
ls config/
git status --short config/
```

**Resolution.** Run from inside the repository tree, and restore any deleted `config/` files from git.

### 4.2 `Configuration file is malformed` / `failed validation`

**Symptom.**
```
[2/10] Validate - Failed: Configuration file is malformed: <path>/config/Thresholds.json
[2/10] Validate - Failed: Configuration file failed validation: <path>/config/EOS.json — <errors>
```

**Likely cause.** One of three things: invalid JSON syntax; a key with **no matching property** on the options record (the loader uses `JsonUnmappedMemberHandling.Disallow`, so unknown keys are rejected rather than ignored); or a value that violates a data-annotation constraint.

**Verify.**
```bash
python3 -m json.tool config/Thresholds.json > /dev/null && echo "valid JSON"
git diff config/
```
Then compare the file's keys against the corresponding record in `src/EOS.SharedKernel/Configuration/`.

**Resolution.** Fix the key name, the type, or the value. Adding a configuration field requires adding the property to the options record **as well as** the JSON — one without the other always fails.

**Escalate** if a field genuinely needs to be added: that is a scoped change with a KISS/YAGNI justification, per `docs/guides/Engineering-Workflow-Guide.md`.

### 4.3 `[4/10] Configure Providers - Failed`

**Symptom.**
```
ProvidersOptions must declare at least one provider.
Provider 'ollama' has an invalid endpoint: <value>
```

**Likely cause.** An empty `providers` array, or an endpoint that is not an absolute URI (a bare host, or a missing scheme).

**Verify.**
```bash
cat config/Providers.json
```

**Resolution.** Endpoints must be absolute — `http://localhost:11434`, not `localhost:11434`.

### 4.4 `[6/10] Health Check - Failed` on thresholds

**Symptom.**
```
ThresholdsOptions.ResourceCriticalPercent must be greater than ResourceWarningPercent.
ThresholdsOptions.Cpu tier boundaries must satisfy Warning < Critical < Emergency (got Warning=…, Critical=…, Emergency=…).
ThresholdsOptions.CpuQuota values must be non-increasing by §16 resource-class rank …
```

**Likely cause.** A `Thresholds.json` edit broke a cross-field invariant. Three families are checked:

1. `resourceCriticalPercent > resourceWarningPercent`
2. For each of `Cpu`, `Ram`, `Disk`, `ModelUsage`, `QueueLength`, `BackgroundTasks`, `CacheUsage`: `Warning < Critical < Emergency`
3. For each of `CpuQuota`, `RamQuota`, `ModelSlotQuota`: values must be **non-increasing** across `UserRequests` → `InteractiveSessions` → `AutonomousTasks` → `BackgroundMaintenance` → `LearningActivities`

**Verify.** The error message names the offending family and prints the values.

**Resolution.** Restore the ordering. These are fail-closed checks — without them, the Capacity Manager would silently misclassify measured values instead of failing.

### 4.5 `[7/10]` / `[8/10]` failures

`KnowledgeOptions.VectorStoreCollection is not set.` — `config/Knowledge.json`'s `vectorStoreCollection` is empty.
`PlannerOptions.ReplanningCadenceMinutes must be greater than zero.` — `config/Planner.json`'s value is `0` or negative.

Both are single-field fixes in the named file.

---

## 5. Environment Variables and Data Stores

### 5.1 `Required environment variable ... is not set`

**Symptom.**
```
Unhandled exception. System.InvalidOperationException:
Required environment variable 'EOS_SQLSERVER_CONNECTION_STRING' is not set.
```

**Likely cause.** `DataStoreConnectionOptions.FromEnvironment()` reads the **process** environment. `docker compose` reads `.env` automatically; **`EOS.Runner` does not.** Having a correct `.env` file is not sufficient.

**Verify.**
```bash
for name in EOS_SQLSERVER_CONNECTION_STRING EOS_REDIS_CONNECTION_STRING EOS_CHROMADB_ENDPOINT; do
  test -n "${!name:-}" && echo "$name is set" || echo "$name is not set"
done
```

**Resolution.** Export the three variables:

```bash
export EOS_SQLSERVER_CONNECTION_STRING='Server=localhost,1433;Database=master;User Id=sa;Password=<pw>;TrustServerCertificate=True'
export EOS_REDIS_CONNECTION_STRING='localhost:6379'
export EOS_CHROMADB_ENDPOINT='http://localhost:8000'
```

### 5.2 `User: command not found` after sourcing `.env`

**Symptom.**
```
$ source .env
./.env: line 2: User: command not found
```
followed by a bootstrap step 5 failure.

**Likely cause.** The connection string contains spaces (`User Id=sa`). `source` performs word splitting, so the value is truncated at the first space and the remainder is executed as a command. This is a real, reproducible failure — the truncated connection string then fails the pre-login handshake.

**Verify.**
```bash
case "${EOS_SQLSERVER_CONNECTION_STRING:-}" in
  *"User Id="*) echo "EOS_SQLSERVER_CONNECTION_STRING is set and contains User Id" ;;
  "") echo "EOS_SQLSERVER_CONNECTION_STRING is not set" ;;
  *) echo "EOS_SQLSERVER_CONNECTION_STRING is set but may be truncated" ;;
esac
```

**Resolution.** Export explicitly (§5.1), or use a loader that preserves the full value:

```bash
while IFS= read -r line; do
  case "$line" in \#*|"") continue;; esac
  export "${line%%=*}=${line#*=}"
done < .env
```

### 5.3 `[5/10] Start Infrastructure - Failed`

**Symptom.**
```
One or more data stores are unreachable: SQL Server: A connection was successfully established
with the server, but then an error occurred during the pre-login handshake. …
```
or `Redis: …`, `ChromaDB: …`, `SQLite: …`.

The message names **every** unhealthy store, semicolon-separated, so read it fully before acting.

**Likely causes, by store:**

| Store | Common causes |
|---|---|
| SQL Server | Container not up; still starting (its healthcheck has a 30 s `start_period`); wrong `MSSQL_SA_PASSWORD`; a **truncated** connection string (§5.2); missing `TrustServerCertificate=True` |
| Redis | Container not up; wrong host/port in `EOS_REDIS_CONNECTION_STRING` |
| ChromaDB | Container not up; wrong `EOS_CHROMADB_ENDPOINT`. The health check probes `/api/v2/heartbeat` — a ChromaDB old enough to serve only `/api/v1/heartbeat` will fail |
| SQLite | `config/Storage.json`'s `dataDirectory` is not creatable or not writable. A leading `~` **is** expanded to the user's home directory |

**Verify.**
```bash
docker compose ps
docker compose logs sqlserver --tail 50
curl -s http://localhost:8000/api/v2/heartbeat
docker exec eos-redis redis-cli ping
ls -ld ~/eos/data
```

**Resolution.** Bring the store up and wait for `healthy`; correct the variable; fix directory permissions. The store health checkers **never throw** for an unreachable store — they return an unhealthy result carrying the underlying message, so the reported text is the real driver error.

**Escalate** if every store reports healthy under `docker compose ps` but bootstrap still reports one unreachable — that indicates a connection-string or networking mismatch worth a second pair of eyes.

### 5.4 Port conflicts

**Symptom.** `docker compose up -d` fails with a bind error, or a store answers unexpectedly.

**Likely cause.** `docker-compose.yml` publishes fixed host ports: **1433** (SQL Server), **6379** (Redis), **8000** (ChromaDB). Ollama occupies **11434**. `dotnet run --project src/EOS.Runner -- web` binds **5000** (the ASP.NET Core default; there is no `launchSettings.json` or `appsettings.json` in this repository).

**Verify.**
```bash
ss -ltn | grep -E ':(1433|6379|8000|11434|5000)\b'    # add -p, with sudo, to name the process
docker ps --format '{{.Names}}\t{{.Ports}}'
```

**Resolution.** Stop the conflicting process. For the web host only, `ASPNETCORE_URLS=http://localhost:5050` moves the port without any code change. The store ports are fixed in `docker-compose.yml`; changing them means editing that file **and** the corresponding `EOS_*` variables.

**Note.** `deploy/restore-drill.sh` deliberately starts its stack with **distinct container names and ports** so a drill never collides with the live stack.

### 5.5 Root-owned files in the data directory

**Symptom.** Backup, cleanup, or container recreation fails with permission errors under `EOS_DATA_DIR`.

**Likely cause.** SQL Server creates internal subdirectories owned by its own container UID. `deploy/backup.sh` exits `3` when a required source path is missing or unreadable; `EOS.RestoreDrill.Tests` documents this exact condition — the live data directories are not fully readable by the host user, which is why its end-to-end test builds a synthetic-but-real archive instead of archiving live data.

**Verify.**
```bash
ls -la "$EOS_DATA_DIR"/sql
```

**Resolution.** This is expected behaviour of the bind-mounted SQL Server container, not a defect. `deploy/restore-drill.sh` handles the equivalent cleanup by removing container-owned content from inside a throwaway container rather than requiring `sudo` on the host.

---

## 6. Runtime

### 6.1 The process exits `0` and does nothing

**Symptom.** Bootstrap reaches `Ready`, then the process exits with no further output.

**Likely cause.** The argument list did not match one of the four recognized command shapes. `Program.cs` matches literally:

```csharp
if (args is not ["ask", _] and not ["compress"] and not ["web"] and not ["run", _]) { return 0; }
```

`ask` or `run` with zero or two-plus text arguments does **not** match. There is no `--help` and no argument parser.

**Verify.**
```bash
dotnet run --project src/EOS.Runner -- ask "one quoted question"     # matches
dotnet run --project src/EOS.Runner -- ask what is EOS              # does NOT match — 3 args
dotnet run --project src/EOS.Runner -- run "change src/EOS.SeniorEngineer/SeniorEngineer.cs" # matches
```

**Resolution.** Quote the question as a single argument, and remember `--` separates `dotnet run`'s own arguments from the program's.

### 6.2 `ask` returns exit code 1

Three distinct causes, distinguishable from the log line immediately before the exit:

| Log line | Cause | Resolution |
|---|---|---|
| `Malformed request: no text was provided to 'ask'.` | Empty or whitespace text | Supply real text |
| `Reasoning failed: <FailureMode> - <message>` | `ReasoningFailedException` | See §6.3 |
| `Decision <id> was not allowed: <Verdict> - <Reason>` | Protection returned a non-`Allow` verdict | See §6.4 |

### 6.3 Reasoning failures

`ReasoningFailureMode` tells you which stage refused:

| Failure mode | Meaning | Typical cause |
|---|---|---|
| `InvalidGoal` | Stage 2 rejected an empty goal | Empty input |
| `MissingContext` | Context was still empty or truncated after expansion | Only occurs when `ContextScope` is supplied; the `ask` path does not supply one |
| `InternalError` | The provider returned nothing usable, or an unsupported `ReasoningType` | Almost always an inference failure — see §7 |

`AskCommand` does not set `ContextScope`, so `MissingContext` is not reachable from the CLI today.

### 6.4 Protection denied the action

**Symptom.** `Decision <id> was not allowed: Deny - <reason>` (or `Defer`).

**Likely causes.** `ProtectionGate.Validate` fails closed on: a `RiskScore` outside 0–100; a blank `Actor` or `ActionType`; a matching deny entry in one of `Security.json`'s four policy lists; or a CPU-measurement failure (`"Resource measurement failed; failing closed."`).

**Verify.**
```bash
cat config/Security.json      # all four lists are empty by default
cat /proc/stat | head -1      # the CPU measurement source
```
Every call logs `Protection validate: ActionId=… ActionType=… Actor=… RiskScore=… Tier=… Verdict=…`.

**Resolution.** With the committed default `Security.json` (four empty policy lists) an `ask` decision validates as `Low`/`Allow`. A denial therefore means a policy was added, or resource measurement failed.

**Escalate** rather than editing policy to get past a denial. Weakening a Protection policy is a governance decision.

### 6.5 The `compress` command

**Symptom.**
```
Compression sweep was not allowed: <Verdict> - <Reason>
```
exit code `1`.

**Likely cause.** `Program.cs` gates the sweep behind `ProtectionGate.Validate(new ActionRequest(…, "MemoryCompression", "HumanOperator", RiskScore: 10))`. A non-`Allow` verdict aborts before any data is touched.

On success it prints exactly:

```
Compression sweep complete: N entries compressed.
```

(singular `entry` when `N == 1`).

**Note — this command mutates data.** `CompressionSweep` replaces eligible `Lesson` nodes' content with a model-generated summary, archiving the original into `ArchivedContentStore` first. A result of `0` is normal and common: the sweep first requests a Background Maintenance slot and returns `0` immediately if it is deferred, and eligibility additionally requires the node's pipeline record to have reached `Pattern` stage or beyond.

### 6.6 The `web` command

**Symptom.** `curl http://localhost:5000/` refuses the connection, or a route 400s.

**Verify.**
```bash
curl -s http://localhost:5000/api/loop-status
curl -s 'http://localhost:5000/api/tasks?state=Ready'
curl -s 'http://localhost:5000/api/recent-events?count=10'
```

**Known behaviours, all confirmed against a running host:**

- `GET /api/tasks` **without** `state` returns **400** — the parameter is required, not optional.
- Enums serialize as **numbers** (`"currentMode":1` is `OperationalMode.Assisted`). No `JsonStringEnumConverter` is configured.
- `/api/recent-events` returns whatever is in the event store, **including rows written by past test runs** (`"producer":"EOS.Runner.Tests"`). There is no per-environment separation.
- The page fetches each endpoint exactly **once** on load. It does not auto-refresh — a stale page is expected, not a bug.
- There is **no authentication, no authorization, and no HTTPS redirect**.

**Resolution.** For a port conflict, set `ASPNETCORE_URLS`. For an empty dashboard, there is simply nothing in those tables yet.

### 6.7 The loop decision is denied before task creation

**Symptom.** The loop iteration reports outcome `Denied`, `run` exits `1`, and no `Goal created`, `Task dispatched`, `Task blocked`, or `Task completed` line appears.

**Likely cause.** Step 7's `LoopIterationDecision` Protection validation returned a non-`Allow` verdict before planning. No Task exists, so there is no `Running → Blocked` transition and no `TaskBlocked` event.

**Verify.** Inspect the `Protection validate` log whose `ActionType` is `LoopIterationDecision`, then confirm that the final loop-iteration line reports `Denied`. Preserve the verdict and reason.

**Safe action.** Investigate or escalate the policy, risk, or resource-measurement reason. Do not weaken Protection to force planning to begin.

### 6.8 Task dispatch is denied but `run` exits `0`

**Symptom.** A Goal may be created, but there is no `Task dispatched (Running)`, `Task blocked`, or `Task completed` line; the outer iteration may nevertheless report `Completed` and `run` may exit `0`.

**Likely cause.** The `TaskDispatch` Protection validation returned non-`Allow`. `DispatchNextAsync` returns `ProtectionDenied` without changing the selected Task from `Ready` or publishing `TaskBlocked`. The current `LoopController` executes only a `Dispatched` result and otherwise continues through its structural learning phase, which returns `Completed`.

**Verify.** Inspect the Protection log whose `ActionType` is `TaskDispatch`; if the Dashboard is running, query `GET /api/tasks?state=Ready`. Treat the absence of `Task completed (Review)` and its evidence reference as proof that the engineering task did not complete, regardless of exit `0`.

**Safe action.** Record and escalate the non-`Allow` verdict. Do not bypass Protection or treat the process exit code alone as task-completion evidence.

### 6.9 The `run` command exits `1` or blocks a Running task

**Symptom.** The command logs `Loop iteration failed for ManualRequest`, prints `Task blocked: <id> — <reason>`, or completes an iteration with a non-`Completed` outcome. A blocked task remains `Blocked`; EOS does not automatically recover, retry, apply, commit, push, or merge it.

**Likely cause.** Use the reason after `Task blocked` to distinguish the classes below. `ExecutionCoordinator` persists `Running → Blocked` and publishes `TaskBlocked` for execution, evidence, Universal Gate, TaskCompletion Protection, and in-process cancellation failures.

**Verify.** Preserve the complete `Task blocked` line and the immediately preceding logs. Query the Dashboard task endpoint if the web host is available:

```bash
curl -s 'http://localhost:5000/api/tasks?state=Blocked'
```

**Safe action.** Correct the task scope or underlying source/test/environment failure and issue a separately reviewed request. Do not weaken Protection or a gate. Automatic `Blocked → Retry` recovery is not wired into this command.

### 6.10 Task scope, workspace, and edit evidence failures

| Symptom/reason contains | Likely cause | Verification | Safe action |
|---|---|---|---|
| `references no repository path under src/ or tests/` | The task contains no explicit permitted path. | Inspect the quoted task argument. | Name each required `src/...` or `tests/...` path explicitly. |
| `not a permitted repository path` | A path is absolute, uses backslashes, contains `.`/`..`/empty segments, or is outside `src/` and `tests/`. | Compare it with the repository-relative forward-slash form. | Correct the task; do not broaden the workspace boundary. |
| Protection denial during a workspace operation | The read or applicability action was not allowed. | Inspect the Protection verdict and policy log. | Escalate the denial; do not bypass Protection. |
| `contains no [EDIT] block` or `Malformed edit block` | Model output did not satisfy the strict edit contract. | Preserve the reasoning output/error; do not hand-edit it into evidence. | Retry only after the provider/task issue is understood. |
| `SEARCH text was not found`, `must occur exactly once`, or another edit-application error | The proposed edit cannot be applied deterministically to the in-memory source. | Confirm the named file and requested scope still match the workspace. | Refine the task or resolve source drift; do not apply the candidate manually as validated evidence. |
| `not a valid, in-scope unified diff` | The locally generated candidate failed structural or scope validation. | Preserve the exact bounded violation. | Treat it as an execution failure and escalate if repeatable. |
| `does not apply to the workspace` | Read-only `git apply --check` rejected the candidate or could not run. | Run `git apply --check` only on a separately saved candidate in a disposable checkout if authorized. | Resolve source drift/tool availability; do not use `git apply` on the primary workspace as a diagnostic shortcut. |
| `Execution failed:` followed by a SQL/connection error after applicability | Artifact registration could not reach or write the SQL-backed registry. | Verify `EOS_SQLSERVER_CONNECTION_STRING` is present and SQL Server is healthy without printing the secret. | Restore the service/configuration; do not substitute an unregistered evidence reference. |

The generated diff is checked but never applied to the real workspace. A successful applicability check proves only that it applies, not that it builds, passes tests, or implements the intended change.

### 6.11 Universal Gate failures

**Symptom.** `Task blocked` contains `Universal Gate failure:` or `Universal Gate evaluation failed:`.

**Likely causes.** Gate 1 failed to apply the candidate in the isolated copy, find an owning project, restore a usable project graph, or build the affected reverse-dependency closure. Gate 2 failed because a targeted test command failed or recorded zero executed tests. A missing tool, timeout, malformed result, or other inability to evaluate fails closed rather than passing.

**Verify.** The bounded failure reason names Gate 1 or Gate 2 and includes the affected build/test command detail. Reproduce the named build or test in a clean disposable checkout with the same approved source and without changing configuration. Gate child processes deliberately receive no `EOS_*` credentials, so a targeted test that requires live EOS infrastructure fails rather than contacting it.

**Safe action.** Fix the candidate or the reproducible toolchain/test problem and rerun under review. Do not edit gate results, provide secrets to the isolated child, or reinterpret `NotApplicable` yourself—the Rule Engine owns the decision. Gates 3–5 are not implemented, and passing Gates 1–2 reaches `Review`, not automatic `Testing` or `Verified`.

---

## 7. Local AI Runtime (Ollama)

### 7.1 Inference fails or times out

**Symptom.** `Reasoning failed: InternalError - …`, preceded by an `InferenceAttemptFailed` warning naming the provider and model.

**`InferenceErrorType` tells you which:**

| Error type | Produced when |
|---|---|
| `ProviderUnavailable` | `HttpRequestException` (Ollama not running / wrong endpoint), or a non-2xx HTTP status |
| `Timeout` | The `HttpClient` timed out. In production this is `Thresholds.inferenceTimeoutSeconds` (currently `100`) |
| `MalformedResponse` | Ollama returned unparseable JSON, or a response that was empty or `done: false` |
| `ContextTooLarge` | `InferenceRequest.TokenBudgetEstimate` exceeds `Inference.json`'s `maxTokens` |
| `CapabilityUnsupported` | No routed provider advertises the requested capability |

**Verify.**
```bash
systemctl status ollama
curl -s http://localhost:11434/api/tags
ollama list
curl -s http://localhost:11434/api/generate \
  -d '{"model":"qwen2.5-coder:7b","prompt":"hello","stream":false}' | head -c 200
```

**Resolution.**

- Not running → start Ollama.
- Model missing → `ollama pull qwen2.5-coder:7b`. The model name must match **both** `config/Inference.json`'s `defaultModel` and the entry in `config/Providers.json`.
- Timeouts on CPU-only hardware → a single completion commonly takes 30–60 s against a 100 s budget. Reduce load, or raise `inferenceTimeoutSeconds`.

### 7.2 The provider is marked unavailable

**Symptom.** Inference stops being attempted at all; a `RoutingDenied` warning appears.

**Likely cause.** `HealthMonitor` marks a provider unavailable after `Thresholds.providerFailureThreshold` consecutive failures (currently `3`), and re-probes only after `providerRecoveryProbeIntervalSeconds` (currently `30`).

**Resolution.** Fix the underlying provider fault and wait for the recovery probe interval, or restart the process (health state is in-memory only).

### 7.3 `No available provider supports the 'Embeddings' capability.`

**Symptom.** An `InvalidOperationException` with exactly that message, thrown from `AIProviderManager.EmbedAsync`.

**Likely cause — this is the current state of the repository, not a misconfiguration on your side.** Two things combine:

1. `config/Providers.json` declares its only model with capabilities `["Chat"]`. `InferenceRouter.Route("Embeddings")` therefore returns no candidates.
2. `Program.cs` constructs `AIProviderManager` **without** the optional `embeddingAdapters` dictionary, so no embedding adapter is registered even if a provider advertised the capability.

**What this does and does not affect.** It does **not** affect `ask` — `KnowledgeClient.UpdateAsync` never embeds. It **does** affect `KnowledgeClient.ConsolidateAsync`, which calls the embedding generator when one is supplied, and which is reachable through the two automatic consolidation triggers wired in `Program.cs`.

`OllamaEmbeddingAdapter` itself is real and proven — `tests/EOS.Knowledge.Tests/EmbeddingGeneratorIntegrationTests.cs` exercises it end-to-end against `nomic-embed-text` and asserts a 768-dimension vector — but that test constructs the adapter directly rather than going through `AIProviderManager`.

**Resolution.** *Not currently implemented / not currently verifiable* as a supported production path. Wiring the composed embedding channel would mean adding an embeddings-capable model to `config/Providers.json` **and** registering an embedding adapter in `Program.cs`. Both are production changes, so they belong in an approved plan — not a local fix.

**Escalate.** Report this rather than patching around it.

---

## 8. Test Failures

`docs/guides/Testing-Guide.md` covers this in depth. The three highest-frequency causes:

**8.1 Infrastructure missing.** Integration tests **fail** rather than skip. `Required environment variable … is not set` or a connection error means the environment, not the code.

**8.2 Ollama contention.** Several integration tests construct an `HttpClient` without overriding `Timeout`, inheriting the .NET default of 100 s. Under full-suite load a single inference can exceed it. **Observed 2026-09-10 on the reference machine:** `AskCommandIntegrationTests.ExecuteAsync_ExplainSOLIDPrinciples_…` failed after 1 m 43 s in the full suite, then passed in 57 s when re-run alone. Always re-run the single test in isolation before concluding anything:

```bash
dotnet test tests/EOS.Runner.Tests/EOS.Runner.Tests.csproj --no-build \
  --filter "FullyQualifiedName~AskCommandIntegrationTests"
```

**8.3 Shared database state.** There is no per-test isolation convention; tests share real SQL Server tables and generally do not clean up. Three assemblies disable parallelization for exactly this reason (`EOS.Infrastructure.Tests`, `EOS.Learning.Tests`, `EOS.Orchestrator.Tests`).

**8.4 An architecture test fails.** Read the whitelist comments in the failing file — they record which Work Package authorized each entry. A new violation normally means a project reference was added that the dependency rules do not permit. **Escalate**: editing the whitelist to make the test pass defeats its purpose.

**8.5 The restore drill test takes ~16 minutes.** That is expected — it starts a fresh SQL Server container and waits for its healthcheck. If Docker is unavailable it fails explicitly with `Docker unavailable — real restore-drill not executed.` rather than skipping.

---

## 9. Backup and Restore Drill

`deploy/backup.sh <destination-dir>` exits:

| Code | Meaning |
|---|---|
| `2` | Wrong argument count |
| `3` | `EOS_DATA_DIR` unset, or a required source path missing (`$EOS_DATA_DIR/{sql,redis,chroma}`, `config/`, `.env`) |

The archive is created with `umask 077` and `chmod 600`, and contains `sql/`, `redis/`, `chroma/`, `config/`, and `.env` at its root — host absolute paths are never embedded.

`deploy/restore-drill.sh <archive-path> <isolated-drill-root>` exits `2` on wrong argument count and `3` when the archive is missing or unreadable. It starts an **isolated** Compose stack with distinct names and ports, runs the real `BootstrapRunner` via `EOS.RestoreDrill`, and prints one `<StepName>: PASS|FAIL` line per step. Success requires all ten steps to pass with `Ready` last.

**Safety invariant worth knowing before you debug it:** the script only recursively deletes a drill root **it created itself**. A pre-existing, operator-supplied directory is never removed, on any exit path.

**Escalate** if a restore drill fails against a real archive — that is a disaster-recovery finding, not a local environment issue.

---

## 10. When To Escalate — Summary

Stop and report, rather than working around, when:

- A fix would require **suppressing an analyzer warning** or editing an **architecture-test whitelist**.
- A fix would require **weakening a Protection policy** to get an action approved.
- A fix would require **changing production source code, configuration schema, or architecture** to make something work.
- A **restore drill fails against a real archive**.
- The repository and a specification **appear to contradict each other** — per `docs/Development-Workflow.md` §2, a perceived conflict is reported, never silently resolved.
- A verification step **cannot be run**. Report it as *not run*.

The escalation path is the one described in `docs/guides/Engineering-Workflow-Guide.md`: evidence first, then the Reopening Criteria, then an additive solution or a recorded, unresolved finding — never a quiet workaround.
