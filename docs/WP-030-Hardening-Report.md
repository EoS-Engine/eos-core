# WP-030 Hardening Report

**Work Package:** WP-030 — Production Readiness: Dashboard, Backup Automation & Hardening Pass
**Scope:** WP030-01 through WP030-07
**Status:** All seven sub-work-packages CLOSED — PASS
**Goal lifecycle status:** DEFERRED (see [Goal Lifecycle](#goal-lifecycle))

This report documents what was actually implemented and verified during WP-030. It does not
describe intended, planned, or future capability — only what exists in the repository and what
was proven to work, distinguishing explicitly between **IMPLEMENTED**, **VERIFIED**, **DEFERRED**,
**NOT VERIFIED**, **PRE-EXISTING FAILURE**, and **OUT OF SCOPE** wherever a claim could otherwise
be read as stronger than the evidence supports.

---

## 1. WP-030 Summary

| WP | Scope | Result | Evidence | Status |
|---|---|---|---|---|
| WP030-01 | `SqlEventStore.GetRecentAsync` — deterministic, tie-broken recent-event query | PASS | 3 dedicated tests (`SqlEventStoreTests.cs`), part of `EOS.Infrastructure.Tests` (20/20 total) | CLOSED |
| WP030-02 | 4 `EOS.Contracts` read interfaces (`ILoopStatusQueryClient`, `ITaskStatusQueryClient`, `RecentEventSummary`, `IRecentEventsQueryClient`) | PASS | Compiles cleanly; no dedicated tests (bare interface/DTO declarations — matches this repository's own convention of not testing interface shape) | CLOSED |
| WP030-03 | `EOS.Dashboard.DashboardQueryService` — aggregates the three approved query interfaces | PASS | `EOS.Dashboard.Tests`, 4/4 | CLOSED |
| WP030-04 | `EOS.Runner` composition-root adapters (`LoopControllerLoopStatusQueryClient`, `DispatchedTaskStoreTaskStatusQueryClient`, `SqlEventStoreRecentEventsQueryClient`) + live `SqlEventStore` event-persistence wiring | PASS | 9 tests in `DashboardCompositionRootTests.cs` (part of `EOS.Runner.Tests`) | CLOSED |
| WP030-05 | `EOS.Web` minimal dashboard host (4 HTTP endpoints), hosted inside the existing `EOS.Runner` process via a new `web` CLI mode | PASS | `EOS.Web.Tests`, 5/5 | CLOSED |
| WP030-06 | `deploy/backup.sh` — filesystem-level daily backup with 7-daily/4-weekly retention | PASS | `EOS.Deploy.Tests`, 13/13 | CLOSED |
| WP030-07 | `deploy/restore-drill.sh` + isolated Docker Compose stack + `EOS.RestoreDrill` driver — restores a backup archive and runs the real, unmodified `BootstrapRunner` against it | PASS | `EOS.RestoreDrill.Tests`, 16/16 (15 deterministic + 1 real end-to-end drill) | CLOSED |

---

## 2. Architecture — Final State

### 2.1 Event flow (recent events)

```
EOS events
    ↓
EventMediator                              (unmodified — EOS.Orchestrator/EventMediator.cs)
    ↓
approved persistence subscribers            (EOS.Runner/Program.cs, PersistEvent<TPayload>)
    ↓
SqlEventStore.AppendAsync                   (EOS.Infrastructure)
    ↓
SqlEventStore.GetRecentAsync → RecentEventSummary   (SqlEventStoreRecentEventsQueryClient, EOS.Runner)
    ↓
DashboardQueryService.GetRecentEventsAsync   (EOS.Dashboard)
    ↓
EOS.Web (GET /api/recent-events)
    ↓
dashboard API response (JSON)
```

### 2.2 Loop status flow

```
LoopController                              (EOS.Orchestrator — implements ILoopControlClient, unmodified)
    ↓
LoopControllerLoopStatusQueryClient          (EOS.Runner — delegates, does not re-derive logic)
    ↓
ILoopStatusQueryClient                       (EOS.Contracts)
    ↓
DashboardQueryService.GetLoopStatusAsync     (EOS.Dashboard)
    ↓
EOS.Web (GET /api/loop-status)
```

### 2.3 Task status flow

```
DispatchedTaskStore                          (EOS.Orchestrator, unmodified)
    ↓
DispatchedTaskStoreTaskStatusQueryClient      (EOS.Runner — delegates)
    ↓
ITaskStatusQueryClient                        (EOS.Contracts)
    ↓
DashboardQueryService.GetTasksByStateAsync    (EOS.Dashboard)
    ↓
EOS.Web (GET /api/tasks?state=...)
```

`EOS.Dashboard` depends only on `EOS.Contracts` throughout (Constitution §0.11/R-04) — it never
references `EOS.Infrastructure` or `EOS.Orchestrator` directly. All three concrete adapters are
composed exclusively in `EOS.Runner` (the sole composition root, Part 1 §1.3), matching the
Composition Root Adapter Pattern established by ADR-015-001.

---

## 3. Approved Persisted Events

Exactly these seven `EventMediator` payload types are subscribed for `SqlEventStore` persistence
in `EOS.Runner/Program.cs`:

- `LoopIterationStarted`
- `LoopIterationCompleted`
- `LoopIterationEvaluated`
- `OperationalModeChanged`
- `GoalCreated`
- `TaskCreated`
- `TaskStarted`

**`GoalCreated` is persisted only as a generic recent event** (`EventType`, `Producer`,
`OccurredAt`, `PayloadJson` — the same shape as every other persisted event). No Goal lifecycle
*status* is derived from it anywhere in the codebase. See [Goal Lifecycle](#goal-lifecycle).

No event type outside this set of seven is persisted. `EventMediator.cs` itself was not modified
by any WP030 sub-work-package — persistence is implemented entirely as additional subscribers.

---

## 4. Dashboard Endpoints

`EOS.Web`'s `DashboardWebHost` (ASP.NET Core minimal API, hosted inside the existing `EOS.Runner`
process via `dotnet run ... -- web`) exposes exactly:

| Method | Route | Source |
|---|---|---|
| GET | `/` | Minimal HTML page; title from `DashboardOptions.Title` (`config/Dashboard.json`) |
| GET | `/api/loop-status` | `DashboardQueryService.GetLoopStatusAsync` |
| GET | `/api/tasks?state={TaskLifecycleState}` | `DashboardQueryService.GetTasksByStateAsync` |
| GET | `/api/recent-events?count={int}` (default 50) | `DashboardQueryService.GetRecentEventsAsync` |

**No Goal endpoint exists.** **No `CountByState` endpoint exists** — `ITaskStatusQueryClient`
declares `CountByStateAsync`, and `DashboardQueryService` exposes `CountTasksByStateAsync`, but no
HTTP route was mapped to it; it remains unused by `EOS.Web`, matching the "no speculative
endpoints" instruction under which WP030-05 was authorized.

Live-view behavior is pull-only: every request queries the live `DashboardQueryService` fresh, with
no caching layer anywhere in the WP030-01–05 chain. No auto-refresh, polling, SignalR, or
WebSockets were added.

---

## 5. Backup — `deploy/backup.sh`

**IMPLEMENTED and VERIFIED** (WP030-06):

- CLI: `deploy/backup.sh <destination-dir>`. Exit codes: `0` success, `2` wrong argument count,
  `3` missing/unreadable required source path.
- Archive contents: `${EOS_DATA_DIR}/sql`, `${EOS_DATA_DIR}/redis`, `${EOS_DATA_DIR}/chroma`,
  `config/*.json`, `.env` — all normalized to archive-root-relative paths (`sql/`, `redis/`,
  `chroma/`, `config/`, `.env`); no absolute host path is embedded in the archive.
- Naming: `eos-backup-<YYYYmmdd-HHMMSS>.tar.gz` (UTC), with a numeric-suffix fallback if a
  same-second collision would otherwise overwrite an existing archive.
- Permissions: the archive is `chmod 600` immediately after creation.
- Secret handling: `.env` is archived as an opaque file — its contents are never read, parsed, or
  printed by the script.
- Retention: after each run, keeps every archive from the last 7 calendar days (age 0–6 days),
  plus the single most-recent archive in each of four non-overlapping 7-day buckets (8–14, 15–21,
  22–28, 29–35 days ago); everything else matching `eos-backup-*.tar.gz` in the destination is
  deleted. Verified against synthetic multi-day/multi-week archive sets, including the literal gap
  at exactly age-7 (neither "last 7 days" nor bucket 1) — implemented and tested exactly as
  specified, not "corrected."

**Known limitation (NOT VERIFIED against production data in this environment):** `deploy/backup.sh`
performs a plain filesystem-level `tar` archive — it is **not** a SQL-native or
database-consistency-aware backup (this is Infrastructure Roadmap Phase 8's own explicit design
choice, not a WP030 limitation: its "Common Mistakes" section names "over-engineering this into a
database-specific export/import tool chain" as a mistake to avoid). In this specific sandboxed
environment, the live `EOS_DATA_DIR`'s SQL Server and Redis data files are owned by the
containers' internal UIDs (10001 and 999 respectively, confirmed via `ls -ln`) and are not
readable by the host user running `backup.sh`, with no passwordless `sudo` available. A real
invocation against the live environment in this sandbox therefore cannot produce a complete
archive of that data. This is an environment/host-permission characteristic, not a defect in
`backup.sh`, and `backup.sh` was not modified to work around it (out of WP030-07's authorized
scope; `deploy/backup.sh` is unchanged since its WP030-06 closeout).

---

## 6. Restore Drill — `deploy/restore-drill.sh`

**IMPLEMENTED and VERIFIED** (WP030-07):

Components:
- `deploy/restore-drill.sh` — orchestration script.
- `deploy/docker-compose.restore-drill.yml` — isolated Compose stack (same three images as the
  live `docker-compose.yml`, distinct container names and host ports, volumes bind-mounted from
  the drill root only).
- `src/EOS.RestoreDrill` — dedicated console driver (`RestoreDrillRunner`), constructs
  `JsonConfigurationLoader` directly from the supplied, already-extracted config directory
  (never calls `JsonConfigurationLoader.Discover()`, which would resolve the real repository's
  `config/`) and invokes the existing, unmodified `BootstrapRunner.CreateEosBootstrap(...)`.
- `tests/EOS.RestoreDrill.Tests` — 15 deterministic tests plus 1 real end-to-end drill test.

Flow: `deploy/restore-drill.sh <archive-path> <isolated-drill-root>` — validates arguments (exit
`2` on wrong count) and archive readability (exit `3`) — extracts the archive into the isolated
drill root — verifies `sql/`, `redis/`, `chroma/`, `config/`, `.env` are all present (exit `3` if
any is missing) — rewrites **only** the extracted copy of `config/Storage.json` so
`dataDirectory` points inside the drill root (the real repository `config/Storage.json` is never
touched — verified byte-identical to `origin/main` after every run) — starts the isolated Compose
stack with `docker compose up -d --wait` — invokes the `EOS.RestoreDrill` driver with drill-only
`EOS_SQLSERVER_CONNECTION_STRING`/`EOS_REDIS_CONNECTION_STRING`/`EOS_CHROMADB_ENDPOINT` environment
variables (set for the subprocess only, never exported to the parent shell, never written to
`.env`) — always tears the Compose stack down and cleans the drill root via a shell `trap` on
every exit path, success or failure.

**Two cleanup fixes discovered and applied during implementation** (both confined to
`deploy/restore-drill.sh`; neither touches `deploy/backup.sh`, `docker-compose.yml`, or any
forbidden file):

1. **Extracted-data permissions.** `tar` extraction applies the invoking process's umask, which
   stripped the world-write bit the isolated SQL Server container's non-root internal user needs
   to initialize into the bind-mounted directory. Fixed with `chmod -R o+rwX` on the three
   extracted data directories, scoped entirely to the isolated drill root.
2. **Container-owned-file cleanup.** SQL Server creates its own internal files/subdirectories
   (e.g. `.system`) owned by its internal, non-host UID, which the host user cannot recursively
   delete. Fixed by running the final cleanup step inside a throwaway container (reusing the
   already-pulled `redis:7-alpine` image — no new dependency), which deletes as root within its
   own container namespace before the host-side directory removal.

### 6.1 Real restore drill — what was verified

Docker was available; the real drill was executed (not skipped, not mocked):

- **Real archive creation** — `deploy/backup.sh` executed for real against real, host-owned,
  empty `sql/`/`redis`/`chroma` source directories, genuinely archiving the real repository
  `config/*.json` and real `.env` (read-only).
- **Real archive extraction** into the isolated drill root.
- **Real isolated SQL Server** — started, became healthy, and genuinely initialized its own
  system databases on disk (`master.mdf`, `model.mdf`, `mastlog.ldf`, etc., confirmed present on
  the host filesystem before cleanup).
- **Real isolated Redis** — started and became healthy.
- **Real isolated ChromaDB** — started and became healthy.
- **Restored filesystem structure** — `sql/`, `redis/`, `chroma/`, `config/`, `.env` all present
  and correctly laid out post-extraction.
- **Extracted config isolation** — the drill's `BootstrapRunner` run was driven entirely by the
  extracted, isolated config directory, not the real repository one.
- **Storage.json safety rewrite** — verified both structurally (extracted copy rewritten) and
  negatively (real `config/Storage.json` confirmed unchanged).
- **Real `BootstrapRunner` execution** — the actual, unmodified class, via `EOS.RestoreDrill`.
- **All ten `BootstrapRunner` steps succeeded; `Ready` was reached** (§7).
- **Container/port isolation** — distinct names and ports confirmed both by static Compose-file
  inspection and by observing the live stack remained "Up 3 days" (uninterrupted) throughout.
- **Cleanup** — verified: no leftover `/tmp/eos-drill-*` directories, no leftover
  `restore-drill`-named containers after the run.
- **Live stack untouched** — confirmed before and after; never stopped, restarted, or depended
  upon.

**Production-data restore was NOT verified in this environment.** The real drill above used a
real backup archive built from real, freshly-initialized (empty-source) infrastructure — genuinely
real Docker, real database engines, real `BootstrapRunner`, nothing mocked — but not from
pre-existing production rows, because (§5) the live `EOS_DATA_DIR`'s container-owned files are not
readable by the host backup operator in this sandboxed environment. The entire restore-drill code
path is proven to work end-to-end; whether it correctly restores a *specific production dataset*
depends on that dataset having been successfully archived in the first place, which was not
demonstrated here.

---

## 7. Bootstrap Verification

`BootstrapRunner` (`src/EOS.Runner/Bootstrap/BootstrapRunner.cs`) was **not modified** by any
WP030 sub-work-package. The real WP030-07 drill executed all ten of its existing steps against the
isolated, restored environment, and all ten reported success:

1. Install — PASS
2. Validate — PASS
3. Generate Keys — PASS
4. Configure Providers — PASS
5. Start Infrastructure — PASS (real SQL Server/Redis/ChromaDB connectivity checks, plus the
   SQLite writability probe — confirmed operating inside the isolated drill root only, never
   `~/eos/data`, because of the `Storage.json` rewrite in §6)
6. Health Check — PASS
7. Initialize Knowledge — PASS
8. Seed Planner — PASS
9. Run Validation — PASS
10. Ready — PASS

None of BootstrapRunner's ten steps perform row-count or checksum data-integrity verification —
that is Constitution Part 13's separate "Integrity Verification" activity, explicitly excluded
from WP-030's frozen Disaster Recovery scope from the outset (WP-030's DR scope was frozen to
exactly five roadmap deliverables: daily backup, 7-daily/4-weekly retention, tested restore,
restore-drill via BootstrapRunner, and this hardening report — not Part 13's full seven-activity
table).

---

## 8. Goal Lifecycle

**Goal lifecycle status: DEFERRED.**

No `IGoalStatusQueryClient` interface exists anywhere in the repository. No Goal lifecycle status
is approximated, derived, or inferred from the `GoalCreated` event (which carries only `GoalId`,
`ParentGoalId`, and `Statement` — no `GoalLifecycleState`, no `PlanId`). This was an explicit,
frozen WP-030 scope decision made during WP030-02: the roadmap's "Task/Goal status" requirement
could not be satisfied for Goals without either modifying `GoalStore` (out of scope) or adding new
Goal lifecycle events (out of scope), and no substitute or approximation was authorized. Dashboard
displays Task status (via `ITaskStatusQueryClient`) but not Goal status. No future implementation
is proposed or implied by this report.

---

## 9. Known Failures (Pre-Existing, Not Caused by WP-030)

The following failures were observed during WP-030 verification and were reproduced/identified as
pre-existing against `origin/main` (via `git stash` comparisons performed during WP030-03 through
WP030-07 closeouts) — none were caused by any WP-030 change, and none were modified:

| Category | Test | Nature |
|---|---|---|
| Architecture test failure | `EOS.ArchitectureTests.OnlyAllowedProjectsMayReferenceAIProviderTests` (`EOS.Learning.Tests → EOS.AIProvider`) | Pre-existing forbidden-reference violation, unrelated to Dashboard/backup/restore work |
| Learning SQL parameter-limit failure | `EOS.Learning.Tests.LearningEngineAcceptanceTests.FourSimilarLessons_ClusterAndPromoteToAPattern_WithARealLessonPromotedEvent` | SQL Server "too many parameters" error from `PipelineRecordStore`, pre-existing |
| Intermittent Runner/Ollama/shared-state failures | `EOS.Runner.Tests.SchedulerExecutionCoordinatorAcceptanceTests.*`, `EOS.Runner.Tests.AskCommandIntegrationTests.*` | Full-suite-run-only flakiness tied to shared, non-isolated SQL Server test data accumulated across this long session, and (for the Ask test) a real Ollama LLM call; both pass reliably in isolation |
| Orchestrator shared-SQL-state failure class | `EOS.Orchestrator.Tests.ProgressMonitorTests.GetGoalProgressAsync_CountsOnlyTheNewCurrentPlanTasks_WhenAnOldPlanAndANewPlanBothExist` | Same shared-SQL-Server-state flakiness class as above (new specific test name observed during WP030-07's full-suite run; confirmed 185/185 in isolation) |

These are documented here for completeness, not resolved — resolving them was explicitly out of
scope for every WP-030 sub-work-package ("do not fix pre-existing baseline failures").

---

## 10. Scope / Forbidden Changes

The following were confirmed untouched (byte-identical to `origin/main`) throughout WP-030:

- `src/EOS.Runner/Bootstrap/BootstrapRunner.cs`
- `docker-compose.yml`
- Every real `config/*.json` file, including `config/Storage.json`
- The real `.env`
- `EOS.Orchestrator/EventMediator.cs`
- `EOS.Infrastructure/StoredEvent.cs`
- `EOS.Planner/GoalStore.cs`
- `EOS.Domain`, `EOS.Application`, `EOS.Planner` (beyond the untouched `GoalStore.cs` reference
  above — no file in these projects was modified), `EOS.Pipeline`, `EOS.Orchestrator`

`src/EOS.Runner/Program.cs` **was** modified — but only by WP030-04 (event-persistence
subscribers, adapter construction) and WP030-05 (the additive `web` CLI branch). **WP030-07 added
zero lines to it** — confirmed via `git diff origin/main -- src/EOS.Runner/Program.cs`, whose full
diff is accounted for entirely by WP030-04/05 content. WP030-07's own implementation
(`deploy/restore-drill.sh`, `deploy/docker-compose.restore-drill.yml`, `src/EOS.RestoreDrill`,
`tests/EOS.RestoreDrill.Tests`) is fully additive and isolated — it references but does not modify
`EOS.Runner`, `EOS.Runner.Bootstrap`, or any other existing project.

---

## 11. Test Evidence

| WP | Test project / file | Result |
|---|---|---|
| WP030-01 | `SqlEventStoreTests.cs` (3 tests) within `EOS.Infrastructure.Tests` | 20/20 (full project) |
| WP030-02 | — (no dedicated tests; bare interface/DTO declarations, matching repository convention) | N/A |
| WP030-03 | `EOS.Dashboard.Tests` (`DashboardQueryServiceTests.cs`) | 4/4 |
| WP030-04 | `DashboardCompositionRootTests.cs` (9 tests) within `EOS.Runner.Tests` | 9/9 (new tests); 34/34 full project in isolation |
| WP030-05 | `EOS.Web.Tests` (`DashboardWebHostTests.cs`) | 5/5 |
| WP030-06 | `EOS.Deploy.Tests` (`BackupScriptTests.cs`) | 13/13 |
| WP030-07 | `EOS.RestoreDrill.Tests` (`RestoreDrillTests.cs`) | 16/16 (15 deterministic + 1 real end-to-end drill) |

All numbers above are taken directly from the actual closeout reports produced during each
sub-work-package's verification gates in this session; none are estimated or reconstructed.

---

## 12. Final Hardening Assessment

### GUARANTEES

WP-030 guarantees, as actually implemented and verified:

- A read-only dashboard query path (`EOS.Web` → `EOS.Dashboard.DashboardQueryService` →
  `EOS.Contracts` interfaces), architecturally isolated from `EOS.Infrastructure`/
  `EOS.Orchestrator` per Constitution §0.11/R-04.
- An approved, exactly-seven-event-type persistence path from `EventMediator` into
  `SqlEventStore`, with `EventMediator.cs` itself unmodified.
- Real, working dashboard HTTP endpoints (`GET /`, `/api/loop-status`, `/api/tasks`,
  `/api/recent-events`), hosted inside the existing `EOS.Runner` process, verified against real
  live infrastructure (manual smoke test) and automated tests.
- Documented, tested `deploy/backup.sh` behavior: CLI validation, archive layout, `0600`
  permissions, retention algorithm, collision handling — all verified deterministically.
- A working, isolated restore-drill mechanism (`deploy/restore-drill.sh` +
  `docker-compose.restore-drill.yml` + `EOS.RestoreDrill`) that never touches the live stack.
- Real `BootstrapRunner` restore validation — the actual, unmodified `BootstrapRunner` was proven,
  via a real (not mocked) end-to-end run, to reach `Ready` against genuinely restored/isolated
  infrastructure.
- Isolation from live services during the drill — distinct container names, distinct host ports,
  distinct data directories, verified live-stack uptime before and after.

### DOES NOT GUARANTEE

- **Production-data restore in this environment** — not verified; see §5/§6.1 for the exact
  host-permission constraint discovered.
- **Row-count/checksum integrity verification** — explicitly out of WP-030's frozen scope
  (Constitution Part 13's separate "Integrity Verification" activity).
- **Goal lifecycle status** — deferred; no interface, no data, no approximation exists.
- **Automated scheduled backups** — `deploy/backup.sh` and `deploy/restore-drill.sh` are scripts;
  no cron entry, scheduler, or `BackgroundService`/`IHostedService` was created or installed by any
  WP-030 sub-work-package (deliberately out of scope throughout).
- **Production disaster recovery certification** — WP-030 implements and verifies the five frozen
  DR deliverables (daily backup, retention, tested restore, restore-drill via BootstrapRunner, and
  this report); it is not a substitute for a full disaster-recovery certification process, and
  makes no claim to be one.
