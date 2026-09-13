# Contributing to EOS

**Document Type:** Developer guide
**Authority:** This guide introduces **no new rules**. It collects the contribution-relevant parts of `docs/Development-Workflow.md`, `docs/governance/EOS-Engineering-Governance-v2.md`, `README.md`, and the Constitution (`docs/EOS-Specification.md`) into one place. Where it and those documents differ, they win.

Read `docs/guides/Engineering-Workflow-Guide.md` first if you have not — it explains *why* the process is shaped this way. This guide is the practical checklist.

---

## 1. Before You Write Any Code

From `README.md`'s own contributing section:

- **Read the architecture before proposing code.** A specification exists for essentially every subsystem in this repository. A change that contradicts it needs an architecture discussion first, not a pull request.
- **One increment at a time.** Changes are scoped to a single, well-defined unit of work with its own plan and its own closure — not bundled with unrelated improvements.
- **No speculative engineering.** Don't add an abstraction, dependency, or configuration surface for a need that doesn't exist yet.
- **No redesign without approval.** If part of the architecture seems wrong, that is a valid and welcome observation — raised and resolved as an architecture discussion, not smuggled in as an implementation detail.
- **Prove it, don't assert it.** A pull request is expected to include the evidence that it works.

Issues and discussions are the right place to propose architectural changes or new capabilities **before** any code is written.

---

## 2. Repository Setup

Follow `docs/guides/Quick-Start.md` in full. The short version:

```bash
git clone https://github.com/EoS-Engine/eos-core.git
cd eos-core
cp .env.example .env                      # fill in real values; never commit this
mkdir -p "$HOME/eos/data"/{sql,redis,chroma}
docker compose up -d && docker compose ps # wait for all three healthy
ollama pull qwen2.5-coder:7b

export EOS_SQLSERVER_CONNECTION_STRING='Server=localhost,1433;Database=master;User Id=sa;Password=<pw>;TrustServerCertificate=True'
export EOS_REDIS_CONNECTION_STRING='localhost:6379'
export EOS_CHROMADB_ENDPOINT='http://localhost:8000'

dotnet restore && dotnet build && dotnet run --project src/EOS.Runner
```

You are ready when bootstrap reaches `[10/10] Ready - Success`.

**Never commit `.env`.** It is git-ignored. The Infrastructure Roadmap calls out committing it even briefly as a mistake worth avoiding entirely, because `git filter-repo` cleanup afterwards is disruptive.

---

## 3. Branching

Every roadmap Work Package starts from a dedicated feature branch off `main`:

```
wp-XXX-short-descriptive-name
```

A separately approved additive or remediation-only change does not invent a Work Package merely to obtain this name. Follow the authorization and post-implementation-finding rules in `docs/guides/Engineering-Workflow-Guide.md` §13; this guide does not define a separate generic branch taxonomy for those cases.

for example `wp-004-data-store-foundations`. **Development never happens directly on `main`.**

A branch is never merged while its own verification suite or Architecture Gate is failing or incomplete.

---

## 4. Architecture and Specification Review

Before implementation:

1. **Execute STEP-0.** Read `docs/governance/EOS-Engineering-Governance-v2.md` and evaluate the Reopening Criteria (§4) against any claim that would otherwise trigger an architecture review. Work through `docs/governance/Review-Checklist.md`.
2. **Re-read the relevant specification sections from the repository** — not from memory, not from a previous report.
3. **Write the plan** (§5) and get it approved.

If the Reopening Criteria are not satisfied, Architecture Review is skipped and Delta Review applies: only the active unit of work and the regressions it introduces are in scope. Previously closed Work Packages are immutable.

---

## 5. The Plan

`docs/Development-Workflow.md` §4 requires an Implementation Plan before implementation begins. It must contain, at minimum:

- **Revision** and **Source of Truth** — the exact specification sections and roadmap row it is built from
- **Current Repository Baseline** — inspected directly, not assumed
- **Objective** and **Exact Roadmap Scope**
- **Vertical Slice Definition** — the concrete, real, end-to-end path this work will prove
- **Scope**, split into **Included** and **Explicitly Excluded**, with every exclusion naming the future work that owns it
- **Projects Affected**, **Files to Create**, **Files to Modify**, **Files That Must Not Change**
- **Dependency Changes** and **Package Changes**, each justified against a current need
- **Configuration Changes**, with schema, validation, and fail-closed behaviour
- **Test Strategy**, separating unit from integration, naming exactly which real services the integration tests require
- **Acceptance Criteria** — copied verbatim from the roadmap where one exists
- **Definition of Done**
- **Risks** and **Future Boundaries**
- **KISS/YAGNI Justification** — for every abstraction, an explicit answer to "why does this need this now?"

Approval is one of **Approved**, **Approved With Required Changes** (specific enumerated changes only — nothing else is implied), or **Rejected**.

**Implementation does not start on an unapproved plan.**

---

## 6. Scope Control While Implementing

- Never implement more than one unit of work at a time.
- Never anticipate or partially implement future work, even when the current code would make it easier later.
- Never create an abstraction, interface, or extension point without a consumer **inside the current scope**.
- No interface for a class with exactly one implementation and no substitutability requirement.
- No configuration field the current work does not read.
- No `TODO` implementations, no placeholder methods, no `NotImplementedException` standing in for real logic.
- Previously-closed work's files are modified **only** when the approved plan explicitly requires it — and the reason is recorded at the point of change, not just in the PR description.

Anything discovered mid-implementation that belongs elsewhere is **deferred and explicitly recorded**, never absorbed.

---

## 7. Coding Expectations

### Enforced automatically

`Directory.Build.props` applies to every project:

```xml
<TargetFramework>net10.0</TargetFramework>
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
<EnableNETAnalyzers>true</EnableNETAnalyzers>
```

**Every warning is a build failure.** Do not suppress an analyzer to get past one — that changes the project's quality posture and needs approval.

`.editorconfig` sets: LF line endings, final newline, trimmed trailing whitespace, UTF-8; 4-space indent for `.cs`, 2-space for `.json`/`.yml`/`.yaml`; `using` directives **outside** the namespace.

### Conventions the codebase actually follows

Match the surrounding code. Observably:

- **File-scoped namespaces** (`namespace EOS.Knowledge;`) everywhere.
- **`sealed`** on essentially every class. `record` for data, `class` for behaviour.
- **Primary constructors** for dependency injection by hand (`public sealed class ProtectionGate(PolicyEngine policyEngine, …)`).
- **Nullable reference types** honoured — no `!` suppressions without a reason at the call site.
- **`CancellationToken cancellationToken = default`** as the trailing parameter on async methods.
- **`ILogger<T>`** from `Microsoft.Extensions.Logging.Abstractions`, with structured message templates — never string interpolation into the message.
- **Comments cite their authority.** The prevailing style is to name the specification section, ADR, or decision that justifies a non-obvious choice — for example `// §14.1 Medium tier: quick synchronous permission + resource-budget check.` Where a component is deliberately inert, stubbed, or unwired, the code says so **in place** and explains why. Continue that: a reviewer should never have to guess whether something is unfinished or intentional.
- **Honest stubs over fake implementations.** `NeverReadRecentlyStub`, `NoActiveRetentionHoldsStub`, and `NoSelfReferentialTasksProvenanceQueryClient` each return the architecturally correct answer for a data source that does not exist, and each documents why. This is the accepted pattern — a `NotImplementedException` is not.

### Architectural constraints the compiler and tests enforce

- **`EOS.Contracts` is the only cross-subsystem language.** If subsystem A needs to read something owned by subsystem B, declare a narrow query interface in A and implement it in `src/EOS.Runner/Program.cs` — the **Composition Root Adapter Pattern** (ADR-015-001). Do not add a project reference between subsystems.
- **`EOS.Runner` is the only project that may reference everything.** It contains wiring, not logic.
- **No dependency cycles**, enforced by `tests/EOS.ArchitectureTests`.
- **`EOS.Dashboard` may reference only `EOS.Contracts`.**
- Adding a project reference to `EOS.AIProvider` or `EOS.Gates` will fail an architecture test unless the project is on that test's whitelist. **Editing the whitelist to pass is not the fix** — it needs an architecture conversation.
- `EOS.SeniorEngineer` may reference only `EOS.Contracts`; the architecture suite enforces that exact set.

### Dependencies

Production projects currently use five external dependency packages: `Microsoft.Data.SqlClient`, `Microsoft.Data.Sqlite`, `SQLitePCLRaw.bundle_e_sqlite3`, `StackExchange.Redis`, and `Microsoft.Extensions.Logging.Abstractions`. Test projects additionally use the standard xUnit runner and .NET test SDK packages. Any new production dependency requires a named, current justification in the plan and approval at Architecture Review before code uses it; do not treat the present count as a permanent invariant.

---

## 8. Tests

- Test projects are organized by tested responsibility; not every source project has a dedicated test assembly. Use the current named inventory in `docs/guides/Testing-Guide.md` rather than assuming a one-to-one mapping.
- xUnit, `[Fact]`/`[Theory]`, one test class per unit under test.
- **Real infrastructure, not mocks, where the acceptance criteria call for it.** `docs/Development-Workflow.md` §2: "A mock is never the sole evidence that connectivity works."
- Hand-written test doubles (`TestDoubles.cs`, `NoOp*`, `NeverCalled*`, `Capturing*` classes) — there is **no mocking framework** in this repository. Do not add one.
- If your test queries a shared SQL Server table globally, follow the existing precedent and add `[assembly: CollectionBehavior(DisableTestParallelization = true)]` in that project's `AssemblyInfo.cs` rather than inventing a cleanup convention.
- Prefer a deterministic test where the behaviour permits one; state plainly when a test depends on infrastructure or on host timing.

See `docs/guides/Testing-Guide.md`.

---

## 9. Documentation

Update documentation **in the same change** that makes it necessary:

| If you change… | Also update… |
|---|---|
| A public API | `docs/guides/API-Reference.md` |
| A command, configuration file, or setup step | `docs/guides/Quick-Start.md` |
| Project structure, wiring, or an extension point | `docs/guides/Developer-Guide.md` |
| Test organization or verification commands | `docs/guides/Testing-Guide.md` |
| A user-visible failure mode | `docs/guides/Troubleshooting-Guide.md` |

Do **not** document planned functionality as implemented. Where something cannot be established from the repository, mark it explicitly as `Not currently implemented / not currently verifiable`. Every command in the documentation must be runnable against the current repository.

Specifications, ADRs, roadmap rows, and completion reports follow their own rules — see `docs/guides/Engineering-Workflow-Guide.md` §15.

---

## 10. Local Verification — Before You Push

Run the whole checklist. Not a subset.

```bash
dotnet restore
dotnet build                        # zero errors, zero warnings
dotnet test                         # all existing plus all new
dotnet format --verify-no-changes
git diff --check
```

Plus, where applicable:

- `dotnet run --project src/EOS.Runner` reaching `Ready` — required if you touched `EOS.Runner` or `BootstrapRunner`
- containers **verified** healthy before integration tests, never assumed

> A step that cannot run because a dependency is unavailable is reported as **not run** — never skipped silently, never reported as passing.

### Architecture Gate

After verification passes and **before** pushing: self-review the **actual diff**, not the plan's description of it. Cover specification and roadmap compliance, vertical-slice integrity, dependency direction, infrastructure isolation, test quality, secrets handling, and every file touched outside the plan's declared boundary.

Produce a findings list, each classified Critical / High / Medium / Low / Informational, each stating whether it blocks the PR. Critical and High always block. A Medium blocks only if it is a scope, specification, or architecture violation. Low and Informational are recorded but never used to justify expanding scope.

---

## 11. Commits

Observed convention on `main`:

```
feat(knowledge): bound query_similar candidate retrieval (WP-031, Finding #2, ADR-005)
fix(knowledge): deterministic bounded retrieval ordering (WP-031 F-2)
fix(knowledge): isolate deterministic retrieval tie-breaker test (WP-031 F-3)
```

- Conventional-commit prefix (`feat`, `fix`) with a subsystem scope.
- An imperative, specific subject.
- A reference to the authorizing unit of work, finding, or ADR.

Some historical commits use a plain imperative subject (`Harden WP-030 backup and restore-drill security`) — both forms exist; the scoped form is the more recent convention.

Keep commits scoped. Separate findings get separate commits, as the WP-031 sequence above shows.

**Do not commit:** `.env`, real secrets or connection strings, build output (`bin/`, `obj/`), or anything under `EOS_DATA_DIR`.

---

## 12. Pull Requests

1. Push the feature branch once local verification **and** the Architecture Gate both pass.
2. Open a PR against `main` describing **scope**, **verification evidence**, and **explicit exclusions**.
3. Wait for a **real** CodeRabbit review. A review is never claimed to have happened unless its result is actually visible on the PR. If it cannot be reached or has not completed, say so plainly.
4. Classify **every** finding as exactly one of:

   | Classification | Action |
   |---|---|
   | **VALID** | Fix before merge |
   | **INVALID** | Reject, with reasoning recorded as a PR reply |
   | **OUT OF SCOPE** | Defer, naming the owning future work if known |
   | **OVER-ENGINEERING** | Reject on KISS/YAGNI grounds, with reasoning recorded |

   Record every classification **on the PR itself**, not only in the closure report.
5. Fix **VALID findings only**, and only to the minimum necessary extent.
6. Re-run the full verification suite after any fix.

CodeRabbit is advisory (`Reviewer-Operating-Rules.md` Rule 7). The Constitution, Roadmap, Specifications, Development Workflow, and Governance remain authoritative.

---

## 13. Merge

**Allowed:** a normal merge commit into `main`.

**Forbidden** unless explicitly and separately approved for a specific, stated reason:

- Force push
- History rewrite
- Squash merge
- Rebase of shared history
- Reset of `main` after review has begun

A merge commit preserves the exact history a reviewer already approved.

**Tag.** Every closed roadmap Work Package gets an **annotated** tag (never lightweight) of the form `v0.X.0-wpXXX`, created after the merge and referencing the resulting merge commit. A separately authorized remediation follows its recorded authorization and the Engineering Workflow Guide §13; do not create a WP-shaped tag by implication.

**Closure.** Write the completion report in `docs/work-packages/` recording what was implemented, every file created and modified, every dependency added with justification, build/test/format results, the CodeRabbit outcome with every finding's classification, the Architecture Gate outcome, the implementation/fix/merge commit SHAs and the tag object SHA, confirmation that local and remote `main` and the tag all match, and the final repository status.

### Definition of Done

- [ ] Implementation matches the approved plan exactly
- [ ] All existing tests still pass; all new tests pass
- [ ] `dotnet build` — zero warnings, zero errors
- [ ] `dotnet format --verify-no-changes` passes
- [ ] Architecture Gate passed, with no unresolved Critical, High, or scope-violating Medium finding
- [ ] CodeRabbit review actually completed (not assumed, not skipped)
- [ ] Every finding classified, with VALID findings fixed
- [ ] Documentation accurately reflects the final implementation
- [ ] PR merged normally into `main`
- [ ] Annotated tag created and pushed, referencing the merge commit
- [ ] Closure report written and committed
- [ ] Working tree clean; local and remote `main` and tag all match
- [ ] No scope beyond the approved work was implemented

---

## 14. If Something Blocks You

Do not route around it.

- **The specification and the code appear to disagree** → report it with evidence. Do not silently pick a reading (`Development-Workflow.md` §2).
- **You need a new dependency, abstraction, or configuration surface** → put it in the plan with a current-need justification and get it approved. Do not add it first.
- **An architecture test fails** → treat it as an architecture conversation, not a whitelist edit.
- **A workflow step seems to be blocking legitimate progress** → raise the concern and get the document changed deliberately (`Development-Workflow.md` §15/§16). Never bypass it quietly, and never justify a deviation retroactively.
- **You found something real after the work closed** → it does not reopen closed work. Evaluate it against the Reopening Criteria, solve it additively if you can, and record it — the WP-031 sequence in `docs/guides/Engineering-Workflow-Guide.md` §13 is the worked template. If it cannot be solved additively, write it down with evidence and stop, as `docs/Governance-Change-Proposal-001.md` does.

Every step of this process exists because skipping an equivalent step earlier would have let scope, complexity, or an unverified assumption into the codebase silently.
