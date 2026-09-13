# EOS Engineering Workflow Guide

**Document Type:** Developer guide — a navigable summary of the project's existing governance
**Audience:** Anyone about to change something in this repository
**Authority:** This guide is **not** a source of truth. It summarizes and points into the documents that are:

| Concern | Authoritative document |
|---|---|
| Architecture | `docs/EOS-Specification.md` (the Constitution) and the subsystem specifications |
| Work Package scope and sequencing | `docs/EOS-Implementation-Roadmap-v1.0.md` |
| How work gets done | `docs/Development-Workflow.md` |
| Review policy since WP-019 | `docs/governance/EOS-Engineering-Governance-v2.md` + `docs/governance/Governance-Ratification.md` |
| Reviewer conduct | `docs/governance/Reviewer-Operating-Rules.md`, `docs/governance/Review-Checklist.md` |

If this guide and any of those disagree, they win. Report the discrepancy.

---

## 1. Architecture-First Development

The organizing rule of this project: **the architecture is decided, written down, and frozen before any code implementing it is written.** Implementation exists to realize the architecture, not to discover it.

`docs/Development-Workflow.md` §1 states the reason plainly: implementing against an architecture that is still being decided has a predictable failure mode — scope drifts, abstractions get built for requirements nobody has yet, and complexity accumulates that no acceptance criterion ever asked for.

Practically, this means every Work Package begins by **re-reading the relevant specification sections from the repository** — not from memory, not from a previous report — and only then writing a plan.

---

## 2. Frozen Specifications

Ten subsystem specifications plus the Constitution live in `docs/`. They are **frozen**: they are not renegotiated during implementation.

- A perceived conflict between the specification and reality is **reported, not silently resolved** (`Development-Workflow.md` §2).
- The roadmap is likewise frozen. A Work Package's plan may narrow ambiguity *within its own row*; it never expands scope beyond it.

When a specification genuinely turns out to be underspecified for the work at hand, the resolution is an **ADR** (§4), and — where a specification's own text must be clarified — an explicit, reviewed amendment. `docs/ADRs/ADR-005-Finding2-Bounded-Query-Similar-Retrieval.md` is a worked example: the ADR records the decision, and the corresponding postcondition text is added to both `Memory-Management-Specification-v1.0.md` §20.1 and `Learning-Engine-Specification-v1.1.md` §14.3 so the two specifications stay consistent with each other.

---

## 3. Source-of-Truth Hierarchy

Constitution §0.1.2 states the governance hierarchy:

```
Constitution
   └── Decision Matrix (§0.6)              — what may be decided autonomously
         └── Autonomous Roles (§0.2)        — who may decide it
               └── Quality Gates (§0.8)     — what must be true before it ships
                     └── Reality Validation (§0.16) — proof it is actually true
```

And the document hierarchy, from immutable to transient:

1. **Constitution / Core Specification** — `docs/EOS-Specification.md`. Changed only through the formal amendment process in §0.1.3 (an ADR tagged `constitutional`, CTO + Principal Engineer review, a passing Architecture Fitness run, and a Knowledge Graph entry under `constitution/amendments`).
2. **Subsystem specifications** — internally consistent with the Constitution and with each other.
3. **Implementation Roadmap** — decomposes the architecture into ordered increments. Never a design of its own.
4. **Implementation Plans** — per-increment, approved before implementation.
5. **Completion Reports** — per-increment, written after closure.

When implementation and documentation appear to disagree, the Constitution and subsystem specifications are authoritative, and the discrepancy is a signal to investigate.

---

## 4. ADR Usage

ADRs live in `docs/ADRs/`. They exist to record a decision that a frozen document did not already make, so that a future reader can see *why* the code looks the way it does.

The existing ADRs show the pattern in use:

- `ADR-001`/`ADR-015-001` — **Embedding Generation Reachability**, which produced the **Composition Root Adapter Pattern** now used ~40 times across `Program.cs`.
- `ADR-002`/`ADR-015-002` — `LessonLearned` producer ownership.
- `ADR-003`/`ADR-015-003` — automatic consolidation event wiring.
- `ADR-004`, `ADR-015-004` — undefined domain types; `MemoryRef` identity.
- `ADR-015-005` — `ContextAssembler` and candidate resolution.
- `ADR-005` — bounded `query_similar` retrieval, raised as a post-implementation finding.
- `ADR-006` — deterministic context-window truncation semantics.
- `ADR-007` — read-only patch-applicability verification before evidence acceptance.
- `ADR-008` — model-produced `[EDIT]` blocks and locally generated unified diffs.
- `ADR-009` — isolated Universal Gates 1–2 before `Running → Review`.

An ADR is *not* a place to smuggle in architecture. It records a decision **within** the space the frozen documents leave open, and cites the sections that leave it open.

A related, lighter-weight artifact is the **Architecture Gap** (`docs/Architecture-Gaps/`, `AG-0001` … `AG-0003`). A Gap records something the architecture does not currently make possible, so that implementation can honestly stop rather than invent. `AG-0003` is why `IReasoningEngineClient` has no `query_history()` member at all — not a stub, not a `NotImplementedException`, simply not declared.

---

## 5. Work Package Lifecycle

From `docs/Development-Workflow.md` §3:

```
Planning → Architecture Review → Implementation → Local Verification → Architecture Gate
  → Feature Branch Push → Pull Request → CodeRabbit Review → Fix VALID Findings Only
  → Re-Verification → Merge → Tag → Closure
```

**Exactly one Work Package is ever in progress.** A WP is not started until the previous one is formally closed.

Each WP delivers a **vertical slice** — a real, working, end-to-end path — not an isolated layer and not scaffolding for a future WP.

A Work Package's plan (`docs/WP-00N-Implementation-Plan.md`, using that document's own naming notation — e.g. `docs/WP-021-Implementation-Plan.md`) must contain, at minimum: revision and source of truth; the current repository baseline (inspected, not assumed); objective and exact roadmap scope; the vertical slice definition; scope split into **Included** and **Explicitly Excluded** with the owning future WP named for each exclusion; projects affected, files to create, files to modify, and **files that must not change**; dependency and package changes with per-item justification; configuration changes with schema and fail-closed behaviour; test strategy separating unit from integration and naming the real services required; acceptance criteria copied verbatim from the roadmap; definition of done; risks and future WP boundaries; and a **KISS/YAGNI justification** answering, for every abstraction, "why does this WP need this now?"

Closure (`§13`) requires a completion report in `docs/work-packages/` recording what was implemented, every file created and modified, every dependency added with justification, build/test/format results, the CodeRabbit outcome with every finding classified, the Architecture Gate outcome, the implementation/fix/merge commit SHAs and the tag object SHA, confirmation that local and remote `main` and the tag all match, and final repository status.

A WP is **CLOSED** only when every item on that list is simultaneously true. Partial completion is never closure.

---

## 6. Scope Control

The rules in `Development-Workflow.md` §6, restated:

- Never implement more than one Work Package at a time.
- Never anticipate or partially implement a future Work Package — even when the current WP's code would make it easier later.
- Never create an abstraction, interface, or extension point without a consumer **inside the current WP**.
- No interface for a class with exactly one implementation and no substitutability requirement.
- No configuration field the current WP does not read.
- No `TODO` implementations, no placeholder methods, no `NotImplementedException` standing in for real logic.
- Previously-closed Work Packages' files are modified only when the current plan explicitly requires it, with the reason recorded **at the point of change** — never for unrelated cleanup or style.

Anything discovered mid-implementation that belongs to a different WP is **deferred and explicitly recorded**, not absorbed.

This is why the codebase contains real components that nothing calls yet, each with an in-place comment explaining why. Wiring them up would have been someone else's Work Package.

---

## 7. Review → Plan → Approval → Implementation

**Architecture Review** (§5) happens on the *plan*, before code exists. Its scope is limited to the specification sections and roadmap row relevant to the current WP; unrelated specifications are not re-litigated. It checks specification compliance, roadmap compliance, architecture boundaries, dependency justification, the KISS/YAGNI gate, and vertical-slice validity.

Three approval states, and only three:

- **Approved** — implementation may begin exactly as planned.
- **Approved With Required Changes** — specific, enumerated changes must be made first; **no other changes are implied**.
- **Rejected** — the plan conflicts with the frozen specification or roadmap and must be redesigned before resubmission.

**Implementation does not start on an unapproved plan.**

---

## 8. Verification

The local verification checklist (`§8`) is run **in full** before any Architecture Gate or PR:

```bash
dotnet restore
dotnet build                        # zero errors, zero warnings
dotnet test                         # every existing test plus every new test
dotnet format --verify-no-changes
git diff --check
```

Plus:

- the architecture fitness test (`EOS.ArchitectureTests`, R-00) passing against the current graph;
- where the WP touches `EOS.Runner`/`BootstrapRunner`: `dotnet run --project src/EOS.Runner` reaching `Ready`;
- where work exercises engineering execution: evidence resolving in the Artifact Registry, patch applicability passing, and Universal Gates 1–2 producing their actual isolated build/test result before `Review`;
- where the WP requires real infrastructure: the relevant containers **verified** healthy and reachable before integration tests run — never assumed.

> **A verification step that cannot run because a real dependency is unavailable is reported as _not run_ — never silently skipped, and never reported as passing.**

`docs/guides/Testing-Guide.md` covers how to run each of these.

### Architecture Gate

After implementation and local verification both pass, and **before** the branch is pushed: a structured self-review of the **actual diff**, not the plan's description of it. It covers specification and roadmap compliance, vertical-slice integrity, dependency direction, infrastructure isolation, test quality, secrets handling, and every file touched outside the plan's declared boundary.

Output is a findings list, each classified Critical / High / Medium / Low / Informational, each with an explicit statement of whether it blocks the PR. Critical and High always block. A Medium blocks only if it is a scope, specification, or architecture violation. Low and Informational are recorded but **never** used to justify expanding scope.

---

## 9. Code Review

A Pull Request is required before any CodeRabbit review — CodeRabbit reviews PRs, not local branches.

**Real review only.** A review is never claimed to have happened unless its result is visible on the PR. If CodeRabbit cannot be reached or has not completed, that is reported plainly.

Every finding is classified as exactly one of:

| Classification | Meaning | Action |
|---|---|---|
| **VALID** | A real defect, correctly identified | Fixed before merge |
| **INVALID** | Factually wrong, or missing context the reviewer did not have | Rejected, with reasoning recorded as a PR reply |
| **OUT OF SCOPE** | Real, legitimate work — but a different Work Package's | Deferred, owning WP named if known |
| **OVER-ENGINEERING** | Recommends an abstraction the current WP has no consumer for | Rejected on KISS/YAGNI grounds, with reasoning recorded |

Every classification is documented **on the PR itself**, not only in the closure report, so future reviewers and contributors can see why a suggestion was or was not acted on.

Per `Reviewer-Operating-Rules.md` Rule 7: **CodeRabbit is advisory.** The Constitution, Roadmap, Specifications, Development Workflow, and Governance remain authoritative.

---

## 10. Merge Requirements

**Allowed:** a normal merge commit into `main`.

**Forbidden**, unless explicitly and separately approved for a specific, stated reason: force push, history rewrite, squash merge, rebase of shared history, reset of `main` after review has begun.

A merge commit preserves the exact history a reviewer already approved. The default is always the safest, most reversible option.

**Tagging.** Every closed WP gets an **annotated** tag (never lightweight) of the form `v0.X.0-wpXXX`, where `X` matches the WP number — created only after the PR is merged, referencing the resulting merge commit.

### Definition of Done (`§14`)

- [ ] Implementation matches the approved plan exactly
- [ ] All existing tests still pass; all new tests pass
- [ ] `dotnet build` — zero warnings, zero errors
- [ ] `dotnet format --verify-no-changes` passes
- [ ] Architecture Gate passed, with no unresolved Critical, High, or scope-violating Medium finding
- [ ] CodeRabbit review actually completed (not assumed, not skipped)
- [ ] Every finding classified, with VALID findings fixed
- [ ] Plan and completion report accurately reflect the final implementation
- [ ] PR merged normally into `main`
- [ ] Annotated tag created and pushed, referencing the merge commit
- [ ] Closure report written and committed
- [ ] Working tree clean; local and remote `main` and tag all match
- [ ] No scope beyond the approved Work Package was implemented

---

## 11. The Frozen Architecture Baseline (WP-019 onward)

`docs/governance/EOS-Engineering-Governance-v2.md`, ratified 2026-08-02 for WP-019 through WP-030, adds a **baseline freeze** on top of the workflow above.

The architecture implemented by WP-001 through WP-018 is the official baseline. It **shall not** be reopened merely because a reviewer wants another audit, another hostile review is requested, assumptions are questioned again, previous discussions are repeated, or already-reviewed documents are re-analyzed.

### Reopening Criteria (§4) — all five must hold

1. New evidence exists.
2. That evidence was unavailable during the WP-001–WP-018 audit process.
3. The evidence proves one of: Constitution violation, Specification violation, Roadmap violation, Development Workflow violation, Public API regression, Cross-WP regression, Build regression, Test regression, or an Architecture blocker.
4. The issue **cannot be solved additively**.
5. Solving it **requires** changing a frozen architecture artifact.

If any one condition is false, the architecture remains frozen. **The burden of proof belongs to the reviewer requesting reopening.**

### STEP-0

Every review **begins** by reading the governance document and evaluating the Reopening Criteria (`Review-Checklist.md`). If they are not satisfied, Architecture Review is skipped and **Delta Review** begins: only the active Work Package and the regressions *it* introduced are reviewed. Previously closed Work Packages are immutable.

A WP review **terminates** when build passes, tests pass, formatting passes, and no blocking findings remain. After that, Architecture Review and Hostile Review end for that WP; only normal Code Review and Delta Review continue.

---

## 12. Handling Architectural Contradictions

The rule is the same at every level: **do not silently resolve.**

1. **Stop.** Do not pick whichever reading is more convenient.
2. **Report the contradiction with evidence** — the exact documents and sections that conflict.
3. **Evaluate the Reopening Criteria** (§11). Most contradictions do not meet them.
4. **If it can be solved additively** — a new ADR, a new adapter, a narrower interface — do that, and record the decision.
5. **Only if all five criteria hold** does a frozen artifact change, and then through the Constitution's own amendment process (§0.1.3).

The repository shows both outcomes in practice. `AG-0001`, `AG-0002`, and `AG-0003` record contradictions that were **not** resolved — implementation stopped at the boundary and the gap was documented. `ADR-005` records one that **was** resolved additively, with the affected specification sections amended to match.

Per `Governance-v2` §7: Architecture Gaps remain governance items. They do not block implementation unless a Gap becomes part of an active WP's own binding acceptance criteria.

---

## 13. Handling Findings Discovered After Implementation

A finding raised after a WP has closed does **not** reopen that WP. `Governance-v2` §3 and `Reviewer-Operating-Rules.md` Rule 3 are explicit: previously closed Work Packages are immutable unless §4 is satisfied.

Keep three authorization shapes distinct: roadmap Work Packages follow the WP lifecycle above; a separately authorized post-roadmap capability follows the scope and gates recorded in its own approval; and a narrow remediation follows this section without automatically inventing a new WP. This distinction documents the authority already granted in each case—it is not a new lifecycle or permission to bypass review.

The repository's own worked example is **WP-031**, the post-roadmap remediation that followed WP-030's closure. Its shape is the template:

1. The finding was raised with concrete evidence (unbounded `query_similar` candidate retrieval).
2. It was evaluated against the Reopening Criteria and found to be **additively solvable**.
3. An ADR (`ADR-005`) recorded the decision and cited the sections that left it open.
4. The two affected specifications were amended with the new postcondition, keeping them consistent with each other.
5. A bounded implementation landed behind a new `Thresholds.json` value (`querySimilarMaxCandidates`), with tests.
6. Follow-on findings from that same remediation (F-2 determinism, F-3 test isolation) were fixed as separate, individually-scoped commits.

Note what did **not** happen: no new Work Package was invented to hold it, and no frozen architecture artifact was rewritten beyond the minimum the ADR justified.

**Engineering Debt** (`Governance-v2` §6) is frozen until its recorded trigger. Debt items are not re-discussed before their recorded trigger is reached by an active Work Package.

### Findings that are raised but not yet accepted

Not every post-implementation finding results in a change. `docs/Governance-Change-Proposal-001.md` records two findings that reached "Proven" confidence in an adversarial governance audit, with full evidence and constitutional justification — and its own status line reads **"Proposed — not applied. No approved document has been modified by this proposal."** It explicitly does not modify the roadmap, the Constitution, or any specification, does not create or move a Work Package, and does not propose an implementation approach. It is submitted to whoever holds authority over the frozen architecture, to accept, modify, or reject.

That is the correct terminal state for a finding whose resolution would require changing a frozen artifact and which has not yet been ruled on. Writing it down with evidence and stopping is the outcome — not implementing it anyway, and not quietly dropping it.

---

## 14. Introducing New Technology or Patterns

The default answer is **no**, and the burden is on the proposal.

From `Development-Workflow.md` §2 and §6, and the Infrastructure Roadmap's own repeated guidance:

- Every new package or project reference must be justified **against a current need** in the active WP's plan.
- No abstraction, interface, factory, DI registration, retry/resilience pipeline, generic framework, or configuration layer without a concrete, named consumer *in the current WP*.
- Do not introduce a technology "for flexibility" before a concrete requirement exists.

The Infrastructure Roadmap names specific rejections with reasons — Bun ("no frozen specification names Bun as a requirement"), `pyenv`/`conda`, a dedicated secrets-manager service, and a full OpenObserve deployment before there was anything worth observing.

The result is visible in the dependency list: five distinct production dependency packages, no ORM, no mediator library, no resilience library. Test projects additionally carry their standard test SDK and xUnit tooling; the detailed package inventory belongs in `docs/guides/Contributing-Guide.md`.

If a new technology is genuinely required, it goes in the WP's Implementation Plan under **Dependency Changes** with its justification, and is approved at Architecture Review — before any code uses it.

---

## 15. Changing Specifications

| Artifact | How it changes |
|---|---|
| **Constitution** (`EOS-Specification.md`) | Only via §0.1.3: an ADR tagged `constitutional`, review by the CTO role and at least one Principal Engineer role, a passing Architecture Fitness run against the proposed change, and a recorded Knowledge Graph entry under `constitution/amendments`. Never implicitly. |
| **Subsystem specification** | Only through an approved, reviewed amendment that keeps it internally consistent with the Constitution **and with every other specification**. `ADR-005` amending both Memory Management §20.1 and Learning Engine §14.3 in one pass is the model. |
| **Roadmap** | Frozen. A WP's plan narrows ambiguity within its own row; it does not expand scope. |
| **Development Workflow** | `§15`: requires explicit architectural approval, and must not be introduced implicitly as part of an unrelated Work Package. A proposal documents why the existing workflow is insufficient, which future WPs are affected, and whether the change is backward compatible. |
| **Governance** | A written proposal, then an explicit ratification that converts it from a project artifact into active process — `docs/governance/Governance-Ratification.md` is the worked example, ratifying `EOS-Engineering-Governance-v2.md` in full, as written, without modification. |

The overriding rule from `Development-Workflow.md` §16: if a step seems to be blocking legitimate progress, the correct response is to **raise the concern and get the document changed deliberately** — never to bypass it quietly, and never to justify a deviation retroactively.
