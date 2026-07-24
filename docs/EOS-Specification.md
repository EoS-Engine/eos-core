# Engineering Operating System (EOS)
## A Complete Engineering Intelligence Platform Specification

**Version:** 1.0.0
**Document Type:** Founding Architecture & Governance Specification
**Scope:** Backend · Web · Mobile (Flutter) · AI/ML · DevOps · Platform Engineering · Technical Leadership

---

## How to Read This Document

This specification defines the EOS as a single, internally consistent system. Every subsystem below cross-references every other subsystem it depends on. Where a later Part (Physical Repository Architecture, Event Catalog, etc.) adds a capability, the sections here already assume its existence — there is no bolt-on layer. If you are implementing this system, read Part 0 (this document's core sections) before Parts 1–15, since Parts 1–15 are the *implementation* of the architecture defined in Part 0.

---

# PART 0 — CORE SPECIFICATION

## 0.1 Constitution

The Constitution is the immutable rule-set that every autonomous role, module, and pipeline in the EOS must obey. It is enforced programmatically (via `EOS.Gates`) and is not advisory.

### 0.1.1 First Principles

1. **Evidence over assertion.** No task, ADR, or release is marked complete without machine-checkable evidence (tests, benchmarks, coverage, security scan, static analysis).
2. **Autonomy with accountability.** Autonomous roles may act without human approval only within their granted authority level (see §0.2.3). Every autonomous action is logged, attributable, and reversible.
3. **Knowledge is a first-class asset.** Every lesson, decision, and failure is captured, versioned, and made queryable through the Knowledge Graph — never lost in chat history or ephemeral logs.
4. **Consistency over speed.** A change that violates architecture fitness rules or quality gates is blocked regardless of deadline pressure. Speed comes from automation, not from skipped verification.
5. **Single source of truth per concern.** No data, configuration, or capability may be duplicated across subsystems (see Part 4 — Data Architecture, and Part 10 — Configuration Strategy).
6. **Reality over simulation.** Capability claims must be validated against real execution (see §0.16 Reality Validation), not just declared.
7. **Continuous compounding.** Every lesson must be capable of eventually becoming automation (see Part 14 — Meta Learning). Knowledge that never compounds into capability is considered a system defect.
8. **Domain equality.** Backend, Web, Mobile, AI/ML, DevOps, and Platform Engineering are peer domains. None is modeled as a subordinate of another (this is why Part 15 makes Flutter a first-class domain, not a "frontend variant").

### 0.1.2 Governance Hierarchy

```
Constitution
   └── Decision Matrix (§0.6)              — what may be decided autonomously
         └── Autonomous Roles (§0.2)        — who may decide it
               └── Quality Gates (§0.8)     — what must be true before it ships
                     └── Reality Validation (§0.16) — proof it is actually true
```

### 0.1.3 Amendment Policy

Constitutional amendments require:
- An ADR (Architecture Decision Record) tagged `constitutional`
- Review by the CTO role and at least one Principal Engineer role
- A passing Architecture Fitness run (Part 2) against the proposed change
- A recorded entry in the Knowledge Graph under `constitution/amendments`

---

## 0.2 Autonomous Roles

Autonomous Roles are independent reasoning/execution units, each backed by an `EOS.<Role>` project (see Part 1). Each role has a **mandate**, an **authority level**, a **competency dependency set** (Competency Graph, §0.3), and a **communication contract** (Part 5).

### 0.2.1 Role Roster

| Role | Mandate | Reports To | Authority Level |
|---|---|---|---|
| CTO | Strategic technical direction, architecture approval, risk acceptance | Human stakeholders | L4 (full) |
| Principal Engineer | Cross-cutting architecture, fitness rules, complex ADRs | CTO | L3 |
| Tech Lead | Team-level execution, task decomposition, code review policy | Principal Engineer | L2 |
| Senior Engineer | Implementation, mentoring, task execution | Tech Lead | L1 |
| QA | Test strategy, quality gate enforcement, defect triage | Tech Lead | L2 (blocking authority) |
| DevOps | Pipeline, infrastructure, deployment, incident response | Principal Engineer | L2 (blocking authority) |
| Product Owner | Backlog priority, acceptance criteria, scope | CTO | L2 (scope authority) |
| Business Analyst | Requirements traceability, stakeholder translation | Product Owner | L1 |
| AI Architect | Model/provider selection, inference architecture, prompt governance | Principal Engineer | L2 |
| Mobile Architect *(new, Part 15)* | Flutter architecture, mobile competency ownership | Principal Engineer | L2 |
| Planner | Capability planning, task generation, scheduling input | CTO (policy), Scheduler (runtime) | L2 (generative, non-blocking) |

### 0.2.2 Cross-Role Rules

- **CTO** may inspect (read-only) all modules and all role decisions. CTO approval is required to override a QA or DevOps block.
- **Senior Engineer** may never reference or invoke `EOS.CTO` directly (see Part 2, dependency rules) — escalation flows through Tech Lead.
- **Planner** communicates with every other role exclusively through `EOS.Contracts` (never direct references) — this keeps the Planner swappable and testable in isolation.
- **QA** and **DevOps** hold *blocking authority*: their gate failures halt promotion regardless of any other role's approval, and only CTO can override, with a mandatory recorded justification ADR.
- **Mobile Architect** has equal standing to AI Architect and any other domain architect — mobile decisions do not route through a "Web Architect."

### 0.2.3 Authority Levels

| Level | Can Do | Cannot Do |
|---|---|---|
| L1 | Implement, propose ADRs, run local gates | Approve ADRs, override gates, change config |
| L2 | Approve domain ADRs, block promotion, edit domain config | Amend Constitution, override another L2's block |
| L3 | Approve cross-cutting ADRs, adjust fitness rules | Amend Constitution unilaterally |
| L4 | Full authority including Constitutional amendment (with process in §0.1.3) | Bypass evidence requirements (§0.1.1) |

### 0.2.4 Role Lifecycle

Each role instance emits `CompetencyProven` and `CapabilityUnlocked` events (Part 3) as it demonstrates new competencies, which feed directly into the Competency Graph and Capability Planner below.
## 0.3 Competency Graph

The Competency Graph is a directed acyclic graph (DAG) of skills. Nodes are competencies; edges are prerequisites. Roles attach to nodes they've "proven" (via `CompetencyProven` events). Domains are peer sub-graphs joined at a common root — **no domain is nested inside another** (Constitution §0.1.1.8).

### 0.3.1 Domain Root Structure

```
Competency Root
 ├── Backend Domain
 ├── Web Domain
 ├── Mobile Domain (Flutter) ── first-class, see Part 15 for full expansion
 ├── AI/ML Domain
 ├── DevOps Domain
 ├── Platform Engineering Domain
 └── Technical Leadership Domain
```

### 0.3.2 Node Schema

Every competency node carries:
- `id`, `domain`, `prerequisites[]`
- `proof_requirements` (what evidence proves this competency — links to Quality Gates)
- `unlocks[]` (downstream capabilities this competency enables — links to Capability Planner)
- `decay_policy` (competencies can go stale; re-validation cadence)

### 0.3.3 Mobile Domain Attachment Point

The Mobile (Flutter) domain root attaches at the same level as Backend/Web/AI, with its own full competency tree defined in **Part 15**. This document does not duplicate that tree here — it is integrated by reference so the Competency Graph stays a single source of truth (Constitution §0.1.1.5).

---

## 0.4 Capability Planner

The Capability Planner (`EOS.Planner`) converts unlocked competencies + backlog + constraints into an executable plan. It never executes work itself — it emits `PlannerGenerated` events consumed by the Scheduler (Part 7).

### 0.4.1 Planning Inputs

| Input | Source |
|---|---|
| Available competencies | Competency Graph (§0.3) |
| Backlog items | Product Owner role |
| Resource budgets | Scheduler (Part 7) |
| Historical velocity | Engineering Economics (§0.17) |
| Risk tolerance | Decision Matrix (§0.6) |
| Domain-specific constraints | Domain architects (incl. Mobile Architect) |

### 0.4.2 Planning Outputs

A `Plan` artifact (versioned in Artifact Registry, Part 8) containing:
- Ordered task graph with dependencies
- Assigned competency requirements per task
- Estimated resource cost (CPU/RAM/inference budget — Part 7)
- Risk-adjusted confidence score

### 0.4.3 Planning Cycle

Planning runs on every Execution Cycle boundary (§0.12) and additionally whenever a `CapabilityUnlocked` or `ArchitectureDriftDetected` event fires out-of-band.

---

## 0.5 Knowledge Graph

The Knowledge Graph (`EOS.KnowledgeGraph`, backed by `EOS.VectorStore`) is the single persistent store of everything the EOS has learned. It is the terminal consumer of `LessonLearned`, `LessonPromoted`, `ADRCreated/Approved/Rejected`, `IncidentResolved`, and `BenchmarkCompleted` events.

### 0.5.1 Node Types

- **Fact** — a verified, stable piece of engineering knowledge
- **Lesson** — an observation from a specific incident/task
- **Pattern** — a lesson generalized across ≥3 occurrences (see Meta Learning, Part 14)
- **Decision** — an ADR and its outcome
- **Risk** — a known failure mode and its mitigation

### 0.5.2 Query Interface

All roles query the Knowledge Graph through `EOS.Knowledge` (never directly through `EOS.VectorStore` — see Part 2 dependency rules). Queries are hybrid: symbolic graph traversal + vector similarity re-ranking.

### 0.5.3 Consistency Guarantee

The Knowledge Graph is the **only** place lessons, patterns, and decisions are stored (Constitution §0.1.1.5). Daily Reports (§0.9) and Dashboards (§0.13) read from it; they never maintain their own copies.

---

## 0.6 Decision Matrix

The Decision Matrix determines, for any given decision type, **which role may decide autonomously**, **which require multi-role consensus**, and **which require human sign-off.**

| Decision Type | Autonomous Decider | Consensus Required | Human Required |
|---|---|---|---|
| Code-level implementation choice | Senior Engineer | — | — |
| Domain architecture (incl. Flutter) | Domain Architect | Principal Engineer | — |
| Cross-cutting architecture | Principal Engineer | CTO | — |
| Constitutional amendment | — | CTO + Principal Engineer | Yes |
| Provider/model swap | AI Architect | DevOps | — |
| Production release | DevOps + QA | — | CTO sign-off if risk score > threshold |
| Security-sensitive change | — | DevOps + Principal Engineer | Yes |
| Disaster recovery invocation | DevOps | — | Yes, if data-loss risk |

### 0.6.1 Risk Scoring

Every decision is risk-scored (0–100) using: blast radius, reversibility, data sensitivity, and historical incident correlation (pulled from Knowledge Graph, §0.5). Score > 70 always escalates one tier regardless of table default.

---

## 0.7 NFR Framework (Non-Functional Requirements)

Every capability, regardless of domain, must declare and be validated against NFRs in these categories:

| Category | Backend/Web Example | Mobile (Flutter) Example |
|---|---|---|
| Performance | P99 latency < 200ms | Cold start < 2s, 60fps scroll |
| Reliability | 99.95% uptime | Crash-free sessions > 99.5% |
| Security | OWASP ASVS L2 | Certificate pinning, secure storage |
| Accessibility | WCAG 2.2 AA | Platform accessibility APIs, screen reader parity |
| Scalability | Horizontal scale to N nodes | Offline-first sync at scale |
| Maintainability | Cyclomatic complexity ceiling | Widget test coverage, modularization score |
| Observability | Distributed tracing coverage | Crash reporting + analytics coverage |
| Localization | i18n coverage | RTL + locale coverage |

NFRs are enforced by Quality Gates (§0.8) and are domain-aware: the same NFR *category* applies everywhere, but the *threshold and proof mechanism* is domain-specific — this is how Mobile becomes a peer domain rather than inheriting Web's thresholds unmodified.
## 0.8 Quality Gates

Quality Gates (`EOS.Gates`) are the enforcement mechanism for the Constitution and NFR Framework. A gate is a pass/fail (or scored) check that must clear before a task/artifact advances a Task Lifecycle stage (Part 6).

### 0.8.1 Universal Gates (apply to every domain)

1. Static analysis / lint clean
2. Unit test pass + coverage threshold
3. Security scan (SAST/dependency audit) clean
4. Architecture Fitness Rules pass (Part 2)
5. Documentation-of-change present
6. NFR thresholds met (§0.7, domain-specific values)

### 0.8.2 Domain-Specific Gate Packs

- **Backend/Web Gate Pack** — integration tests, contract tests, load test thresholds
- **Mobile Gate Pack (new, Part 15)** — Flutter Analyze, Formatting, Widget Tests, Golden Tests, Integration Tests, Performance, Accessibility, Localization, Security, Package Audit, APK/AAB Validation, IPA Validation, Store Readiness, Crash-free Startup (full detail in Part 15 §15.3)
- **AI/ML Gate Pack** — prompt regression tests (Prompt Registry, Part 9), eval benchmark thresholds, hallucination-rate ceiling
- **DevOps Gate Pack** — pipeline reproducibility, rollback rehearsal, infra-as-code drift check

### 0.8.3 Gate Failure Handling

A failed gate emits a blocking status on the task (Task Lifecycle, Part 6) and a `LessonLearned` event if the failure is novel (first occurrence) — feeding directly into Meta Learning (Part 14).

---

## 0.9 Daily Reports

Generated automatically at the end of each Execution Cycle day boundary (§0.12), sourced **only** from live subsystem state — never hand-maintained.

### 0.9.1 Report Contents

- Tasks: created / completed / blocked / retried (from Task Lifecycle + Event Catalog, Part 3/6)
- Gate pass/fail summary per domain, including Mobile Gate Pack results
- New competencies proven / capabilities unlocked (Competency Graph, §0.3)
- New lessons learned / patterns promoted (Knowledge Graph, §0.5)
- Budget consumption vs. Scheduler budgets (Part 7)
- Open risks and their scores (Decision Matrix, §0.6.1)
- ADRs created/approved/rejected

### 0.9.2 Distribution

Reports are persisted as versioned artifacts (Artifact Registry, Part 8) and surfaced on the Dashboard (§0.13). No separate copy is emailed/stored outside these two channels (avoids data duplication, Part 4).

---

## 0.10 Architecture Evolution

The mechanism by which the EOS's own architecture changes over time without violating Constitution §0.1.1.5 (single source of truth) or introducing drift.

### 0.10.1 Drift Detection

`EOS.Gates` continuously diffs the live dependency graph (extracted from `EOS.Orchestrator` + build metadata) against the Module Dependency Rules (Part 2). Any divergence emits `ArchitectureDriftDetected`.

### 0.10.2 Evolution Workflow

```
ArchitectureDriftDetected  OR  Proposed ADR (constitutional/architectural)
        │
        ▼
Principal Engineer reviews against Architecture Fitness Rules (Part 2)
        │
        ▼
ADR created → ADRCreated event → Knowledge Graph
        │
        ▼
If constitutional scope → CTO + Principal Engineer consensus (Decision Matrix §0.6)
        │
        ▼
ADRApproved/Rejected → Physical Repository Architecture updated (Part 1)
        │
        ▼
Fitness Rules regenerated → re-validated against live graph
```

### 0.10.3 Versioning

The EOS architecture itself is versioned (semantic versioning at the specification level; this document is v1.0.0). Every Part 1–15 addition that changes dependency shape bumps at minimum a minor version and requires an ADR.

---

## 0.11 Dashboards

(Full KPI/metric wiring cross-referenced in §0.13 and Part 8; this subsection defines dashboard *architecture*, not content.)

Dashboards (`EOS.Dashboard`, presented via `EOS.Web`) are **read-only projections** — per Part 2's dependency rule "Dashboard never references implementation," dashboards read exclusively from:
- Artifact Registry (Part 8) for reports/benchmarks/evidence
- Knowledge Graph (§0.5) for lessons/patterns/decisions
- Event stream (Part 3) for live state

Dashboards never call into `EOS.Application`/`EOS.Domain` directly.

---

## 0.12 Execution Cycles

The rhythm at which planning, execution, and reporting happen.

### 0.12.1 Cycle Structure

| Cycle | Cadence | Activities |
|---|---|---|
| Micro-cycle | Continuous (event-driven) | Task state transitions, gate checks |
| Daily cycle | 24h | Daily Report generation, Scheduler re-balancing, drift scan |
| Sprint cycle | 1–2 weeks | Planner replan, Competency Graph review, KPI rollup |
| Quarterly cycle | Quarterly | Engineering Economics review, Disaster Simulation (Part 13), Constitution review |

### 0.12.2 Cycle Boundaries and Gates

Each cycle boundary triggers a mandatory checkpoint: no cycle closes with unresolved blocking-severity gate failures (§0.8) or unacknowledged `IncidentDetected` events (Part 3) without an explicit, ADR-logged risk acceptance (Decision Matrix §0.6).
## 0.13 KPIs (Key Performance Indicators)

KPIs are computed, not entered. Every KPI has a declared source query against the Event Catalog (Part 3), Artifact Registry (Part 8), or Knowledge Graph (§0.5).

### 0.13.1 Engineering KPIs

| KPI | Formula Source | Domain Scope |
|---|---|---|
| Cycle time | `TaskCreated` → `TaskCompleted` delta | All |
| Escaped defect rate | Post-release `IncidentDetected` / releases | All |
| Gate pass rate | Passed gates / total gate runs | All, broken out by domain incl. Mobile |
| Knowledge reuse rate | Tasks referencing existing Knowledge Graph nodes / total tasks | All |
| Automation ratio | Automated Golden Paths / total repeated patterns (Part 14) | All |
| Crash-free session rate | Mobile crash reporting feed | Mobile (new) |
| Store readiness score | Mobile Gate Pack composite (Part 15 §15.3) | Mobile (new) |
| Provider cost efficiency | Inference spend / successful task completions | AI/ML |
| Competency velocity | New `CompetencyProven` events / cycle | All |

### 0.13.2 KPI Governance

KPI *definitions* are versioned artifacts (Part 8). Changing a KPI formula requires an ADR (same path as Architecture Evolution, §0.10) so historical trend lines are never silently redefined.

---

## 0.14 Provider Architecture

Defines how the EOS abstracts and manages LLM/inference providers (and, by extension, other pluggable external capability providers).

### 0.14.1 Abstraction Layer

```
EOS.AIArchitect  (policy: which provider, which model, fallback order)
        │
        ▼
EOS.SDK  Provider Contract  (shared interface — Part 11)
        │
   ┌────┼─────────────┬───────────────┐
   ▼    ▼             ▼               ▼
Provider A        Provider B      Local LLM      Flutter-embedded
(cloud)           (cloud)         (on-prem)      inference (mobile, Part 15)
```

### 0.14.2 Provider Selection Policy

Selection is driven by: task competency requirements (§0.3), cost budget (Part 7), latency NFR (§0.7), and data-sensitivity classification (Data Architecture, Part 4). `ProviderChanged` events (Part 3) log every switch with justification.

### 0.14.3 Mobile Provider Integration

Flutter clients integrate with providers either via `EOS.Contracts`-defined REST/gRPC calls to backend-hosted inference, or via on-device local LLM APIs (Part 15) for offline-first scenarios — never by embedding provider API keys directly in the mobile app (Mobile Security, Part 15).

---

## 0.15 Reality Validation

The mechanism that prevents the EOS (or its autonomous roles) from marking work "done" based on self-reported or simulated success.

### 0.15.1 Validation Principles

- A `CompetencyProven` event must be backed by a real, reproducible gate pass — not a role's self-assessment.
- A `TaskCompleted` event must reference passing evidence in the Artifact Registry (Part 8), not just a status flag.
- Benchmarks (Part 8) must run against real inputs/environments, not mocked stand-ins, at least once per Sprint cycle (§0.12.1).
- Mobile reality validation additionally requires real-device (or real-device-farm) test runs, not emulator-only evidence, before `Store Readiness` KPI (§0.13.1) can reach 100%.

### 0.15.2 Validation Pipeline

```
Claimed Completion → Evidence Check (Artifact Registry) → Independent Re-run (sampled) → Reality Score
```

A Reality Score below threshold reopens the task and emits `TaskRetried` (Part 3).

---

## 0.16 Engineering Economics

Treats engineering effort, inference spend, and infrastructure cost as a unified budget managed jointly by the Scheduler (Part 7) and Capability Planner (§0.4).

### 0.16.1 Cost Categories

- **Compute** — CPU/RAM (Scheduler budgets, Part 7)
- **Inference** — token/model spend (Provider Architecture, §0.14)
- **Human review time** — L2+ role time spent on approvals (Decision Matrix, §0.6)
- **Technical debt interest** — modeled as a decaying multiplier on cycle time for modules with open `ArchitectureDriftDetected` events

### 0.16.2 ROI Modeling

Every automated Golden Path (Meta Learning, Part 14) is evaluated for ROI: (manual cost saved per invocation) × (projected invocation frequency) − (build + maintenance cost). Golden Paths below ROI threshold are deprecated rather than kept as maintenance burden.

### 0.16.3 Mobile Economics

Mobile adds device-fragmentation cost (test matrix width) and store-review latency as first-class cost factors distinct from Backend/Web release economics — reflected in Mobile KPIs (§0.13.1) and Mobile Quality Gates (Part 15 §15.3).

---

*End of Part 0 — Core Specification. Parts 1–15 below define the concrete implementation of every subsystem referenced above.*
# PART 1 — Physical Repository Architecture

## 1.1 Solution Structure

```
EOS.sln
├── src/
│   ├── EOS.Core/                  Cross-cutting kernel types (no business logic)
│   ├── EOS.SharedKernel/          Value objects, base entities, common primitives
│   ├── EOS.Contracts/             All inter-module contracts (interfaces, DTOs, events)
│   ├── EOS.Domain/                Domain model: tasks, competencies, plans, ADRs
│   ├── EOS.Application/           Use cases / application services orchestrating Domain
│   ├── EOS.Infrastructure/        Persistence, messaging, external integrations
│   ├── EOS.Orchestrator/          Coordinates roles, cycles, and event routing
│   ├── EOS.Planner/               Capability Planner (§0.4) + Planning & Execution Engine (Planning-Execution-Engine-Specification-v1.0)
│   ├── EOS.Learning/               Learning Engine: Meta Learning pipeline, ROI Gate, Quarantine, Fitness Functions (Learning-Engine-Specification-v1.1)
│   ├── EOS.Reasoning/              Reasoning Engine: 12-stage decision/explanation pipeline (Reasoning-Engine-Specification-v1.0)
│   ├── EOS.AIProvider/             AI Provider Layer: inference/embedding abstraction, provider routing (AI-Provider-Layer-Specification-v1.0)
│   ├── EOS.Resources/              Resource Management: capacity measurement, quotas, model residency (Resource-Management-Specification-v1.0)
│   ├── EOS.CTO/                   CTO autonomous role
│   ├── EOS.PrincipalEngineer/     Principal Engineer autonomous role
│   ├── EOS.TechLead/              Tech Lead autonomous role
│   ├── EOS.SeniorEngineer/        Senior Engineer autonomous role
│   ├── EOS.QA/                    QA autonomous role + gate enforcement hooks
│   ├── EOS.DevOps/                DevOps autonomous role + pipeline hooks
│   ├── EOS.ProductOwner/          Product Owner autonomous role
│   ├── EOS.BusinessAnalyst/       Business Analyst autonomous role
│   ├── EOS.AIArchitect/           AI Architect autonomous role + provider policy
│   ├── EOS.MobileArchitect/       Mobile Architect autonomous role (Part 15)
│   ├── EOS.Knowledge/             Knowledge Graph query/query-planning API
│   ├── EOS.KnowledgeGraph/        Graph storage + traversal engine
│   ├── EOS.VectorStore/           Embedding storage + similarity search (ChromaDB-backed)
│   ├── EOS.Gates/                 Quality Gates engine (§0.8) + Fitness Rules (Part 2) + Protection Layer (Protection-Layer-Specification-v1.0)
│   ├── EOS.Pipeline/              CI/CD pipeline definitions and execution — Deferred (Post-v1); registered project skeleton only, no specification (see §1.4)
│   ├── EOS.SDK/                   Public reusable SDK (Part 11)
│   ├── EOS.Dashboard/             Dashboard aggregation/query layer (read-only, §0.11)
│   ├── EOS.Web/                   Web front-end (Blazor/React host — presentation only)
│   ├── EOS.Mobile/                Flutter mobile app (Part 15) — separate toolchain, bridged via EOS.Contracts
│   ├── EOS.Tools/                 Internal developer tooling, codegen, scaffolding
│   └── EOS.Runner/                Composition root / host process (Generic Host)
├── tests/                         Mirrors src/ 1:1, plus tests/EOS.ArchitectureTests
├── benchmarks/                    BenchmarkDotNet + Flutter integration_test perf suites
├── docs/                          Generated + hand-authored docs, ADRs, this specification
├── config/                        The ten Part 10 configuration files (§10.3 ownership/storage)
├── scripts/                       Bootstrap, restore-drill, migration scripts (Part 12/13)
├── deploy/                        IaC (Bicep/Terraform), Helm charts, store deployment configs
└── prompts/                       Prompt Registry source-of-truth (Part 9), versioned per role
```

## 1.2 Project Ownership

| Project | Owning Role(s) | Depends On | Never Depends On |
|---|---|---|---|
| EOS.Core | Principal Engineer | (nothing — leaf) | Everything else |
| EOS.SharedKernel | Principal Engineer | EOS.Core | EOS.Domain, EOS.Infrastructure |
| EOS.Contracts | Principal Engineer | EOS.SharedKernel | EOS.Application, EOS.Infrastructure |
| EOS.Domain | Tech Lead / Senior Engineer | EOS.SharedKernel, EOS.Contracts | EOS.Application, EOS.Infrastructure |
| EOS.Application | Tech Lead | EOS.Domain, EOS.Contracts | EOS.Infrastructure, EOS.Web, EOS.Mobile |
| EOS.Infrastructure | DevOps | EOS.Application, EOS.Contracts | EOS.CTO..EOS.MobileArchitect (role projects) |
| EOS.Orchestrator | Principal Engineer | EOS.Contracts, EOS.Application | Role internals directly (role projects only via contracts) |
| EOS.Planner | Product Owner (policy), Principal Engineer (impl) | EOS.Contracts, EOS.Knowledge | Role projects directly |
| EOS.Learning | Principal Engineer | EOS.Contracts, EOS.Knowledge, EOS.SDK | Role projects (no role project depends on it directly) |
| EOS.Reasoning | Principal Engineer | EOS.Contracts, EOS.SDK | Everything except EOS.AIProvider (sole `IAIProviderClient` consumer) |
| EOS.AIProvider | AI Architect (policy), Principal Engineer (impl) | EOS.Contracts, EOS.SDK | A third consumer channel beyond EOS.Reasoning (`infer`) and EOS.Knowledge (`embed`) |
| EOS.Resources | Principal Engineer | EOS.Contracts, EOS.SDK | Dispatch, gating, or selection logic (measurement/publication only) |
| EOS.CTO | CTO | EOS.Contracts, EOS.Knowledge | Nothing may depend on it except Orchestrator (routing) |
| EOS.PrincipalEngineer | Principal Engineer | EOS.Contracts, EOS.Gates, EOS.Knowledge | — |
| EOS.TechLead | Tech Lead | EOS.Contracts, EOS.Planner (read) | EOS.CTO |
| EOS.SeniorEngineer | Senior Engineer | EOS.Contracts | EOS.CTO, EOS.PrincipalEngineer, EOS.TechLead |
| EOS.QA | QA | EOS.Contracts, EOS.Gates | — |
| EOS.DevOps | DevOps | EOS.Contracts, EOS.Pipeline, EOS.Infrastructure | — |
| EOS.ProductOwner | Product Owner | EOS.Contracts, EOS.Knowledge | — |
| EOS.BusinessAnalyst | Business Analyst | EOS.Contracts | — |
| EOS.AIArchitect | AI Architect | EOS.Contracts, EOS.SDK (provider layer) | — |
| EOS.MobileArchitect | Mobile Architect | EOS.Contracts, EOS.SDK | Web-specific projects |
| EOS.Knowledge | Principal Engineer | EOS.KnowledgeGraph, EOS.VectorStore | Role projects |
| EOS.KnowledgeGraph | Principal Engineer | EOS.Infrastructure (storage) | EOS.Dashboard, EOS.Web directly |
| EOS.VectorStore | Principal Engineer | EOS.Infrastructure | — |
| EOS.Gates | QA / Principal Engineer | EOS.Contracts, EOS.Domain (read) | EOS.Web, EOS.Mobile |
| EOS.Pipeline | DevOps | EOS.Gates, EOS.Contracts | — *(Deferred Post-v1 — see §1.4; dependencies reflect registered intent, not an implemented project)* |
| EOS.SDK | Principal Engineer | EOS.Core, EOS.SharedKernel, EOS.Contracts | EOS.Domain, EOS.Infrastructure |
| EOS.Dashboard | Tech Lead | EOS.Contracts (read-only projections) | EOS.Application, EOS.Domain, EOS.Infrastructure |
| EOS.Web | Senior Engineer (Web) | EOS.Dashboard, EOS.Contracts | EOS.Infrastructure directly |
| EOS.Mobile | Mobile Architect / Senior Engineer (Mobile) | EOS.Contracts (via REST/gRPC bridge) | Any .NET project directly (cross-runtime boundary) |
| EOS.Tools | Principal Engineer | EOS.Core | Runtime projects |
| EOS.Runner | DevOps | Everything (composition root) | — |

## 1.3 Rationale

- **EOS.Contracts is the only cross-role dependency surface.** This directly implements Constitution §0.1.1.5 and the Decision Matrix rule that Planner communicates through contracts only (§0.4, Part 2).
- **EOS.Mobile is intentionally isolated** behind a runtime boundary (Dart/Flutter vs. .NET) and communicates only through `EOS.Contracts`-defined REST/gRPC/MCP surfaces exposed by `EOS.Application` — never linked in-process. This is what makes Mobile a first-class *peer* domain rather than an in-process frontend module (Part 15).
- **EOS.Runner is the only project allowed to reference everything** — it is pure composition (DI wiring), not logic.

## 1.4 EOS.Pipeline Status

`EOS.Pipeline` (CI/CD) remains registered above as a Part 1 project skeleton and event participant (`PipelineCompleted`, `BenchmarkCompleted`, `MobileBuildCompleted` — Part 3), but it is **Deferred (Post-v1)**: no specification exists for it beyond this one-line registration, and `EOS-Implementation-Roadmap-v1.0.md` explicitly excludes its implementation from all 30 Work Packages, consistent with `Architecture-Validation-Report-v1.0.md` §17.2 Blocker #4's recommendation that it receive its own lightweight specification before implementation. This status marking is administrative only — it registers a decision already made in the Implementation Roadmap, not a new one.

# PART 2 — Module Dependency Rules

## 2.1 Core Rules

1. **CTO may inspect all modules.** `EOS.CTO` holds a read-only reflection/reporting dependency on every project for audit purposes, enforced as *read-only* by fitness rule R-01 (no write-capable interface may be referenced).
2. **Senior Engineer may never reference CTO.** Enforced by fitness rule R-02 — a compile-time forbidden-reference test.
3. **Planner communicates through contracts only.** `EOS.Planner` may only depend on `EOS.Contracts` and `EOS.Knowledge` — never on concrete role projects. Fitness rule R-03.
4. **Dashboard never references implementation.** `EOS.Dashboard` may depend only on `EOS.Contracts` read-projections. Fitness rule R-04.
5. **Infrastructure never references Web.** `EOS.Infrastructure` has zero dependency edge toward `EOS.Web` or `EOS.Mobile`. Fitness rule R-05.
6. **No project may create a dependency cycle.** Enforced globally via topological-sort validation (R-00, runs before all other rules).
7. **Role projects never reference each other directly.** All role-to-role communication is via `EOS.Orchestrator` + `EOS.Contracts` events (Part 5). Fitness rule R-06.
8. **EOS.Mobile never references any .NET project.** Cross-runtime isolation; communication is exclusively via the network contracts described in Part 5/Part 1 §1.3. Fitness rule R-07.

## 2.2 Layered Dependency Direction

```
EOS.Core
   ▲
EOS.SharedKernel
   ▲
EOS.Contracts  ◄────────────────────────────┐
   ▲                                        │ (only this project may be
EOS.Domain                                  │  referenced across role/
   ▲                                        │  Planner/Dashboard boundaries)
EOS.Application                             │
   ▲                                        │
EOS.Infrastructure                          │
   ▲                                        │
EOS.Orchestrator ──routes events to──► Role Projects (CTO, PrincipalEngineer, ...)
   ▲                                        │
EOS.Runner (composition root, references all)
```

`EOS.Dashboard`, `EOS.Planner`, and `EOS.Web` branch off `EOS.Contracts` directly (dashed line above) and never descend into `EOS.Application`/`EOS.Infrastructure`/role projects.

## 2.3 Architecture Fitness Rules (Machine-Enforced)

| Rule ID | Statement | Enforcement Mechanism |
|---|---|---|
| R-00 | No circular project references | Build-graph topological sort in `EOS.Gates` |
| R-01 | CTO's cross-module access is read-only | Interface-shape analysis (no command/write interfaces referenced) |
| R-02 | SeniorEngineer → CTO reference forbidden | Static forbidden-edge test (NetArchTest / ArchUnit-equivalent) |
| R-03 | Planner depends only on Contracts + Knowledge | Allowed-dependency whitelist test |
| R-04 | Dashboard depends only on Contracts (read side) | Allowed-dependency whitelist test |
| R-05 | Infrastructure → Web/Mobile forbidden | Forbidden-edge test |
| R-06 | Role-to-role direct reference forbidden | Forbidden-edge test scoped to `EOS.*Role*` namespace pattern |
| R-07 | EOS.Mobile has zero .NET assembly references | Toolchain-boundary check (no `.csproj`/`.dll` refs in `pubspec.yaml`/build graph) |
| R-08 | Every public contract in EOS.Contracts is versioned (Part 9-style semver) | Contract-versioning linter |
| R-09 | Every new project declares an owning role in Part 1 ownership table | Doc-sync check against this specification |

Fitness Rules run as a mandatory Quality Gate (§0.8.1, item 4) on every build and are re-validated automatically whenever `ArchitectureDriftDetected` fires (§0.10.1).

## 2.4 Cycle Prevention Strategy

- Dependency graph extracted from build metadata (project references + Flutter package deps for the cross-runtime boundary) after every build.
- Extracted graph topologically sorted; any failure to produce a valid ordering is a hard build failure, not a warning.
- New project additions must declare their allowed dependency set in `EOS.Tools` scaffolding *before* the project can be added to the solution — preventing accidental cycles at creation time rather than catching them after the fact.
# PART 3 — Event Catalog

All events flow through `EOS.Orchestrator` onto the messaging backbone (RabbitMQ — see Part 5) and are persisted to the event store (Part 4) with a consistent envelope:

```
EventEnvelope {
  event_id (uuid)
  event_type
  version (semver)
  producer
  correlation_id
  causation_id
  occurred_at
  payload (schema-versioned)
}
```

## 3.1 Event Definitions

| Event | Producer | Consumers | Payload (key fields) | Persistence | Replay Policy | Versioning |
|---|---|---|---|---|---|---|
| `TaskCreated` | EOS.Planner | Scheduler, Dashboard, EOS.TechLead | task_id, competencies_required, priority | Append-only event store (SQL Server) | Replayable indefinitely | v1 |
| `TaskStarted` | Scheduler | Dashboard, Knowledge (context capture) | task_id, actor_role, started_at | Event store | Replayable | v1 |
| `TaskCompleted` | Actor role (e.g. EOS.SeniorEngineer) | Planner, QA, Dashboard, Knowledge | task_id, evidence_refs[] (Artifact Registry) | Event store | Replayable; evidence refs must resolve at replay time | v1 |
| `TaskBlocked` | Any role, EOS.Gates | Scheduler, Dashboard, EOS.TechLead | task_id, blocking_gate/reason | Event store | Replayable | v1 |
| `TaskRetried` | Reality Validation pipeline (§0.15), Scheduler | Planner, Dashboard | task_id, retry_count, reason | Event store | Replayable | v1 |
| `CapabilityUnlocked` | EOS.Planner, Competency Graph engine | Planner, Dashboard, KPI engine | capability_id, unlocking_competencies[] | Event store + Knowledge Graph | Replayable | v1 |
| `CompetencyProven` | Role project (self-report) + EOS.Gates (verification) | Competency Graph, Knowledge, Dashboard | competency_id, role, proof_evidence_ref | Event store + Knowledge Graph | Replayable | v1 |
| `LessonLearned` | Any role, EOS.Gates (on novel failure) | Knowledge, Meta Learning pipeline (Part 14) | context, observation, source_task_id | Knowledge Graph (canonical) + event store | Replayable | v1 |
| `LessonPromoted` | Meta Learning pipeline | Knowledge, Dashboard | lesson_id → pattern_id | Knowledge Graph | Replayable | v1 |
| `ADRCreated` | Any L2+ role | Knowledge, Dashboard, Architecture Evolution (§0.10) | adr_id, title, scope | Artifact Registry + Knowledge Graph | Replayable | v1 |
| `ADRApproved` | CTO / Principal Engineer per Decision Matrix | Architecture Evolution, Knowledge | adr_id, approver, conditions | Artifact Registry + Knowledge Graph | Replayable | v1 |
| `ADRRejected` | CTO / Principal Engineer | Knowledge, Planner (re-plan trigger) | adr_id, rejector, reason | Artifact Registry + Knowledge Graph | Replayable | v1 |
| `ArchitectureDriftDetected` | EOS.Gates (fitness rule engine) | Principal Engineer, Architecture Evolution | rule_id, offending_edge | Event store | Replayable | v1 |
| `BenchmarkCompleted` | EOS.Pipeline / benchmarks runner | KPI engine, Dashboard, Reality Validation | benchmark_id, metrics{}, environment | Artifact Registry | Replayable | v1 |
| `IncidentDetected` | EOS.DevOps, monitoring (OpenObserve) | DevOps, QA, Dashboard, Knowledge | incident_id, severity, affected_modules[] | Event store | Replayable | v1 |
| `IncidentResolved` | EOS.DevOps | Knowledge (LessonLearned trigger), Dashboard | incident_id, resolution, root_cause | Event store + Knowledge Graph | Replayable | v1 |
| `PipelineCompleted` | EOS.Pipeline | Scheduler, Dashboard, QA | pipeline_id, stage_results[] | Event store | Replayable | v1 |
| `ReleaseApproved` | DevOps + QA (Decision Matrix consensus) | Pipeline, Dashboard, Knowledge | release_id, approvers[], risk_score | Artifact Registry + event store | Replayable | v1 |
| `KnowledgeUpdated` | EOS.Knowledge | Dashboard, Planner (re-plan signal) | node_id, node_type, change_kind | Knowledge Graph | Replayable | v1 |
| `PlannerGenerated` | EOS.Planner | Scheduler, Dashboard | plan_id, task_graph_ref | Artifact Registry | Replayable | v1 |
| `ProviderChanged` | EOS.AIArchitect | Dashboard, Knowledge, DevOps | from_provider, to_provider, justification | Event store + Knowledge Graph | Replayable | v1 |

## 3.2 Cross-Cutting Rules

- **Producer/consumer decoupling**: consumers never assume a producer's internal implementation — only the versioned payload schema (Part 2, R-08 mirrored for events).
- **Replay**: the full event store is replayable to reconstruct Dashboard/KPI state from scratch (disaster recovery dependency, Part 13) — this is why every event is append-only and no consumer is allowed to be the sole source of truth for anything derivable from the stream.
- **Versioning discipline**: a breaking payload change requires a new `event_type` version suffix (e.g., `TaskCompleted.v2`) with both versions live during a deprecation window tracked as an ADR.
- **New Mobile-domain events** (e.g., `MobileCrashDetected`, `StoreSubmissionApproved`) extend this catalog under the same envelope and versioning discipline — see Part 15 §15.4 for the mobile-specific extension list.
# PART 4 — Data Architecture

## 4.1 Store Ownership (No Duplication)

| Store | Owns | Never Stores |
|---|---|---|
| SQL Server | Transactional domain data: tasks, plans, ADRs, event store (append-only), release records | Vector embeddings, ephemeral cache, logs |
| SQLite | Local/offline mobile-side cache (Flutter Drift/SQLite, Part 15), edge-node offline queues | Canonical/shared state |
| Redis | Ephemeral cache, distributed locks, Scheduler in-flight state (Part 7), rate-limit counters | Durable knowledge, audit trail |
| RabbitMQ | In-transit messages / event delivery (not long-term storage — messages are drained into SQL Server event store) | Anything queryable long-term |
| ChromaDB (via EOS.VectorStore) | Embeddings for Knowledge Graph semantic search | Graph structure itself (owned by EOS.KnowledgeGraph relational/graph store) |
| OpenObserve | Logs, traces, metrics (observability telemetry) | Business/domain data |
| Vector Database (ChromaDB, above) | — consolidated, not a separate store from the row above | — |
| File Storage (blob) | Artifacts: binaries, APK/AAB/IPA, benchmark raw output, backups | Anything with a structured query need (indexed metadata lives in SQL Server pointing at blob keys) |
| Backups | Point-in-time snapshots of SQL Server + Knowledge Graph + File Storage manifests | Live/queryable copies |
| Metrics | OpenObserve (see above) | Duplicated in SQL Server |
| State | Redis (ephemeral) + SQL Server (durable) — no third copy | — |
| Knowledge | EOS.KnowledgeGraph + EOS.VectorStore jointly (graph structure + embeddings respectively) | Duplicated in Dashboard or Reports |
| Configuration | File-based per Part 10, loaded into Redis cache at runtime (cache, not source of truth) | Hardcoded in any project |
| Logs | OpenObserve | SQL Server |
| Reports | Artifact Registry (Part 8), generated from live queries against the above — never separately persisted state | — |

## 4.2 Ownership Rule

**No data duplication** (Constitution §0.1.1.5): every value has exactly one canonical store; every other subsystem that needs it either queries the canonical store directly or receives it transiently via an event (RabbitMQ) without persisting a second durable copy.

## 4.3 Mobile Data Note

Flutter clients hold **cache-only** copies (SQLite/Drift/Hive/ObjectBox, per Part 15) of data whose canonical home is SQL Server, synchronized via the Offline Synchronization + Conflict Resolution mechanisms defined in Part 15. The mobile cache is never treated as a source of truth during conflict resolution — SQL Server wins unless an explicit merge policy says otherwise.

---

# PART 5 — Agent Communication Architecture

## 5.1 Transport Selection Matrix

| Communication | Transport | Use Case |
|---|---|---|
| In-process (same runtime, same host) | Direct method call via `EOS.Orchestrator` mediator | Role-to-role coordination within `EOS.Runner` |
| Durable async cross-service | RabbitMQ | Event Catalog delivery (Part 3), Scheduler task dispatch |
| Real-time push to UI | SignalR | Dashboard live updates, incident alerts |
| High-throughput internal RPC | gRPC | EOS.Application ↔ EOS.Infrastructure ↔ EOS.Mobile bridge |
| External/public API | REST | EOS.Mobile ↔ backend (primary), third-party integrations |
| Tool-augmented agent calls | MCP (Model Context Protocol) | AI Architect-mediated tool use, external connector calls |
| Cross-module data shape agreement | EOS.Contracts (shared contracts, not a transport) | Underpins all of the above |

## 5.2 Consistency Model

- **Synchronization**: role coordination within a single Execution micro-cycle (§0.12.1) is synchronous via the Orchestrator mediator.
- **Eventual Consistency**: Knowledge Graph updates, Dashboard projections, and cross-service state (e.g., mobile cache) are eventually consistent, reconciled via the Event Catalog stream.

## 5.3 Resilience Policies

| Concern | Policy |
|---|---|
| Timeout Strategy | Per-call timeout budget defined in `EOS.SDK` Retry/Policy module (Part 11), tiered by transport (gRPC: 2s, REST: 5s, RabbitMQ consumer: cycle-bound) |
| Retry Strategy | Exponential backoff with jitter, max attempts defined per contract in `EOS.Contracts`; retries emit `TaskRetried` when task-scoped |
| Circuit Breakers | Per-provider (Provider Architecture, §0.14) and per-downstream-service circuit breaker in `EOS.SDK`; open-circuit state surfaces on Dashboard |
| Correlation IDs | Generated at the originating event/request, propagated through every hop (envelope field, §3.1) |
| Tracing | OpenTelemetry-compatible spans exported to OpenObserve, correlated via the same correlation ID |
| Dead-letter handling | RabbitMQ dead-letter queues feed into `IncidentDetected` after N failed deliveries |
# PART 6 — Task Lifecycle

## 6.1 States

```
Created → Planned → Ready → Running → (Waiting|Blocked|Retry) → Review → Testing → Verified → Released → Archived
                                                                                              ↘
                                                                                           Cancelled (from any state)
```

## 6.2 Transition Table

| Transition | Allowed Actor | Evidence Required | Required Gates | Rollback Path |
|---|---|---|---|---|
| Created → Planned | EOS.Planner | Backlog item + competency match (Competency Graph) | None | → Cancelled |
| Planned → Ready | Scheduler | Resource budget available (Part 7) | Dependency graph satisfied | → Planned |
| Ready → Running | Scheduler assigns; Role executes | Actor assignment logged | Actor competency proven (§0.3) | → Ready |
| Running → Waiting | Actor role | Blocking external dependency logged | None | → Running |
| Running → Blocked | EOS.Gates, any role | Gate failure record or unmet dependency | Relevant Quality Gate result | → Running (after fix) or → Cancelled |
| Blocked → Retry | Scheduler / Reality Validation | Root-cause note | Retry budget not exhausted (Part 7) | → Blocked (if retries exhausted) |
| Retry → Running | Scheduler | — | Same gates as Ready→Running | → Blocked |
| Running → Review | Actor role | Implementation evidence (diff, artifact) | Universal Gates §0.8.1 (1–5) | → Running |
| Review → Testing | Tech Lead / QA | Review approval recorded | Peer review gate | → Review |
| Testing → Verified | QA | Test evidence (Artifact Registry) | Domain Gate Pack (§0.8.2) incl. Mobile Gate Pack where applicable | → Testing |
| Verified → Released | DevOps + QA (Decision Matrix consensus) | Release evidence, risk score | `ReleaseApproved` event, NFR thresholds (§0.7) | → Verified |
| Released → Archived | Scheduler (automatic, retention policy) | — | None | Not applicable (historical) |
| Any → Cancelled | Product Owner / CTO | Cancellation justification (ADR if scope-significant) | None | Not applicable |

## 6.3 Evidence Linkage

Every transition's "Evidence" column resolves to an entry in the Artifact Registry (Part 8) — no transition is valid on the basis of a verbal/self-reported claim alone (Reality Validation, §0.15).

---

# PART 7 — Scheduler

## 7.1 Responsibilities

The Scheduler (`EOS.Orchestrator`-hosted subsystem) turns a `PlannerGenerated` plan into actual dispatch, respecting budgets and dependencies.

## 7.2 Core Structures

| Structure | Purpose |
|---|---|
| Priority Queue | Orders `Ready` tasks by priority score (from Planner + Decision Matrix risk weighting) |
| Dependency Graph | Task-level DAG ensuring a task only becomes `Ready` when its prerequisite tasks are `Verified`/`Released` |
| Resource Budget | Aggregate ceiling combining CPU, RAM, and Inference budgets per cycle |
| CPU Budget | Per-cycle compute ceiling, enforced per role-project execution pool |
| RAM Budget | Per-cycle memory ceiling, same enforcement point |
| Inference Budget | Token/spend ceiling per provider (Provider Architecture, §0.14), tracked in near-real-time |
| Daily Capacity | Aggregate task-throughput ceiling per day, informed by Engineering Economics (§0.16) historical velocity |
| Concurrency | Max simultaneous `Running` tasks per role/domain (prevents role overload) |
| Retry Windows | Time/attempt ceiling before a `Retry` transitions permanently to `Blocked` (Task Lifecycle, Part 6) |
| Maintenance Windows | Reserved periods for Disaster Recovery drills (Part 13) and infra maintenance where new dispatch is paused |

## 7.3 Scheduling Algorithm (Summary)

1. Pull `Ready` tasks ordered by Priority Queue.
2. Check Dependency Graph satisfaction.
3. Check Resource Budget headroom (CPU/RAM/Inference) against Daily Capacity.
4. Check Concurrency ceiling for the target role/domain.
5. Dispatch (emit `TaskStarted`); on failure to satisfy any check, task remains `Ready` and is re-evaluated next micro-cycle (§0.12.1).
6. On gate failure/timeout, apply Retry Window policy before permanent `Blocked`.

## 7.4 Mobile-Specific Scheduling Note

Mobile builds (APK/AAB/IPA — Part 15) additionally consume a **device-farm budget** dimension (distinct from CPU/RAM) representing real-device test capacity, reflecting the Mobile Economics note in §0.16.3.
# PART 8 — Artifact Registry

## 8.1 Purpose

Every generated artifact is versioned, content-addressed (hash), and indexed for query — the canonical evidence store referenced throughout Task Lifecycle (Part 6), Quality Gates (§0.8), and Reality Validation (§0.15).

## 8.2 Artifact Types

| Type | Producer | Retention |
|---|---|---|
| ADR | Any L2+ role | Permanent |
| Threat Model | DevOps / Principal Engineer | Permanent, revisioned per architecture change |
| Architecture (diagrams/specs) | Principal Engineer | Permanent, versioned with this specification |
| Lessons | Any role (raw form; canonical form lives in Knowledge Graph) | Permanent |
| Benchmarks | EOS.Pipeline / benchmarks runner | Rolling window + permanent summary |
| Coverage | QA / CI pipeline | Rolling window + trend summary |
| Reports (Daily Reports, §0.9) | Reporting engine | Rolling window per retention policy |
| Performance | Benchmarks + Reality Validation | Rolling window + permanent summary |
| Security | DevOps / QA | Permanent |
| Incidents | DevOps | Permanent |
| Design Documents | Any L2+ role | Permanent |
| Specifications | Principal Engineer / this document's lineage | Permanent |
| Reference Tests | QA | Permanent (golden references) |
| Fitness Reports | EOS.Gates | Rolling window + permanent summary |
| Evidence | Any gate/pipeline | Permanent (linked from Task Lifecycle transitions) |
| Snapshots | Backup subsystem (Part 13) | Rolling per DR policy |

## 8.3 Versioning Rule

Every artifact is immutable once written; a "change" creates a new version referencing the prior version's hash — never an in-place edit. This is what makes Reality Validation (§0.15) and audit trails trustworthy.

---

# PART 9 — Prompt Management

## 9.1 Prompt Registry

`prompts/` (Part 1 solution structure) is the source of truth; the registry loads, versions, and serves prompts to every role project — no role hardcodes prompt text.

## 9.2 Organization

```
prompts/
  <role>/                e.g. prompts/senior-engineer/
    <capability>.v<N>.prompt.md
    <capability>.metadata.json   (owner, eval-benchmark link, changelog)
```

## 9.3 Lifecycle Operations

| Operation | Description |
|---|---|
| Prompt Evolution | New version created via ADR-linked change (ties into Architecture Evolution, §0.10, when the prompt affects role authority/behavior boundaries) |
| Prompt Testing | Regression suite (golden input/output pairs) run as part of AI/ML Gate Pack (§0.8.2) |
| Prompt Benchmarking | Scored against eval suite; results are `BenchmarkCompleted` artifacts (Part 8) |
| Prompt Rollback | Previous version reactivated by pointer swap (no data loss — old versions never deleted) |
| Prompt Metrics | Success rate, hallucination rate, cost-per-call tracked per version in KPI engine (§0.13) |

---

# PART 10 — Configuration Strategy

## 10.1 Replacing the Monolithic appsettings.json

| File | Owns |
|---|---|
| `EOS.json` | Global system identity, environment, feature toggles at the platform level |
| `Planner.json` | Planning weights, risk tolerance defaults, replanning cadence |
| `Inference.json` | Model defaults, token limits, temperature/sampling defaults per role |
| `Providers.json` | Provider endpoints, fallback order, credentials *references* (not secrets themselves — see Security.json) |
| `Thresholds.json` | NFR thresholds (§0.7) per domain, gate pass thresholds (§0.8) |
| `Security.json` | Secret references (vault pointers), auth policy, certificate pinning config (mobile) |
| `Dashboard.json` | Dashboard layout/query definitions (read-projection wiring only) |
| `Knowledge.json` | Knowledge Graph/VectorStore connection + indexing policy |
| `Storage.json` | Data Architecture (Part 4) connection strings/pointers, retention policy values |
| `FeatureFlags.json` | Runtime feature toggles, consumed by Provider Architecture and Mobile Remote Configuration alike |

## 10.2 Loading Rule

Configuration files are loaded once at Bootstrap (Part 12), cached in Redis (Part 4), and hot-reloadable only for `FeatureFlags.json` and `Thresholds.json` — all other files require a Bootstrap re-run to change, preventing silent architectural drift from live config edits.

## 10.3 Ownership, Storage & Validation Responsibility

This section closes `Architecture-Validation-Report-v1.0.md` R3/R9's write-authority and validation-responsibility gap at the ownership level (§2.2, §6.2 of that report). It does not define a field-level schema — that is `EOS-Implementation-Roadmap-v1.0.md` WP-002's implementation task, not a documentation change.

| Concern | Answer |
|---|---|
| **Owner (may write)** | The role named in §0.2.1 whose domain the file's content matches (`Planner.json` → Product Owner; `Providers.json`/`Inference.json` → AI Architect; `Thresholds.json` → Principal Engineer; `Security.json` → DevOps; `Dashboard.json` → Tech Lead; `Knowledge.json`/`Storage.json` → Principal Engineer; `FeatureFlags.json` → Product Owner; `EOS.json` → Principal Engineer). A write by any other role is a Decision-Matrix-governed action (§0.6) like any other risk-bearing change, not a bypass — no separate configuration-write authority model exists beyond the roles and Authority Levels already defined in §0.2.3. |
| **Storage** | Each file is a versioned artifact in a top-level `config/` directory (repository root, sibling to `src/` — see Part 1 §1.1), loaded by `EOS.Runner` into the Bootstrap-time cache (§10.2) — not a database table, not a separate service. This is the simplest option consistent with the ten-file structure already in place; it introduces no new storage technology. |
| **Loading strategy** | As stated in §10.2 — unchanged by this section. |
| **Validation responsibility** | `EOS.Runner`'s Bootstrap sequence (Part 12, step "Validate") is the sole validation point: every file is parsed and checked against its schema (WP-002) before Bootstrap proceeds to "Ready." A malformed file fails Bootstrap closed — no subsystem performs its own redundant validation of a file it merely reads. This reuses the fail-closed posture already established for Protection Layer (Protection-Layer-Specification-v1.0 §26) rather than introducing a new validation mechanism. |

# PART 11 — EOS SDK

## 11.1 Modules

| Module | Purpose |
|---|---|
| Logging | Structured logging abstraction, OpenObserve sink |
| Telemetry | OpenTelemetry span/metric helpers |
| Events | Event envelope helpers, publish/subscribe base classes (Part 3) |
| Policies | Retry/circuit-breaker/timeout policy primitives (Part 5) |
| Retry | Backoff strategies used by Policies module |
| Knowledge | Thin client for `EOS.Knowledge` queries |
| Correlation | Correlation ID propagation helpers (Part 5) |
| Security | Auth token handling, secret-reference resolution (never raw secrets in code) |
| Contracts | Re-exports/aliases from `EOS.Contracts` for convenience |
| Base Classes | Base role, base gate, base pipeline stage abstractions |
| Extensions | DI registration helpers, `IServiceCollection` extension methods |
| Shared Kernel | Value objects/primitives shared across projects (mirrors `EOS.SharedKernel`) |
| NuGet Packaging | Packaging/publish scripts (`scripts/`) producing internal NuGet feed artifacts |

## 11.2 Design Rule

The SDK is the **only** allowed place for cross-cutting infrastructure concerns to leak into role projects — role projects depend on `EOS.SDK` + `EOS.Contracts` and nothing else infrastructural (reinforces Part 2, R-06).

---

# PART 12 — Bootstrap System

## 12.1 Bootstrap Sequence

```
Install → Validate → Generate Keys → Configure Providers → Start Infrastructure
   → Health Check → Initialize Knowledge → Seed Planner → Run Validation → Ready
```

| Step | Description |
|---|---|
| Install | Restore dependencies (.NET + Flutter toolchains), provision containers |
| Validate | Confirm configuration files (Part 10) are present and schema-valid |
| Generate Keys | Provision/rotate signing keys, TLS certs, mobile code-signing identities |
| Configure Providers | Resolve `Providers.json`, verify connectivity/auth to each configured provider |
| Start Infrastructure | Bring up SQL Server, Redis, RabbitMQ, ChromaDB, OpenObserve (Part 4) |
| Health Check | Verify each infra dependency responds within SLA before proceeding |
| Initialize Knowledge | Load/verify Knowledge Graph schema + VectorStore indices |
| Seed Planner | Load initial Competency Graph state and any seed backlog |
| Run Validation | Execute a smoke-test Execution micro-cycle end-to-end (Reality Validation, §0.15) |
| Ready | System accepts external task/dispatch traffic; emits a `SystemReady` operational signal |

## 12.2 Idempotency

Bootstrap is idempotent and safe to re-run — each step checks current state before acting, which is what makes it reusable both for first-install and for Disaster Recovery (Part 13) restores.

---

# PART 13 — Disaster Recovery Testing

## 13.1 Automated Recovery Verification

Backups (Part 4/8) already exist; this section automates *proving they work*.

| Activity | Cadence | Description |
|---|---|---|
| Restore Tests | Weekly | Automated restore of latest backup into an isolated environment |
| Integrity Verification | Every restore test | Checksum/row-count validation against source snapshot manifest |
| Knowledge Validation | Every restore test | Confirms Knowledge Graph + VectorStore consistency post-restore (cross-reference check) |
| Snapshot Verification | Daily | Lightweight validation that the latest snapshot is retrievable and non-corrupt |
| Recovery Benchmarks | Weekly | Measures RTO/RPO achieved against target SLAs; recorded as `BenchmarkCompleted` |
| Weekly Restore Drill | Weekly | Full Bootstrap (Part 12) execution against restored data in an isolated environment |
| Quarterly Disaster Simulation | Quarterly | Simulated regional/provider outage exercising failover, Scheduler maintenance windows (Part 7), and Provider fallback (§0.14) |

## 13.2 Failure Handling

A failed Restore Test or Recovery Benchmark emits `IncidentDetected` and blocks the next scheduled production release (`ReleaseApproved` gate) until resolved — DR readiness is a release gate, not a side activity.

---

# PART 14 — Meta Learning

## 14.1 Compounding Pipeline

```
Lesson → Pattern → Best Practice → Engineering Principle → Golden Path → Automation → Reusable Component → Platform Capability
```

| Stage | Trigger to Advance |
|---|---|
| Lesson | `LessonLearned` event (Part 3) from any task/gate/incident |
| Pattern | ≥3 independent Lessons cluster on the same root cause/context (similarity via VectorStore) — emits `LessonPromoted` |
| Best Practice | Pattern reviewed and ratified by Principal Engineer (ADR-linked) |
| Engineering Principle | Best Practice generalized across ≥2 domains (e.g., both Backend and Mobile) |
| Golden Path | Principle codified as a reusable, gated implementation template in `EOS.Tools` |
| Automation | Golden Path wired into Planner/Scheduler as an auto-invocable capability |
| Reusable Component | Automation packaged into `EOS.SDK` or a dedicated shared library |
| Platform Capability | Component adopted as a standing `CapabilityUnlocked` entry in the Competency Graph, available platform-wide |

## 14.2 Governing Rule

Constitution §0.1.1.7 requires every Lesson to have a *path* to Platform Capability status; Lessons that never advance are periodically reviewed (Sprint cycle, §0.12.1) and either re-attempted or explicitly archived with a documented reason, so the pipeline doesn't silently accumulate dead-end knowledge.

## 14.3 ROI Gate

Advancement from Golden Path → Automation requires passing the ROI check defined in Engineering Economics §0.16.2.
# PART 15 — Flutter Mobile Engineering Platform

Flutter is a **first-class engineering domain**, structurally equal to Backend and Web (Constitution §0.1.1.8). It is not modeled as a frontend rendering target of the Web domain — it has its own competency tree, its own gate pack, its own architect role (`EOS.MobileArchitect`), its own KPI set, and its own runtime isolation boundary (Part 1 §1.3, Part 2 R-07).

## 15.1 Mobile Engineering Competency Domain

Attached at the Competency Graph root (§0.3.1) as a peer sub-graph. Organized into competency clusters:

### 15.1.1 Architecture & State Management
Flutter Architecture · Clean Architecture · MVVM · Bloc · Cubit · Riverpod · Provider · Dependency Injection · go_router

### 15.1.2 UI/UX & Platform Fidelity
Responsive Design · Adaptive UI · Material 3 · Cupertino · Accessibility · Localization · RTL

### 15.1.3 Data & Offline
Offline-first · SQLite · Hive · Drift · ObjectBox · Secure Storage · Offline Synchronization · Conflict Resolution

### 15.1.4 Platform & Device Integration
Background Services · Push Notifications · Firebase Messaging · Deep Links · Biometrics · Camera · GPS · Bluetooth · NFC · File Management · Media Processing · QR/Barcode · Platform Channels · Native Android · Native iOS · Flutter Web · Desktop

### 15.1.5 Quality & Performance
Animations · Performance · Memory Optimization · Widget Testing · Golden Testing · Integration Testing · Patrol · Flutter Performance Profiling · Flutter DevTools

### 15.1.6 Delivery & Operations
CI/CD · Fastlane · Play Store Deployment · App Store Deployment · Crash Reporting · Analytics · Remote Configuration · Feature Flags

### 15.1.7 Security
Mobile Security · Certificate Pinning · OAuth2 · JWT · OpenID Connect

### 15.1.8 Enterprise Scale
Large Enterprise Flutter Architecture · Flutter Modularization · Flutter Package Design · Design System · Micro Frontends (Mobile)

### 15.1.9 AI & Connectivity
Flutter AI Integration · Semantic Kernel APIs · SignalR Integration · gRPC · REST · GraphQL · MQTT · WebSockets · Local LLM APIs · Flutter + .NET integration patterns

Each competency node follows the standard schema (§0.3.2): prerequisites, proof requirements (linked to the Mobile Gate Pack, §15.3), and unlocks (linked to Mobile-specific `CapabilityUnlocked` entries).

## 15.2 Integration Into Existing Sections

| Existing Section | Mobile Integration |
|---|---|
| Competency Graph (§0.3) | Mobile Domain root, §15.1 tree, attached as peer |
| Capability Planner (§0.4) | Mobile Architect supplies domain-specific planning constraints (device fragmentation, store review latency) |
| Knowledge Graph (§0.5) | Mobile lessons/patterns tagged `domain:mobile`, participate in Meta Learning (Part 14) identically to other domains |
| Engineering KPIs (§0.13) | Crash-free session rate, Store readiness score (added rows, §0.13.1) |
| Architecture Reviews (§0.10) | Mobile Architect included in Architecture Evolution workflow for mobile-scoped ADRs |
| Technical Leadership (§0.2) | `EOS.MobileArchitect` role, peer authority to AI Architect (L2) |
| Quality Gates (§0.8) | Mobile Gate Pack (§15.3 below) |
| Execution Cycles (§0.12) | Mobile builds respect device-farm budget (Part 7 §7.4) within the same cycle cadence |
| Incident Simulator (Part 13, DR) | Mobile crash/incident scenarios included in Quarterly Disaster Simulation |
| System Design Registry (§15.5) | Enterprise mobile project templates (below) |
| Platform Engineering | Flutter Modularization/Package Design feed platform-wide Golden Paths (Part 14) |
| Reality Validation (§0.15) | Real-device-farm evidence required, emulator-only insufficient |
| Engineering Economics (§0.16) | Device-fragmentation cost + store-review latency as first-class cost factors (§0.16.3) |
| Provider Architecture (§0.14) | Mobile provider integration path — backend-proxied or on-device Local LLM (§0.14.3) |
| Dashboards (§0.11) | Mobile KPI tiles, crash-free trend, store readiness trend |
| Benchmark Framework (Part 8) | Flutter integration_test perf suites in `benchmarks/` |
| Prompt Registry (Part 9) | `prompts/mobile-architect/`, `prompts/senior-engineer-mobile/` namespaces |

## 15.3 Mobile Quality Gates

New domain-specific gate pack (extends §0.8.2), required before a mobile task/artifact can advance Testing → Verified (Task Lifecycle, Part 6):

| Gate | Checks |
|---|---|
| Flutter Analyze | Static analysis clean (no errors, warnings under threshold) |
| Formatting | `dart format` compliance |
| Widget Tests | Widget-level test coverage threshold met |
| Golden Tests | Pixel-reference comparisons pass across target device/theme matrix |
| Integration Tests | End-to-end flows pass (Patrol/integration_test) |
| Performance | Cold start, frame-render (jank), memory thresholds met (NFR Framework, §0.7) |
| Accessibility | Platform accessibility API compliance (TalkBack/VoiceOver parity) |
| Localization | All target locales + RTL rendering verified |
| Security | Certificate pinning present, secure storage used for sensitive data, no hardcoded secrets |
| Package Audit | Dependency license/vulnerability audit clean |
| APK/AAB Validation | Build artifact validated (size budget, manifest correctness, signing) |
| IPA Validation | iOS build artifact validated (signing, entitlements, size budget) |
| Store Readiness | Store metadata, screenshots, privacy manifest complete |
| Crash-free Startup | Startup crash rate below threshold on real-device sample |

Mobile Gate Pack results feed the `Store readiness score` and `Crash-free session rate` KPIs (§0.13.1).

## 15.4 Mobile-Specific Event Catalog Extensions

Extending Part 3 under the same envelope/versioning discipline:

| Event | Producer | Consumers |
|---|---|---|
| `MobileBuildCompleted` | EOS.Pipeline (mobile lane) | Dashboard, QA |
| `MobileCrashDetected` | Crash reporting integration | DevOps, Knowledge (Lesson candidate) |
| `StoreSubmissionApproved` | EOS.DevOps (post store review) | Dashboard, Knowledge |
| `OfflineSyncConflictDetected` | EOS.Mobile sync engine | DevOps, Knowledge |

## 15.5 Mobile System Designs (Enterprise Project Registry)

Reference architectures maintained as System Design Registry entries (Artifact Registry, Part 8, type: Design Documents), each exercising a distinct combination of §15.1 competencies:

| Project | Primary Competency Emphasis |
|---|---|
| ERP Mobile | Large Enterprise Flutter Architecture, offline-first, RBAC/OAuth2 |
| CRM Mobile | Offline sync, push notifications, deep links |
| Warehouse Scanner | Camera/QR-Barcode, Bluetooth, background services |
| Field Service | GPS, offline-first, biometrics, media processing |
| Healthcare Mobile | Security/compliance, accessibility, secure storage |
| Banking Mobile | Certificate pinning, biometrics, OAuth2/OIDC, fraud-aware UX |
| Delivery Platform | GPS, real-time (WebSockets/SignalR), push notifications |
| IoT Dashboard | Bluetooth/NFC, MQTT, real-time telemetry rendering |
| Retail POS | Offline-first, hardware integration (barcode/payment), performance |
| Offline Inspection App | Offline-first, media capture, conflict resolution, sync |

Each entry in this registry must declare its NFR profile (§0.7 Mobile column) and pass the full Mobile Gate Pack (§15.3) before promotion to `Released` (Task Lifecycle, Part 6).
# FINAL VALIDATION

This section is the closing deliverable set: a review of the whole EOS as specified above, confirming internal consistency (Constitution §0.1.1.5, §0.1.1.8) and providing the artifacts needed to actually build it.

## F.1 Architecture Review

**Consistency check summary:**

- ✅ Every new capability in Parts 1–15 references back into a Part 0 subsystem it extends (no orphaned additions) — verified section-by-section in each Part's opening paragraph and in §15.2's integration table.
- ✅ No subsystem was rewritten or duplicated; Parts 1–15 are additive implementation detail beneath the Part 0 policy layer.
- ✅ Domain equality preserved: Mobile (Part 15) attaches at the same graph depth as Backend/Web/AI everywhere it appears (Competency Graph, KPIs, Gates, Economics).
- ✅ Single-source-of-truth preserved: Data Architecture (Part 4) has zero rows with more than one canonical owner.
- ✅ Dependency rules (Part 2) and Physical Architecture (Part 1) agree — every "never depends on" rule in Part 1 §1.2 has a matching fitness rule in Part 2 §2.3.

**Open architectural questions flagged for CTO review:**
1. Whether `EOS.Dashboard` should get a dedicated caching tier as Mobile KPI volume grows (currently reads live from Contracts projections — §0.11).
2. Whether Local LLM APIs (mobile on-device inference, §15.1.9) need their own Provider Architecture entry distinct from cloud providers, given different cost/latency/security profiles (§0.14.3 currently treats it as one branch).

## F.2 Dependency Impact Report

| Change | Impacted Subsystems | Nature of Impact |
|---|---|---|
| New `EOS.MobileArchitect` role | Autonomous Roles (§0.2), Decision Matrix (§0.6), Physical Architecture (Part 1), Module Dependency Rules (Part 2) | Additive: new peer role, new fitness-rule-governed project |
| Mobile Competency tree (§15.1) | Competency Graph (§0.3), Capability Planner (§0.4), KPIs (§0.13) | Additive peer sub-graph; no existing node edges changed |
| Mobile Gate Pack (§15.3) | Quality Gates (§0.8), Task Lifecycle (Part 6, Testing→Verified transition) | Additive gate pack selected when task.domain == mobile |
| Mobile events (§15.4) | Event Catalog (Part 3) | Additive events under existing envelope/versioning scheme |
| EOS.Mobile project + runtime boundary | Physical Repository Architecture (Part 1), Module Dependency Rules (Part 2, R-07) | New forbidden-edge fitness rule required (cross-runtime isolation) |
| Device-farm budget dimension | Scheduler (Part 7 §7.4) | Additive budget dimension alongside CPU/RAM/Inference |
| Mobile economics factors | Engineering Economics (§0.16.3) | Additive cost category |

No existing dependency edge was removed or redirected — every impact above is additive, consistent with the "preserve everything" mandate.

## F.3 Implementation Roadmap

| Phase | Scope | Exit Criteria |
|---|---|---|
| Phase 0 — Foundation | `EOS.Core`, `EOS.SharedKernel`, `EOS.Contracts`, `EOS.Domain`, Bootstrap (Part 12) skeleton | Bootstrap runs end-to-end against empty infra |
| Phase 1 — Core Roles | `EOS.Orchestrator`, CTO/PrincipalEngineer/TechLead/SeniorEngineer/QA/DevOps role projects, Decision Matrix enforcement | Role projects pass Fitness Rules R-00–R-06 |
| Phase 2 — Knowledge & Gates | `EOS.Knowledge`, `EOS.KnowledgeGraph`, `EOS.VectorStore`, `EOS.Gates`, Universal Gates (§0.8.1) | Sample task traverses full Task Lifecycle (Part 6) |
| Phase 3 — Planning & Scheduling | `EOS.Planner`, Scheduler (Part 7), Event Catalog (Part 3) live | End-to-end Planner→Scheduler→Execution micro-cycle demonstrated |
| Phase 4 — Web & Dashboard | `EOS.Web`, `EOS.Dashboard`, Daily Reports (§0.9), KPIs (§0.13) | Dashboard renders live KPI data with zero implementation references (R-04 holds) |
| Phase 5 — Mobile Domain | `EOS.MobileArchitect`, `EOS.Mobile`, Mobile Competency tree, Mobile Gate Pack, mobile events | R-07 fitness rule holds; one enterprise mobile reference app (§15.5) passes full Mobile Gate Pack |
| Phase 6 — AI/Provider Layer | `EOS.AIArchitect`, Provider Architecture (§0.14), Prompt Registry (Part 9) | Provider swap demonstrated with `ProviderChanged` event + rollback |
| Phase 7 — Resilience | Disaster Recovery (Part 13), Reality Validation (§0.15) pipeline | Weekly Restore Drill passes; Reality Score computed on sample tasks |
| Phase 8 — Meta Learning | Meta Learning pipeline (Part 14), ROI gating (§0.16.2) | At least one Lesson demonstrably reaches Automation stage |
| Phase 9 — Platform Hardening | Full Quarterly cycle (§0.12.1) run: Engineering Economics review, Disaster Simulation, Constitution review | All Part 0–15 fitness rules green in a single full cycle |

## F.4 Repository Migration Plan

1. Freeze current repository state; tag as `pre-eos-v1-migration`.
2. Scaffold new solution structure (Part 1 §1.1) alongside existing code — no in-place deletion.
3. Move existing shared/common code into `EOS.Core`/`EOS.SharedKernel` first (lowest-risk, no behavior change).
4. Introduce `EOS.Contracts` and migrate existing inter-module calls to go through it incrementally, module by module, verified by Fitness Rules (Part 2) after each migration step.
5. Stand up `EOS.Gates` early so every subsequent migration step is gate-checked, not just reviewed by hand.
6. Migrate role-equivalent existing logic into the new role projects one role at a time, starting with the lowest-authority role (Senior Engineer) to minimize blast radius.
7. Introduce Mobile domain (`EOS.Mobile`, `EOS.MobileArchitect`) as a net-new addition — no existing mobile code to migrate implies this phase can run in parallel with steps 3–6.
8. Cut over Dashboard/reporting last, once Knowledge Graph and Event Catalog are live, so Dashboards launch already reading from the final architecture rather than an interim shape.
9. Decommission any pre-migration ad hoc scripts/config only after one full Quarterly cycle (§0.12.1) has run clean on the new architecture.

## F.5 Project Structure / Solution Structure

See Part 1 §1.1 (Solution Structure) and §1.2 (Project Ownership) — reproduced there as the authoritative structure; not duplicated here per Constitution §0.1.1.5.

## F.6 Technology Stack Matrix

| Layer | Technology |
|---|---|
| Backend runtime | .NET (C#), Generic Host (`EOS.Runner`) |
| Web front-end | Blazor or React host (`EOS.Web`), SignalR for live updates |
| Mobile | Flutter/Dart (`EOS.Mobile`), platform channels for native Android/iOS |
| Relational store | SQL Server |
| Mobile/edge local store | SQLite (Drift), Hive, ObjectBox |
| Cache/ephemeral state | Redis |
| Messaging | RabbitMQ |
| Vector store | ChromaDB (via `EOS.VectorStore`) |
| Observability | OpenObserve (logs, metrics, traces), OpenTelemetry instrumentation |
| CI/CD | `EOS.Pipeline`, Fastlane (mobile store deployment) |
| Package management | NuGet (internal feed, `EOS.SDK`), pub.dev (Flutter packages) |
| IaC/deploy | Bicep/Terraform + Helm charts (`deploy/`) |
| Cross-service RPC | gRPC, REST, MCP |

## F.7 Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Cross-runtime boundary (Mobile ↔ .NET) becomes a hidden coupling point | Medium | High | R-07 fitness rule + contract versioning discipline (Part 2, Part 3) |
| Knowledge Graph becomes a bottleneck as Lesson volume grows | Medium | Medium | VectorStore indexing strategy review each Quarterly cycle; sharding evaluated in Phase 9 |
| Mobile store-review latency delays release cadence | High | Medium | Store readiness KPI tracked continuously (§0.13.1), submissions batched ahead of release windows |
| Provider outage disrupts AI-dependent roles | Medium | High | Circuit breakers + fallback provider order (Provider Architecture §0.14), Local LLM fallback for mobile (§15.1.9) |
| Migration introduces temporary architecture drift | High (during migration) | Medium | Fitness Rules run from Phase 0 onward (F.3), drift tracked as `ArchitectureDriftDetected` not silently tolerated |
| Golden Path automation accumulates low-ROI maintenance burden | Low | Medium | ROI gate (§0.16.2) required before Automation promotion |

## F.8 Readiness Assessment

| Dimension | Status After This Specification | Remaining Work |
|---|---|---|
| Constitutional/governance clarity | Ready | None — Constitution, Decision Matrix, Roles fully specified |
| Physical architecture | Ready | Implementation per Roadmap (F.3) |
| Data ownership | Ready | None — no duplication identified |
| Event-driven backbone | Ready | Implementation of producers/consumers per Roadmap |
| Mobile domain parity | Ready (specification) | Reference app build-out (§15.5), real-device farm provisioning |
| Disaster recovery | Specified, not yet exercised | First Weekly Restore Drill + Quarterly Disaster Simulation (Part 13) |
| Meta learning compounding | Specified, not yet exercised | Needs a live Lesson corpus to validate the pipeline end-to-end |

**Overall readiness: specification-complete, implementation not yet started.** This document is the Phase 0 entry criterion for the Implementation Roadmap (F.3).
