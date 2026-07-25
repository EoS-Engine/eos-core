# WP-004 Implementation Plan — Data Store Foundations

## 1. Revision
Revision 1 — initial plan, pending approval. Not yet implemented.

## 2. Source of Truth
`docs/EOS-Specification.md` Part 4 (Data Architecture, §4.1 Store Ownership, §4.2 Ownership Rule), Part 12 (Bootstrap System — "Start Infrastructure"/"Health Check" steps); `docs/EOS-Implementation-Roadmap-v1.0.md` WP-004 row (verbatim); `docs/Infrastructure-and-Implementation-Roadmap-v1.0.md` Phases 3/4 (Docker Compose, `.env.example` conventions); `docs/WP-002-Implementation-Plan.md` (Future Evolution clause pre-authorizing this WP's Bootstrap step extensions).

## 3. Current Repository Baseline (inspected directly, not assumed)
- `EOS.Infrastructure`: scaffolded empty (WP-001), references `EOS.Application`, `EOS.Contracts`. Zero `.cs` files.
- `EOS.Runner`: references only `EOS.SharedKernel` + `Microsoft.Extensions.Hosting`. `BootstrapRunner.CreateEosBootstrap` has 10 named steps; "Start Infrastructure" currently only checks `StorageOptions.DataDirectory` is non-empty (a WP-002 stub); "Health Check" currently only checks `ThresholdsOptions` internal consistency (a WP-002 stub) — both are exactly the extension points WP-002's own plan named for this WP.
- `config/Storage.json` / `StorageOptions.cs`: exists, one field (`DataDirectory`), validated via WP-002's `JsonConfigurationLoader`.
- No `docker-compose.yml`, no `.env`/`.env.example` exist anywhere in the repository. Zero Docker containers currently running in this environment.
- `EOS.Contracts` contains only `EventEnvelope<TPayload>` (WP-003). `EOS.Application`, `EOS.SharedKernel` (beyond `Configuration/`) are empty.

## 4. Objective
Establish real, tested connections from `EOS.Infrastructure` to SQL Server, Redis, ChromaDB, and SQLite, per Constitution Part 4's store-ownership table, and the event store's append-only write path — nothing more.

## 5. Exact Roadmap Scope
Included: connection management + health-check probes for all four stores; the event store's append-only write path (SQL Server); no domain schema beyond what proves connectivity. Excluded: `KnowledgeNode` schema (WP-007); Redis keyspace conventions beyond one connectivity test key; ChromaDB collections beyond one smoke-test collection. Expected deliverable: `EOS.Infrastructure` exposes a health-check surface consumed by Bootstrap's Health Check step. Demo criteria: Bootstrap's Health Check step reports all four stores healthy on a clean `docker compose up`.

## 6. Vertical Slice Definition
`.env` (gitignored, real values) → `DataStoreConnectionOptions.FromEnvironment()` → `DataStoreHealthChecker` opens one real connection per store → `BootstrapRunner`'s "Start Infrastructure" step captures results, fails closed on any unhealthy store → "Health Check" step asserts the captured results → observable per-store success/failure in Bootstrap logs. Independently, `SqlEventStore` proves the append-only write path via one real insert + read-back, exercised by an integration test (not wired into any live event flow — that is a later WP's job).

## 7. Stores Included

| Store | Why it exists (Part 4 §4.1) | What WP-004 establishes | Config | Connectivity check | Round-trip proof |
|---|---|---|---|---|---|
| SQL Server | Transactional domain data, event store (append-only) | Real connection; event-store table + append/read-back | `EOS_SQLSERVER_CONNECTION_STRING` (env) | Open connection, `SELECT 1` | Insert one `StoredEvent` row, read it back, assert equality |
| Redis | Ephemeral cache, distributed locks, Scheduler state | Real connection | `EOS_REDIS_CONNECTION_STRING` (env) | `PING` via `StackExchange.Redis` | `SET`/`GET`/`DEL` one test key |
| ChromaDB | Embeddings for Knowledge Graph search | Real connection | `EOS_CHROMADB_ENDPOINT` (env) | `GET /api/v2/heartbeat` | Create, list, delete one smoke-test collection |
| SQLite | Local/offline cache, edge-node queues | Real local-file connection | Reuses existing `StorageOptions.DataDirectory` (WP-002, unchanged) | Open/create file, `PRAGMA` no-op query | Create temp table, insert, select, drop |

## 8. Stores Explicitly Excluded
None of the four are excluded — all are in scope per the Roadmap. **Excluded within each store:** `KnowledgeNode` or any other domain schema (owned by WP-007); Redis keyspace conventions beyond the one test key (future subsystem WPs); ChromaDB production collections beyond the one smoke-test collection (future Memory/Knowledge WPs); RabbitMQ and OpenObserve (named in Part 12's "Start Infrastructure" step description but **not** named in WP-004's own Roadmap row's "Included components" or "Projects affected" — deferred; RabbitMQ is explicitly a future-only transport per Part 5 §5.1, OpenObserve is observability infrastructure with no owning WP yet in the 30-WP roadmap's early milestones).

## 9. Architecture Boundaries
All new infrastructure code stays inside `EOS.Infrastructure`. `EOS.Contracts`/`EOS.Application` are not modified. `EOS.Runner` gains one new `ProjectReference` (to `EOS.Infrastructure`) and extends two existing Bootstrap step *bodies* only (no new step, no change to `BootstrapRunner`'s orchestration model, `BootstrapResult` shape, or fail-fast/exit-code contract — per WP-002's own frozen Future Evolution rule). No new public `EOS.Contracts` interface. No circular reference (`EOS.Infrastructure` gains no new project reference).

## 10. Projects Affected
`EOS.Infrastructure` (new code), `EOS.Runner` (extends 2 existing step bodies + 1 new project reference), new `tests/EOS.Infrastructure.Tests`.

## 11. Files to Create
- `src/EOS.Infrastructure/DataStoreConnectionOptions.cs`
- `src/EOS.Infrastructure/StoreHealthResult.cs`
- `src/EOS.Infrastructure/DataStoreHealthChecker.cs`
- `src/EOS.Infrastructure/StoredEvent.cs`
- `src/EOS.Infrastructure/SqlEventStore.cs`
- `.env.example` (repo root)
- `tests/EOS.Infrastructure.Tests/EOS.Infrastructure.Tests.csproj`
- `tests/EOS.Infrastructure.Tests/DataStoreConnectionOptionsTests.cs` (unit)
- `tests/EOS.Infrastructure.Tests/SqlServerConnectivityTests.cs` (integration)
- `tests/EOS.Infrastructure.Tests/RedisConnectivityTests.cs` (integration)
- `tests/EOS.Infrastructure.Tests/ChromaDbConnectivityTests.cs` (integration)
- `tests/EOS.Infrastructure.Tests/SqliteConnectivityTests.cs` (integration)

## 12. Files to Modify
- `src/EOS.Runner/Bootstrap/BootstrapRunner.cs` — "Start Infrastructure" step body performs real connectivity checks via `DataStoreHealthChecker`, captures results, throws on any unhealthy store; "Health Check" step body asserts the captured results are all healthy (no repeated I/O).
- `src/EOS.Runner/EOS.Runner.csproj` — add `ProjectReference` to `EOS.Infrastructure`.
- `src/EOS.Infrastructure/EOS.Infrastructure.csproj` — add 3 `PackageReference`s.
- `EOS.slnx` — register `EOS.Infrastructure.Tests`.
- `.gitignore` — add `.env`.

## 13. Files That MUST NOT Change
`src/EOS.Contracts/**`, `src/EOS.Application/**`, `src/EOS.SharedKernel/**` (including `config/Storage.json`, `StorageOptions.cs`), `src/EOS.Orchestrator/**`, `tests/EOS.ArchitectureTests/**`, `tests/EOS.Runner.Tests/**`, `tests/EOS.Orchestrator.Tests/**`, `docs/EOS-Specification.md`, `docs/EOS-Implementation-Roadmap-v1.0.md`, `.coderabbit.yaml`, `README.md`, `LICENSE.md`.

## 14. Dependency Changes
`EOS.Runner → EOS.Infrastructure` (new `ProjectReference`). No other project reference changes. `EOS.Infrastructure` gains zero new project references (only NuGet packages).

## 15. NuGet Package Changes
Added to `EOS.Infrastructure.csproj` only:
- `Microsoft.Data.SqlClient` — the standard, only reasonable SQL Server client for .NET.
- `StackExchange.Redis` — the de facto standard .NET Redis client.
- `Microsoft.Data.Sqlite` — the standard Microsoft SQLite provider.

ChromaDB uses the BCL's `HttpClient` (its REST heartbeat/collections API, no dedicated client needed). No package added to any other project.

## 16. Configuration Changes
No change to any `config/*.json` file or any existing `Options` record (see §8's ambiguity note — this is the recommended resolution, pending your confirmation). New: `DataStoreConnectionOptions.FromEnvironment()` reads `EOS_SQLSERVER_CONNECTION_STRING`, `EOS_REDIS_CONNECTION_STRING`, `EOS_CHROMADB_ENDPOINT` from process environment variables via `Environment.GetEnvironmentVariable` (BCL, zero new package/layer); throws `InvalidOperationException` if any is missing (fail-closed, consistent with Part 10.3's posture). SQLite reuses `StorageOptions.DataDirectory` unchanged — no new field. `.env.example` documents the three variables with placeholder (non-real) values; real `.env` is gitignored and never committed.

## 17. Infrastructure Components
- `DataStoreConnectionOptions` — plain record, env-var-sourced.
- `StoreHealthResult(string StoreName, bool Healthy, string? Error)` — plain record.
- `DataStoreHealthChecker` — one concrete class, one method per store (`CheckSqlServerAsync`, `CheckRedisAsync`, `CheckChromaDbAsync`, `CheckSqliteAsync`) plus `CheckAllAsync` aggregating all four. No interface (no current second implementation).
- `StoredEvent` — flat record mirroring `EventEnvelope`'s fields for SQL persistence (payload pre-serialized to `PayloadJson` by the caller — `EOS.Infrastructure` does not depend on `EOS.Contracts`' generic `EventEnvelope<TPayload>` for storage, since SQL rows need a concrete, non-generic shape).
- `SqlEventStore` — one concrete class, `EnsureTableExistsAsync` (idempotent `CREATE TABLE IF NOT EXISTS`-equivalent), `AppendAsync`, `ReadByIdAsync`. No repository interface, no ORM, no migrations tool — raw `Microsoft.Data.SqlClient` with inline SQL.

## 18. Connection/Connectivity Flow
1. `BootstrapRunner`'s "Start Infrastructure" step: `DataStoreConnectionOptions.FromEnvironment()` → `new DataStoreHealthChecker(connectionOptions, storageOptions.DataDirectory)` → `await CheckAllAsync()` → if any `!Healthy`, throw `ConfigurationValidationException` (fail-closed) with the specific store's error; else store results in a closure variable.
2. "Health Check" step: assert the captured results list is non-null and all `Healthy` (no new I/O — the real check already happened).
3. `SqlEventStore` is exercised only by its own integration tests in this WP — not wired into `EventMediator` or any live path.

## 19. Failure Behavior
| Condition | Behavior |
|---|---|
| Required env var missing | `DataStoreConnectionOptions.FromEnvironment()` throws `InvalidOperationException` immediately — "Start Infrastructure" step fails closed |
| Malformed connection string | Underlying client throws on connect attempt; caught inside the per-store check method, returned as `StoreHealthResult(Healthy: false, Error: <message>)`; "Start Infrastructure" step throws, listing which store(s) failed |
| Store unreachable / connection refused | Same as above — caught, reported, fails closed |
| Auth failure | Same as above — caught, reported, fails closed |
| Timeout | Each client's default timeout applies (no custom timeout tuning — that is a future Resource Management WP concern, not WP-004's); on timeout, the client throws, caught and reported the same way |

No retries, no circuit breakers, no recovery pipeline — a single failed attempt fails the Bootstrap step closed, consistent with every existing Bootstrap step's behavior since WP-002.

## 20. Test Strategy

**Unit tests** (`tests/EOS.Infrastructure.Tests`, always runnable, no external service):
- `DataStoreConnectionOptions.FromEnvironment()` throws when a required variable is missing.
- `DataStoreConnectionOptions.FromEnvironment()` succeeds and captures correct values when all three are set.

## 21. Integration Test Strategy
Require the Docker Compose stack (SQL Server, Redis, ChromaDB) running per the Infrastructure Roadmap's Phase 3/4 convention — **not** Testcontainers (not already required by the repository, and Docker Compose is the specification's own established mechanism). SQLite tests always run (file-based, no external service). Each store gets: a real-connect test, a real write/read round-trip test, and a deliberately-wrong-connection-string test asserting a clean caught error (not a crash). If the Compose stack is not running when tests execute, the SQL Server/Redis/ChromaDB integration tests will fail with a clear connection error — this is expected and correctly distinguishes "not run" from "faked as passing." No test fakes a passing result when the real service is unavailable.

## 22. Acceptance Criteria (verbatim from Roadmap)
"Bootstrap's Health Check step (WP-002) reports all four stores healthy on a clean `docker compose up`."

## 23. Definition of Done
Derived from the established EOS workflow + this WP's roadmap row: `dotnet restore/build/test/format` all succeed; zero warnings; all existing tests (WP-001/002/003, 15 total) still pass; all new WP-004 tests pass (unit always; integration when the Compose stack is running); `EOS.ArchitectureTests` (R-00) passes; no unintended file changed (§13); no infrastructure leakage outside `EOS.Infrastructure`; no scope creep; CodeRabbit review completed via the PR workflow with valid findings addressed; working tree clean; normal merge to `main`; annotated `v0.4.0-wp004` tag; closure report.

## 24. KISS/YAGNI Justification
No interface for `DataStoreHealthChecker`/`SqlEventStore` — single implementation, single consumer (Bootstrap steps + this WP's own tests). No DI registration — `EOS.Runner`'s Bootstrap steps already construct dependencies directly (matching WP-002's own established pattern, not a new one). No async abstraction beyond what the underlying I/O already requires (`Task`-based, matching the existing `Func<CancellationToken, Task>` step signature). No new configuration file/layer — environment variables via the BCL, zero package. No ORM/migrations tool — raw ADO.NET, one inline idempotent table-creation statement. No retry/circuit-breaker — explicitly out of scope, future Resource Management/Protection Layer concern.

## 25. Explicit No-Over-Engineering Rules
Do not add `IEventStore`, `IHealthChecker`, or any provider-agnostic abstraction. Do not add a generic `IDataStoreConnection` interface "for consistency" across the four stores — each store's client API is fundamentally different (`SqlConnection`, `ConnectionMultiplexer`, `HttpClient`, `SqliteConnection`); forcing a shared interface would be premature unification with no current second implementation. Do not add a retry policy library. Do not add EF Core. Do not add a DI container registration layer. Do not add Testcontainers.

## 26. Future WP Boundaries
- `KnowledgeNode` schema and its persistence — WP-007.
- Real Redis keyspace conventions (Scheduler in-flight state, distributed locks) — the WPs that own the Scheduler (Milestone 6) and rate-limiting concerns.
- Production ChromaDB collections for Knowledge Graph embeddings — later Memory/Knowledge WPs (Milestone 4).
- Wiring `SqlEventStore` into `EventMediator`'s live publish path — a future WP once a real event needs durable persistence; WP-004 only proves the write-path mechanism exists.
- RabbitMQ, OpenObserve — not owned by any WP in this roadmap's early milestones; excluded here per §8.
- Connection resilience (retry/circuit-breaker) — future Resource Management/Protection Layer WPs (Milestone 5).

## 27. CodeRabbit Review Boundaries
**Branch:** `wp-004-data-store-foundations`. **Expected changed files:** exactly the files listed in §11/§12 — nothing else. **Files that MUST NOT change:** §13's list — a CodeRabbit finding touching any of those files is out of scope for this PR by definition. **Expected project references:** one new edge, `EOS.Runner → EOS.Infrastructure`. **Expected package changes:** exactly the three listed in §15, added to `EOS.Infrastructure.csproj` only. **Tests expected:** 2 unit + up to 12 integration (3 per store × 4 stores). **Acceptance criteria:** §22. **Explicit exclusions:** §8, §25, §26 — CodeRabbit suggestions matching any of those are to be classified OVER-ENGINEERING / OUT OF SCOPE and rejected with reference to this plan, not silently implemented.

## 28. Risks
- **Docker Compose stack does not exist in this repository or environment** (confirmed via direct inspection — zero containers running, no `docker-compose.yml` file anywhere). Integration tests for SQL Server/Redis/ChromaDB cannot pass until this is resolved. Flagged explicitly for your decision (§8, item 3) — not silently worked around.
- **Secret handling design (env vars vs. `Storage.json`)** is a judgment call, not dictated unambiguously by the frozen documents. Flagged explicitly for your confirmation (§8, item 2).
- Everything else: no architectural risk identified.

## 29. Implementation Sequence
1. Create `docker-compose.yml` + confirm stack runs (prerequisite — pending your direction, §8 item 3).
2. `DataStoreConnectionOptions` + unit tests.
3. `StoreHealthResult` + per-store check methods + `DataStoreHealthChecker` + `CheckAllAsync`.
4. `StoredEvent` + `SqlEventStore` (table creation, append, read-back).
5. Integration tests per store (connect, round-trip, bad-connection-string).
6. Extend `BootstrapRunner`'s "Start Infrastructure"/"Health Check" step bodies.
7. `.env.example`, `.gitignore` update.
8. Full local verification (`restore/build/test/format`, `EOS.ArchitectureTests`, `dotnet run --project EOS.Runner` against a running Compose stack).
9. Local Architecture/Self-Review Gate.
10. Push branch, open PR, CodeRabbit review, fix valid findings, re-verify, merge, tag, close.

## 30. Architecture Gate Checklist

| Check | Result |
|---|---|
| Specification compliance (Part 4, Part 12) | PASS |
| Roadmap compliance (WP-004 row, verbatim) | PASS |
| Project boundaries | PASS — infra code stays in `EOS.Infrastructure` |
| Dependency direction | PASS — one new edge, `EOS.Runner → EOS.Infrastructure`, matches Part 1 §1.1 |
| Vertical slice validity | PASS — real config → real connection → real check → real test evidence |
| Configuration design | PASS, pending confirmation (§8 item 2) |
| Infrastructure isolation | PASS — no leakage into `EOS.Contracts`/`EOS.Application` |
| Test quality | PASS — real connect + real round-trip + real negative case per store |
| KISS/YAGNI | PASS — every abstraction has a named, current consumer (§24) |
| No-over-engineering | PASS (§25) |
| Security/secrets | PASS, pending confirmation — no secret committed, env-var pattern matches Infrastructure Roadmap's own convention |
| Future WP isolation | PASS (§26) |
| CodeRabbit readiness | PASS (§27) |
| Scope creep | PASS — none found |
