# WP-004 Completion Report — Data Store Foundations

# Summary

Established real, tested connections from `EOS.Infrastructure` to SQL Server, Redis, ChromaDB, and SQLite, per Constitution Part 4's store-ownership table, and the SQL Server append-only event-store write path (Part 3 §3.2). The health-check surface is consumed by `BootstrapRunner`'s existing "Start Infrastructure"/"Health Check" steps — no new step, no orchestration-model change.

# Vertical Slice Delivered

`.env` → `DataStoreConnectionOptions.FromEnvironment()` → concrete store clients → real connections → `DataStoreHealthChecker` → `BootstrapRunner` "Start Infrastructure" (captures results, fails closed on any unhealthy store) → "Health Check" (asserts captured results) → observable per-store result. Independently: `SqlEventStore` appends one `StoredEvent` and reads it back with all fields verified — not wired into `EventMediator`'s live path (future-WP scope).

Verified live against the real local Docker Compose stack: `dotnet run --project src/EOS.Runner` — all 10 Bootstrap steps succeed, "Start Infrastructure" performs real connections (~700–800ms), Ready reached, exit code 0. Matches the Roadmap's acceptance criterion verbatim: *"Bootstrap's Health Check step reports all four stores healthy on a clean `docker compose up`."*

# Files Created

- `src/EOS.Infrastructure/DataStoreConnectionOptions.cs`, `DataStoreHealthChecker.cs`, `StoreHealthResult.cs`, `StoredEvent.cs`, `SqlEventStore.cs`
- `tests/EOS.Infrastructure.Tests/` (`AssemblyInfo.cs`, `EnvFileLoader.cs`, `DataStoreConnectionOptionsTests.cs`, `SqlServerConnectivityTests.cs`, `RedisConnectivityTests.cs`, `ChromaDbConnectivityTests.cs`, `SqliteConnectivityTests.cs`, `.csproj`)
- `docker-compose.yml`, `.env.example`
- `docs/WP-004-Implementation-Plan.md`

# Files Modified

- `src/EOS.Runner/Bootstrap/BootstrapRunner.cs` — extended "Start Infrastructure"/"Health Check" step bodies only (10-step sequence, `BootstrapResult` shape, and exit-code logic unchanged)
- `src/EOS.Runner/EOS.Runner.csproj` — +1 `ProjectReference` to `EOS.Infrastructure`
- `src/EOS.Infrastructure/EOS.Infrastructure.csproj` — +4 `PackageReference`s (see Dependencies below)
- `EOS.slnx`, `.gitignore` (+`.env`)

No WP-001/002/003 file touched (`EOS.Contracts`, `EOS.Application`, `EOS.SharedKernel`, `EOS.Orchestrator`, and all three pre-existing test projects confirmed byte-identical to pre-WP-004 `main` throughout).

# Dependencies Added

`Microsoft.Data.SqlClient`, `StackExchange.Redis`, `Microsoft.Data.Sqlite` (the three approved packages), plus `SQLitePCLRaw.bundle_e_sqlite3` — a direct pin required to fix a high-severity CVE ([GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q)) in a transitive dependency of `Microsoft.Data.Sqlite` itself; without it, `dotnet restore` fails outright (`NU1903` warning-as-error). CodeRabbit flagged this as a 4th unapproved package during review; classified INVALID and rejected with this reasoning (recorded on the PR).

# Tests

29 total, all passing, confirmed stable across repeated runs:
- `EOS.ArchitectureTests`: 1/1 (R-00 holds against the larger graph)
- `EOS.Orchestrator.Tests`: 5/5 (unchanged, WP-003 unaffected)
- `EOS.Runner.Tests`: 9/9 (unchanged, WP-002 unaffected — required the same env vars `BootstrapRunner` now genuinely needs)
- `EOS.Infrastructure.Tests`: 14/14 (new) — 2 unit (env var presence/absence) + 12 integration against the real running Docker stack (connect + round-trip + invalid-config negative case per store, plus the SQL event-store append/read-back proof), zero mocks

# Build Results

```
dotnet restore EOS.slnx → succeeded, no errors
dotnet build EOS.slnx   → Build succeeded. 0 Warning(s), 0 Error(s)
```

# Format Results

`dotnet format EOS.slnx --verify-no-changes` → exit 0. `git diff --check` → exit 0.

# CodeRabbit Summary

Real review completed on PR #1 (status `SUCCESS`, 3 actionable comments):
1. Rethrow `OperationCanceledException` in SQL Server/ChromaDB health checks — **VALID, fixed**.
2. Remove the `SQLitePCLRaw.bundle_e_sqlite3` pin — **INVALID, rejected** (would break the build and reintroduce a known CVE; documented above and on the PR).
3. Assert `OccurredAt` on event-store read-back — **VALID, fixed**.

Fix commit: `79fe436`.

# Architecture Gate Summary

Local Architecture/Self-Review Gate passed prior to PR. Two real defects were found and fixed during self-review, both resolved entirely within WP-004's own files:
1. Test-isolation bug — `DataStoreConnectionOptionsTests` was clearing (not restoring) real process env vars, causing order-dependent flakiness across the test assembly. Fixed by save/restore; confirmed stable across repeated full-suite runs.
2. Tilde-expansion bug — `config/Storage.json`'s WP-002 baseline value (`~/eos/data`) had never been consumed as a real filesystem path before WP-004; .NET doesn't expand `~`, producing a stray literal directory. Fixed inside `DataStoreHealthChecker` only — `Storage.json`/`StorageOptions.cs` were not touched.

No CRITICAL, HIGH, or scope-violating MEDIUM findings at any point.

# Git Record

- **Implementation commit:** `8b65f89` — "Implement WP-004: Data Store Foundations"
- **CodeRabbit fix commit:** `79fe436` — "Address CodeRabbit findings: propagate cancellation, assert OccurredAt"
- **Merge commit:** `0335b66c78a62efb639ce3fd6d564e31cdd0481c` (normal merge, no squash, no rebase, no history rewrite)
- **Tag:** `v0.4.0-wp004` (annotated, object `868d3e1b7df22041d74ec10a049dab841075e0fe`), points to the merge commit above
- **PR:** [EoS-Engine/eos-core#1](https://github.com/EoS-Engine/eos-core/pull/1)
- **Remote:** `origin = https://github.com/EoS-Engine/eos-core.git`

# Repository Status

Local `main` == `origin/main` == merge commit `0335b66`. Working tree clean. Tag present locally and remotely, verified matching. WP-005 not started.
