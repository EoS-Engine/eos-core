# EOS Development Workflow

**Document Type:** Engineering Process Specification (not an architecture document)
**Status:** Mandatory for all Work Packages from WP-005 onward
**Basis:** The process actually followed for WP-001 through WP-004, as recorded in `docs/WP-00N-Implementation-Plan.md` and `docs/work-packages/WP-00N-Completion-Report.md`

This document does not define architecture. `docs/EOS-Specification.md` remains the single source of truth for architecture, and `docs/EOS-Implementation-Roadmap-v1.0.md` remains the single source of truth for Work Package scope and sequencing. This document defines only the **process** by which that frozen architecture and roadmap are turned into code.

---

## 1. Purpose

EOS is built incrementally, one Work Package at a time, against an already-approved and frozen architecture. This workflow exists because the alternative — implementing against an architecture that is still being decided, or implementing several capabilities at once — has a predictable failure mode: scope drifts, abstractions get built for requirements nobody has yet, and the codebase accumulates complexity that no single Work Package's acceptance criteria ever asked for.

**Why strict Work Packages.** Each Work Package (WP) is a small, independently verifiable slice of the roadmap. Keeping exactly one WP active at a time means every change in the repository's history can be traced to one plan, one review, and one closure report. A codebase built this way stays auditable years after the fact; a codebase built by several WPs in flight simultaneously does not.

**Why architecture comes before implementation.** The architecture (`docs/EOS-Specification.md` and its subsystem specifications) is frozen. Implementation decisions that would require changing that architecture are not implementation decisions — they are architecture decisions, and must be escalated, not made silently inside a WP. Reviewing architecture compliance before writing code is cheaper than discovering a violation after code exists and tests pass.

**Why KISS and YAGNI are mandatory, not aspirational.** Every WP in this project has shipped fewer lines than the roadmap's own estimate by consistently asking, for every proposed abstraction: "does this WP have a current consumer for this?" If the answer is no, the abstraction is removed from the plan before implementation begins. This is not a style preference — it is the mechanism that has kept WP-001 through WP-004 small, reviewable, and free of speculative infrastructure.

---

## 2. Core Principles

- **One Work Package Only.** Exactly one WP is ever in progress. A WP is not started until the previous one is formally closed.
- **Vertical Slice.** Every WP delivers a real, working, end-to-end path through the system — not an isolated layer, not scaffolding for a future WP.
- **KISS.** The simplest implementation that satisfies the WP's acceptance criteria is the correct one.
- **YAGNI.** Nothing is built for a requirement that does not exist yet, even if the roadmap names that requirement for a later WP.
- **No Over-Engineering.** Interfaces, factories, DI registrations, retry/resilience pipelines, generic frameworks, and configuration layers are added only when the current WP has a concrete, named consumer for them.
- **Frozen Specification.** `docs/EOS-Specification.md` and its subsystem specifications are not renegotiated during implementation. A perceived conflict is reported, not silently resolved.
- **Frozen Roadmap.** `docs/EOS-Implementation-Roadmap-v1.0.md` defines WP scope and sequencing. A WP's plan may narrow ambiguity within its own row; it does not expand scope beyond it.
- **Architecture First.** Every WP begins with a review of the specific specification sections it implements, before any code is written.
- **Test-First Verification.** A WP is not complete because it compiles. It is complete when its tests — including real integration tests against real infrastructure, where applicable — pass.
- **Real Infrastructure.** Where a WP's acceptance criteria require a real store or service, tests run against that real service. A mock is never the sole evidence that connectivity works.
- **No Fake Implementations.** No `NotImplementedException`, no placeholder return values, no TODO comments standing in for logic the WP was scoped to deliver.
- **No Scope Creep.** Anything discovered during implementation that belongs to a different WP is deferred and explicitly recorded, not absorbed into the current one.

---

## 3. Work Package Lifecycle

```
Planning
   |
Architecture Review
   |
Implementation
   |
Local Verification
   |
Architecture Gate
   |
Feature Branch Push
   |
Pull Request
   |
CodeRabbit Review
   |
Fix VALID Findings Only
   |
Re-Verification
   |
Merge
   |
Tag
   |
Closure
```

**Planning.** The relevant specification sections and the WP's own roadmap row are read directly from the repository (not recalled from memory), and a WP Implementation Plan is written per §4.

**Architecture Review.** The plan is checked against the frozen specification and roadmap for conflicts, ambiguity, and scope creep before any code exists. See §5.

**Implementation.** Code is written strictly to the approved plan, on a feature branch. See §6, §7.

**Local Verification.** The full verification suite (§8) is run before any self-review begins.

**Architecture Gate.** A structured self-review against the plan, the specification, and the KISS/YAGNI gate. See §9.

**Feature Branch Push.** The branch is pushed once local verification and the Architecture Gate both pass.

**Pull Request.** A PR is opened against `main`, describing scope, verification evidence, and explicit exclusions.

**CodeRabbit Review.** A real review must be observed on the PR before proceeding. See §10.

**Fix VALID Findings Only.** Every finding is classified; only VALID findings are fixed.

**Re-Verification.** The full verification suite is re-run after any fix.

**Merge.** A normal merge commit into `main`. See §11.

**Tag.** An annotated tag is created against the merged state. See §12.

**Closure.** A closure report is written and the WP is declared officially closed. See §13.

---

## 4. Planning Phase

A WP Implementation Plan (`docs/WP-00N-Implementation-Plan.md`) must contain, at minimum:

- **Revision** and **Source of Truth** — the exact specification sections and roadmap row the plan is built from.
- **Current Repository Baseline** — the actual state of the affected projects, inspected directly, not assumed.
- **Objective** and **Exact Roadmap Scope** — copied or paraphrased directly from the roadmap row, not reinterpreted.
- **Vertical Slice Definition** — the concrete, real, end-to-end path the WP will prove works.
- **Scope**, split into **Included** and **Explicitly Excluded** — every excluded item states which future WP owns it.
- **Projects Affected**, **Files to Create**, **Files to Modify**, and **Files That Must Not Change**.
- **Dependency Changes** and **Package Changes** — each new package or project reference justified against a current need.
- **Configuration Changes**, if any, with schema, validation, and fail-closed behavior specified.
- **Test Strategy**, separating unit tests from integration tests, and naming exactly which real services the integration tests require.
- **Acceptance Criteria** — copied verbatim from the roadmap where the roadmap states one.
- **Definition of Done** — derived from the roadmap and this workflow, never invented ad hoc.
- **Risks** and **Future WP Boundaries** — anything the plan deliberately does not solve, and why.
- **KISS/YAGNI Justification** — for every abstraction in the plan, the question "why does this WP need this now?" is answered explicitly.

The plan is presented for approval before implementation begins. Implementation does not start on an unapproved plan.

---

## 5. Architecture Review

**Purpose.** To catch specification conflicts, roadmap scope creep, and unjustified abstractions while they are still one paragraph in a plan document, not a merged pull request.

**Review scope** is limited to the specification sections and roadmap row relevant to the current WP. Unrelated specifications are not re-litigated.

The review explicitly checks:
- **Specification compliance** — does the plan match the cited sections of `docs/EOS-Specification.md`?
- **Roadmap compliance** — does the plan match the WP's own roadmap row's Included/Excluded/Projects Affected/Acceptance Criteria fields exactly?
- **Architecture boundaries** — does every new file live in the project the specification assigns it to?
- **Dependency review** — does every new project reference and package have a named, current justification?
- **KISS/YAGNI gate** — is every abstraction rejected unless a current consumer inside this WP requires it?
- **Vertical Slice validation** — does the plan describe a real, provable, end-to-end path, not disconnected scaffolding?

**Approval states:**
- **Approved** — implementation may begin exactly as planned.
- **Approved With Required Changes** — specific, enumerated changes must be made to the plan before implementation begins; no other changes are implied.
- **Rejected** — the plan conflicts with the frozen specification or roadmap and must be redesigned before resubmission.

---

## 6. Implementation Rules

- Never implement more than one Work Package at a time.
- Never anticipate or partially implement a future Work Package, even when the current WP's own code would make it easier later.
- Never create an abstraction, interface, or extension point without a consumer inside the current WP.
- No interface is introduced for a class with exactly one implementation and no substitutability requirement.
- No configuration field is added unless the current WP reads it.
- No `TODO` implementations, no placeholder methods, no `NotImplementedException` standing in for real logic.
- Existing, previously-closed Work Packages' files are modified only when the current WP's own approved plan explicitly requires it, and the reason is recorded at the point of change — not modified for unrelated cleanup or style.

---

## 7. Feature Branch Policy

Every Work Package starts from a dedicated feature branch, named:

```
wp-XXX-short-descriptive-name
```

for example `wp-004-data-store-foundations`. Development never happens directly on `main`. A branch is never merged while its own verification suite (§8) or Architecture Gate (§9) is failing or incomplete.

---

## 8. Local Verification Checklist

Run in full before any Architecture Gate or PR:

- `dotnet restore`
- `dotnet build` — zero errors, zero warnings
- `dotnet test` — every existing test plus every new test passing
- `dotnet format --verify-no-changes`
- `git diff --check`
- The architecture fitness test (`EOS.ArchitectureTests`, R-00: no circular project references) passing against the current graph
- Bootstrap verification, where the WP touches `EOS.Runner`/`BootstrapRunner`: `dotnet run --project src/EOS.Runner` reaching Ready
- Docker verification, where the WP requires real infrastructure: the relevant containers healthy and reachable before integration tests are run, never assumed

A verification step that cannot run because a real dependency (a database, a running container) is unavailable is reported as **not run**, never silently skipped and never faked as passing.

---

## 9. Architecture Gate

**What is reviewed:** the actual diff, not the plan's description of it — specification compliance, roadmap compliance, vertical-slice integrity, dependency direction, infrastructure isolation, test quality, security/secrets handling, and every file touched outside the plan's declared boundary.

**When it happens:** after implementation and local verification both pass, and before the feature branch is pushed.

**Who approves:** the same disciplined self-review process used for every WP to date; for changes affecting more than the current WP's declared boundary, explicit human approval is required before proceeding.

**Required output:** a findings list, each entry classified by severity (Critical / High / Medium / Low / Informational), with an explicit statement of whether it blocks proceeding to a PR.

**Blocking vs. non-blocking:** any Critical or High finding blocks the PR until fixed. A Medium finding blocks the PR only if it represents a scope, specification, or architecture violation; a Medium finding that is a legitimate, documented, non-blocking observation may be recorded and deferred. Low and Informational findings are recorded but never used to justify expanding the WP's scope.

---

## 10. CodeRabbit Policy

A Pull Request is required before any CodeRabbit review can occur — CodeRabbit reviews PRs, not local branches or direct commits to `main`.

**Real review only.** A review is never claimed to have happened unless its result is actually visible on the PR (a completed status check, visible comments, or an explicit "no issues found" outcome). If CodeRabbit cannot be reached or has not yet completed, that fact is reported plainly.

Every finding CodeRabbit posts is classified as exactly one of:

- **VALID** — a real defect, correctly identified; fixed before merge.
- **INVALID** — the finding is factually wrong or lacks context CodeRabbit did not have (for example, flagging a dependency that is in fact a required security-vulnerability pin); rejected, with the reasoning recorded as a reply on the PR.
- **OUT OF SCOPE** — the finding describes real, legitimate work, but work that belongs to a different, later Work Package; deferred, with the owning WP named if known.
- **OVER-ENGINEERING** — the finding recommends an abstraction, pattern, or generalization the current WP has no consumer for; rejected on KISS/YAGNI grounds, with the reasoning recorded.

Every classification is documented on the PR itself, not only in the closure report — CodeRabbit's own future reviews, and any future contributor, must be able to see why a given suggestion was or was not acted on.

Exception: Work Packages completed before adoption of the Feature Branch + Pull Request + CodeRabbit workflow remain valid historical exceptions where explicitly documented (for example WP-003). This workflow is mandatory from WP-005 onward.

---

## 11. Merge Policy

**Allowed:** a normal merge commit into `main`.

**Forbidden**, unless explicitly and separately approved for a specific, stated reason:
- Force push
- History rewrite
- Squash merge
- Rebase of shared history
- Reset of `main` after review has begun

The default is always the safest, most reversible option. A merge commit preserves the exact commit history a reviewer already approved.

---

## 12. Tagging Policy

Every closed Work Package is tagged using an **annotated** tag (never a lightweight tag), in the form:

```
v0.X.0-wpXXX
```

where `X` in `v0.X.0` matches the Work Package number (for example, `v0.4.0-wp004` for WP-004). The tag is created only after the Pull Request has been merged, and it references the resulting merge commit — the implementation state exactly as it exists in `main` after review, not an intermediate commit on the feature branch and not a subsequent documentation-only commit.

---

## 13. Closure Policy

A Work Package's closure report (`docs/work-packages/WP-00N-Completion-Report.md`) records:

- A summary of what was implemented and the vertical slice delivered.
- Every file created and modified.
- Every dependency added, with justification.
- Test results, build results, and format results.
- The CodeRabbit review outcome, with every finding's classification.
- The Architecture Gate outcome, including any defects found and fixed during self-review.
- The implementation commit SHA, any fix commit SHA, the merge commit SHA, and the tag's object SHA.
- Confirmation that local and remote `main` match, and that the tag matches on both.
- Final repository status.

A Work Package becomes officially **CLOSED** only when all of the following are true simultaneously: the plan was implemented as approved, all tests pass, the Architecture Gate passed, CodeRabbit review actually completed and every finding was classified and resolved, the PR was merged normally, the tag was created and pushed, the closure report was written and committed, and the working tree is clean. A WP is never declared closed based on partial completion of this list.

---

## 14. Definition of Done

- [ ] Implementation matches the approved plan exactly
- [ ] All existing tests still pass; all new tests pass
- [ ] `dotnet build` — zero warnings, zero errors
- [ ] `dotnet format --verify-no-changes` passes
- [ ] Architecture Gate passed, with no unresolved Critical, High, or scope-violating Medium finding
- [ ] CodeRabbit review actually completed (not assumed, not skipped)
- [ ] Every CodeRabbit finding classified as VALID / INVALID / OUT OF SCOPE / OVER-ENGINEERING, with VALID findings fixed
- [ ] Documentation (plan and completion report) accurately reflects the final implementation
- [ ] Pull Request merged normally into `main`
- [ ] Annotated tag created and pushed, referencing the merge commit
- [ ] Closure report written and committed
- [ ] Working tree clean; local and remote `main` and tag all match
- [ ] No scope beyond the approved Work Package was implemented.

---

## 15. Workflow Evolution

This workflow is version-controlled.

Changes to this workflow require explicit architectural approval and must not be introduced implicitly as part of an unrelated Work Package.

Any proposed workflow change should clearly document:
- why the existing workflow is insufficient;
- which future Work Packages are affected;
- whether the change is backward compatible.

The workflow itself is treated as an engineering specification and evolves deliberately rather than opportunistically.

---

## 16. Future Maintainers

This workflow is mandatory for every Work Package from WP-005 onward. It is not a suggestion, and it is not something to streamline under time pressure — every step exists because skipping an equivalent step earlier in the project's history would have let scope, complexity, or an unverified assumption into the codebase silently.

Any deviation from this workflow — skipping the Architecture Gate, merging before a real CodeRabbit review has completed, expanding a Work Package's scope mid-implementation, or introducing an abstraction without a current consumer — requires explicit architectural approval before it happens, not a retroactive justification after the fact. If a step in this document seems to be blocking legitimate progress, the correct response is to raise that concern and get this document changed deliberately, not to bypass it quietly.

The frozen specification and the frozen roadmap are the source of truth for *what* EOS is. This document is the source of truth for *how* it gets built.
