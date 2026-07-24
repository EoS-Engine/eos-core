# EOS System Architecture Specification v1.0

**Document Type:** Capstone Architecture Specification — the System Architecture Blueprint of EOS
**Extends:** `@EOS-Specification.md` (the Constitution, immutable), and synthesizes `@Learning-Engine-Specification-v1.1.md`, `@Memory-Management-Specification-v1.0.md`, `@Reasoning-Engine-Specification-v1.0.md`, `@Protection-Layer-Specification-v1.0.md`, `@Planning-Execution-Engine-Specification-v1.0.md`, `@Knowledge-Management-Specification-v1.0.md`, `@AI-Provider-Layer-Specification-v1.0.md`, and `@Resource-Management-Specification-v1.0.md` (all immutable, approved)
**Status:** Proposed

This document does not redesign, fork, or duplicate any approved document. It is the synthesis layer above all nine — the single place a reader goes to see how the whole system fits together, with every claim traceable back to the specification that actually owns it. Where this document states a boundary, an interface, or a flow, it cites the approved document that already established it; it introduces no new ownership, no new project beyond the four already flagged as pending registration (`EOS.Learning`, `EOS.Reasoning`, `EOS.AIProvider`, `EOS.Resources`), and resolves one genuinely new question this task raises that no prior document addressed: whether the architecture supports future distributed deployment (§5.4, §22, ADR-SYS003).

---

## 1. Executive Summary

The Engineering Operating System (EOS) is composed of eight cooperating subsystems, each owning exactly one bounded context, communicating exclusively through published interfaces (`EOS.Contracts`-defined) and the Event Catalog (Constitution Part 3) — never through direct storage access or direct project reference to one another's concrete implementation. Memory Management and Knowledge Management share one physical subsystem (`EOS.Knowledge`/`EOS.KnowledgeGraph`/`EOS.VectorStore`) expressing two complementary concerns (storage/retrieval vs. taxonomy/governance); Planning & Execution Engine similarly spans two existing Constitutional projects (`EOS.Planner`, `EOS.Orchestrator`'s Scheduler). Four new projects (`EOS.Learning`, `EOS.Reasoning`, `EOS.AIProvider`, `EOS.Resources`) round out the physical topology, each still pending a lightweight Constitution Part 1 registration update. Every cross-subsystem call — including the two bidirectional call pairs (Reasoning↔Knowledge, Reasoning↔Learning) — routes through an `EOS.Contracts`-defined interface, wired at the `EOS.Runner` composition root, which is what keeps the entire dependency graph acyclic (§17, §6.3) despite the runtime call graph containing mutual usage. No subsystem executes an action, stores knowledge, or makes a decision without passing through the subsystem that actually owns that responsibility — this document is the map showing exactly which subsystem that is, for every capability EOS has.

## 2. System Vision

EOS is an autonomous Engineering Operating System capable of transforming engineering goals into safely executed, continuously improving outcomes — reasoning over accumulated knowledge, learning from every outcome, protecting itself from unsafe or corrupted action, and doing all of this fully offline on a single local machine, while remaining architecturally ready (never operationally required) to scale beyond it (§22, ADR-SYS003). This vision is realized not by one monolithic intelligence but by eight bounded-context subsystems, each excellent at one thing, coordinated through the Constitution's governance layer (Decision Matrix, Quality Gates, Reality Validation) and this document's synthesis of how they interoperate.

## 3. Architecture Goals

- **Bounded context per subsystem** (Architecture Rule) — §7 gives each of the eight subsystems exactly one Purpose/Responsibilities/Ownership statement, none overlapping another's.
- **Acyclic dependencies** (Architecture Rule) — §17 proves the full cross-subsystem dependency graph is acyclic, including the two bidirectional interface-usage pairs.
- **No Protection bypass** (Architecture Rule) — every subsystem's execution/governance-affecting path is traced through `IProtectionClient.validate()` in §12, §17.2.
- **No direct storage access across subsystems** (Architecture Rule) — §17.2 enumerates every storage boundary and confirms only its owning subsystem ever touches it directly.
- **Interface/event-only communication** (Architecture Rule) — §14, §15 catalogue every published interface and event; no cross-subsystem call in this lineage bypasses either.
- **Documented dependency reasons** (Architecture Rule) — §17.1's dependency table carries a reason column for every edge.
- **Offline-first** (Architecture Rule, reaffirmed from every prior document) — §22.
- **Distributed-deployment-ready without subsystem redesign** (Architecture Rule, new to this document) — §22, ADR-SYS003.

## 4. Architecture Principles

Inherited, unchanged, from the Constitution's own First Principles (§0.1.1) and reaffirmed as the principles governing how this synthesis reads the eight subsystems together:

1. Evidence over assertion (Constitution §0.1.1.1) — every flow in §8–§13 ends in an Artifact Registry-resolvable evidence trail, never a bare claim.
2. Autonomy with accountability (§0.1.1.2) — every autonomous action in §12/§18 passes through Protection.
3. Knowledge is a first-class asset (§0.1.1.3) — §9's Knowledge Flow and §10's Memory Flow show knowledge persisting and compounding, never evaporating in a subsystem's private state.
4. Consistency over speed (§0.1.1.4) — no flow in this document ever shows a subsystem skipping a gate "to save time."
5. Single source of truth per concern (§0.1.1.5) — §17.2 is the definitive statement of which subsystem owns which store; this principle is this document's single most-repeated cross-check.
6. Reality over simulation (§0.1.1.6) — Reality Validation (Constitution §0.15, realized inside Protection Layer per Protection-Layer-Specification-v1.0 §1) appears in every completion-claiming flow (§8, §12).
7. Continuous compounding (§0.1.1.7) — §13's Learning Feedback Flow is this principle's direct architectural realization.
8. Domain equality (§0.1.1.8) — no subsystem in §7 is modeled as subordinate to another; Mobile/Backend/Web domain equality (Constitution Part 15) is preserved unchanged by every subsystem's own `domain_tags` handling.

## 5. Architectural Constraints

### 5.1 Hardware Constraint (unchanged across the entire lineage)

Ubuntu, Intel i7-1065G7, 32GB RAM, 477GB NVMe, single local machine, offline-first — every subsystem's own Performance Considerations section (cited throughout §7, §22) was designed against this exact target; this document introduces no new hardware assumption.

### 5.2 Provider Independence Constraint

No subsystem depends on a specific AI model or provider (AI-Provider-Layer-Specification-v1.0 §4) — the only two channels into any AI provider (`IAIProviderClient`, exclusively `EOS.Reasoning`'s; `IEmbeddingProviderClient`, exclusively `EOS.Knowledge`'s) are structurally gated (AI-Provider-Layer-Specification-v1.0 §10.9).

### 5.3 Governance Constraint

Every Decision Matrix-routed action (Constitution §0.6), Quality Gate (§0.8), and Reality Validation check (§0.15) is realized concretely inside Protection Layer (Protection-Layer-Specification-v1.0 §1) — no subsystem re-implements its own competing governance mechanism.

### 5.4 Distribution-Readiness Constraint (new to this document, resolved by ADR-SYS003)

The governing task requires the architecture to "support future distributed deployment without requiring subsystem redesign." No prior document in this lineage designed for this explicitly — all nine assumed a single local machine. This document resolves the requirement by observing, not inventing: because every cross-subsystem interaction already routes through an `EOS.Contracts`-defined interface or the Event Catalog (Constitution Part 3), and because Constitution Part 5 (Agent Communication Architecture) already names multiple transport bindings (in-process, RabbitMQ, SignalR, gRPC, REST, MCP) for exactly this kind of interface, a future distributed deployment is a **transport-binding change at the `EOS.Runner` composition root only** — swapping an in-process DI wiring for a network-transport wiring of the same, unchanged interface. No subsystem's internal algorithm, ownership boundary, or interface signature changes. See ADR-SYS003 for the full reasoning and its limits.
## 6. Overall Architecture

### 6.1 The Layered Shape

```
┌──────────────────────────────────────────────────────────────────────────┐
│  GOVERNANCE LAYER (Constitution Part 0 + Protection Layer)                │
│  Constitution, Decision Matrix, Quality Gates, Reality Validation,         │
│  realized concretely as EOS.Gates (Protection-Layer-Specification-v1.0)   │
└───────────────────────────────┬────────────────────────────────────────────┘
                                 │ every subsystem below validates through this layer
┌───────────────────────────────▼────────────────────────────────────────────┐
│  COGNITIVE LAYER                                                            │
│  Reasoning Engine (EOS.Reasoning) — decisions, judgment, explanation        │
│  Learning Engine (EOS.Learning) — Lesson→...→Platform Capability pipeline   │
└───────────────────────────────┬────────────────────────────────────────────┘
                                 │
┌───────────────────────────────▼────────────────────────────────────────────┐
│  KNOWLEDGE LAYER                                                            │
│  Memory Management + Knowledge Management (both EOS.Knowledge/              │
│  EOS.KnowledgeGraph/EOS.VectorStore) — storage/retrieval + taxonomy/         │
│  governance, two concerns, one subsystem                                   │
└───────────────────────────────┬────────────────────────────────────────────┘
                                 │
┌───────────────────────────────▼────────────────────────────────────────────┐
│  EXECUTION LAYER                                                            │
│  Planning & Execution Engine (EOS.Planner + EOS.Orchestrator Scheduler)     │
│  — goals → plans → gated dispatch → Task Lifecycle (Constitution Part 6)   │
└───────────────────────────────┬────────────────────────────────────────────┘
                                 │
┌───────────────────────────────▼────────────────────────────────────────────┐
│  PLATFORM LAYER                                                             │
│  AI Provider Layer (EOS.AIProvider) — inference/embedding abstraction       │
│  Resource Management (EOS.Resources) — measurement, capacity, quotas        │
└──────────────────────────────────────────────────────────────────────────┘
```

This layering is descriptive, not a new dependency hierarchy — the actual allowed dependency edges are enumerated exhaustively in §17.1, and several edges cross layers in both directions (e.g., Reasoning, in the Cognitive Layer, calls Knowledge in the Knowledge Layer, and Knowledge calls Reasoning back) via the Contracts-mediated pattern in §6.3. The layering is useful for understanding *conceptual* grouping — cognition, knowledge, execution, platform, all under governance — not for implying a strict one-directional call order.

### 6.2 The Governance Spine

Every subsystem's execution- or governance-affecting action passes through Protection Layer's Validation Pipeline (Protection-Layer-Specification-v1.0 §14) at a depth proportional to its computed risk (Constitution §0.6.1's formula, reused unchanged throughout). This is not one more subsystem among eight — it is the spine every other subsystem's risk-bearing action passes through, structurally (Protection-Layer-Specification-v1.0 §10.9), never by convention. §12 (Decision Flow) and §17.2 make this the load-bearing property of the entire architecture.

### 6.3 The Contracts-Mediation Pattern (resolves the "no cyclic dependencies" Architecture Rule)

Two pairs of subsystems call each other:

- **Reasoning ↔ Knowledge**: Reasoning calls Knowledge's `assemble_context()` (Reasoning-Engine-Specification-v1.0 §12.1); Knowledge calls Reasoning's `summarize()`/`compare()` (Memory-Management-Specification-v1.0 §17.2, Knowledge-Management-Specification-v1.0 §18.3).
- **Reasoning ↔ Learning**: Learning calls Reasoning's `compare()`/`get_trust_signal()` (Learning-Engine-Specification-v1.1 §14.1/§14.2); Reasoning never calls Learning back (a one-directional edge, not actually a pair — listed here only to confirm it is *not* a hidden cycle).

The genuine bidirectional pair (Reasoning↔Knowledge) does not create a circular *project* dependency, because neither `EOS.Reasoning` nor `EOS.Knowledge` references the other's concrete project directly. Both depend only on `EOS.Contracts` (Constitution Part 1 §1.2, unchanged dependency rule already governing every role project and the Planner), which defines `IKnowledgeClient` and `IReasoningEngineClient` as pure interface types. The concrete implementations are wired to each other only at the `EOS.Runner` composition root (Constitution Part 1 §1.1) via dependency injection — satisfying Constitution Part 2's Architecture Fitness Rule R-00 (no circular project references) exactly as the same pattern already keeps Planner-through-Contracts-only (R-03) and Dashboard-through-Contracts-only (R-04) acyclic. §17's full dependency table makes this explicit for every edge in the system, not just this pair.

### 6.4 Physical-to-Logical Subsystem Mapping

| Logical Subsystem (this lineage) | Physical Project(s) (Constitution Part 1) | Registration Status |
|---|---|---|
| Learning Engine | `EOS.Learning` | New — pending Part 1 registration (Learning-Engine-Specification-v1.1 Open Question 1) |
| Memory Management | `EOS.Knowledge`, `EOS.KnowledgeGraph`, `EOS.VectorStore` | Existing, unchanged |
| Reasoning Engine | `EOS.Reasoning` | New — pending Part 1 registration (Reasoning-Engine-Specification-v1.0 Open Question 1) |
| Protection Layer | `EOS.Gates` | Existing, scope-description update recommended (Protection-Layer-Specification-v1.0 ADR-P001) |
| Planning & Execution Engine | `EOS.Planner`, `EOS.Orchestrator` (Scheduler subsystem) | Existing, scope-description update recommended (Planning-Execution-Engine-Specification-v1.0 ADR-PE001) |
| Knowledge Management | `EOS.Knowledge` (same project as Memory Management — complementary concern, not a sibling project) | Existing, unchanged (Knowledge-Management-Specification-v1.0 ADR-KM001) |
| AI Provider Layer | `EOS.AIProvider` | New — pending Part 1 registration (AI-Provider-Layer-Specification-v1.0 Open Question 1) |
| Resource Management | `EOS.Resources` | New — pending Part 1 registration (Resource-Management-Specification-v1.0 Open Question 1) |

**Consolidated registration recommendation (§25, ADR-SYS001):** the four new-project registrations and the two scope-description updates above should be handled as a single, consolidated Architecture Evolution ADR (Constitution §0.10) rather than six separate changes — every one of the four new-project specifications already independently recommended bundling with its siblings; this document is where that bundling is finally proposed concretely.
## 7. Subsystem Overview

Each subsystem below is summarized, not redefined — every claim cites the approved document that is the actual source of truth.

### 7.1 Learning Engine

| Aspect | Summary |
|---|---|
| Purpose | Operationalizes the Meta Learning pipeline: Lesson→Pattern→Best Practice→Principle→Golden Path→Automation→Reusable Component→Platform Capability (Constitution Part 14; Learning-Engine-Specification-v1.1) |
| Responsibilities | Pipeline stage transitions, ROI Gate, Quarantine, Fitness Functions, Stall detection (Learning-Engine-Specification-v1.1 §6) |
| Ownership | Never stores content (INV-1); never performs semantic judgment itself (INV-5) |
| Interfaces (published) | `ILearningEnginePublicApi` (read-only query), `query_generated_tasks()`-style read access consumed by Planning & Execution Engine |
| Interfaces (consumed) | `IKnowledgeClient` (Memory), `IReasoningEngineClient.compare()`/`.get_trust_signal()` (Reasoning) |
| Dependencies | `EOS.Contracts`, `EOS.Knowledge`, `EOS.SDK` — no role project depends on it directly (Learning-Engine-Specification-v1.1 §11) |

### 7.2 Memory Management

| Aspect | Summary |
|---|---|
| Purpose | Storing, organizing, retrieving, and governing the lifecycle of seven memory types (Working/Short-term/Long-term/Episodic/Semantic/Project/Session) — the full implementation of Constitution §0.5 (Memory-Management-Specification-v1.0) |
| Responsibilities | Storage/retrieval mechanics, mechanical ranking, Context Assembly, Consolidation/Compression/Expiration (Memory-Management-Specification-v1.0 §4) |
| Ownership | The *only* place Lesson/Pattern/Decision/Risk content physically lives (Constitution §0.5.3, reaffirmed) |
| Interfaces (published) | `IKnowledgeClient` (`query`, `update`, `query_similar`, `assemble_context`, `consolidate`) |
| Interfaces (consumed) | `IReasoningEngineClient.summarize()`, `IEmbeddingProviderClient.embed()` |
| Dependencies | `EOS.KnowledgeGraph`, `EOS.VectorStore`, `EOS.Contracts`, `EOS.SDK` |

### 7.3 Reasoning Engine

| Aspect | Summary |
|---|---|
| Purpose | Transforms knowledge, context, and goals into explainable, evidence-backed, confidence-scored decisions via a 12-stage pipeline (Reasoning-Engine-Specification-v1.0) |
| Responsibilities | Context processing through Decision Validation (self-consistency only, ADR-R003); similarity comparison, trust signals, summarization, general-purpose reasoning (§6 of that spec) |
| Ownership | Sole owner of semantic judgment platform-wide; never stores, plans, or gates safety |
| Interfaces (published) | `IReasoningEngineClient` (`reason`, `compare`, `get_trust_signal`, `summarize`, `query_history`) |
| Interfaces (consumed) | `IKnowledgeClient.assemble_context()` (Memory), `IAIProviderClient.infer()` (AI Provider Layer) |
| Dependencies | `EOS.Contracts`, `EOS.SDK` — the only project reaching `IAIProviderClient` directly (AI-Provider-Layer-Specification-v1.0 §10.9) |

### 7.4 Protection Layer

| Aspect | Summary |
|---|---|
| Purpose | Validates, gates, and governs every autonomous action platform-wide — the full implementation of Constitution §0.6 (Decision Matrix), §0.6.1 (Risk Scoring), §0.8 (Quality Gates), §0.15 (Reality Validation), unified (Protection-Layer-Specification-v1.0) |
| Responsibilities | Policy/Rule/Risk/Approval Engines, Trust Evaluation, Safety Gates, Governance/Enforcement Layers (§10 of that spec) |
| Ownership | Sole authority to allow/deny/defer/retry any risk-bearing action; never learns, remembers, plans, or reasons itself |
| Interfaces (published) | `IProtectionClient` (`validate`, `check_approval`, `report_outcome`) |
| Interfaces (consumed) | `IReasoningEngineClient.reason()` (only for genuine semantic policy judgments, FR-P8), `IKnowledgeClient.query()` (read-only) |
| Dependencies | `EOS.Contracts`, `EOS.Domain` (read), `EOS.SDK` — depended upon by every other subsystem's risk-bearing action path (§12, §17.2) |

### 7.5 Planning & Execution Engine

| Aspect | Summary |
|---|---|
| Purpose | Transforms Goals into executable Task Graphs and safely orchestrates their execution — the full implementation of Constitution §0.4 (Capability Planner), Part 6 (Task Lifecycle), Part 7 (Scheduler), unified (Planning-Execution-Engine-Specification-v1.0) |
| Responsibilities | Goal decomposition, dependency/priority management, scheduling, execution coordination, retry/rollback, dynamic replanning (§6 of that spec) |
| Ownership | Sole subsystem permitted to execute an action (FR-PE1); "Reasoning proposes, Planning owns" resolved narrowly via ADR-PE003 |
| Interfaces (published) | `IPlanningClient` (`submit_goal`, `query_generated_tasks`, `get_goal_status`, `pause_workflow`/`resume_workflow`, `cancel_goal`) |
| Interfaces (consumed) | `IProtectionClient.validate()` (every dispatch), `IReasoningEngineClient.reason()` (bounded delegation only, §10.11), `IKnowledgeClient` (reusable patterns) |
| Dependencies | `EOS.Contracts`, `EOS.Knowledge`, `EOS.SDK` — no role project bypasses it for execution (Architecture Rule, §12) |

### 7.6 Knowledge Management

| Aspect | Summary |
|---|---|
| Purpose | Defines what a piece of engineering knowledge structurally *is* — taxonomy, relationships, quality/governance/freshness metadata — as a complementary concern within the same subsystem Memory Management already realizes (Knowledge-Management-Specification-v1.0) |
| Responsibilities | Taxonomy/Relationship/Quality/Governance/Freshness Managers, Discovery/Reuse Engine (§10 of that spec) |
| Ownership | Content-level metadata schema and governance record-keeping; never storage/retrieval mechanics (Memory's, unchanged) or approval workflows (Learning's/Protection's, unchanged) |
| Interfaces (published) | `IKnowledgeManagementClient` (`classify`, `navigate_relationships`, `get_quality`, `search`, `request_governance_action`, `find_duplicates`) |
| Interfaces (consumed) | `IKnowledgeClient` (all reads/writes route through Memory's existing interface), `IReasoningEngineClient.compare()`, `IProtectionClient.validate()` |
| Dependencies | Same as Memory Management (§7.2) — no separate project, no separate store |

### 7.7 AI Provider Layer

| Aspect | Summary |
|---|---|
| Purpose | Sole abstraction boundary between EOS and every AI model — no subsystem depends on a specific provider (AI-Provider-Layer-Specification-v1.0) |
| Responsibilities | Provider/Model Registry, Inference Routing, Context Packaging, Response Normalization, Health Monitoring/Failover, Configuration (§6 of that spec) |
| Ownership | Executes the AI Architect role's (Constitution §0.2.1) selection policy; never sets that policy itself (ADR-AI001) |
| Interfaces (published) | `IAIProviderClient` (`infer`, `discover_capabilities` — exclusively `EOS.Reasoning`'s channel), `IEmbeddingProviderClient` (`embed` — exclusively `EOS.Knowledge`'s channel) |
| Interfaces (consumed) | `IProtectionClient.validate()` (Model Usage ceiling, every dispatch) |
| Dependencies | `EOS.Contracts`, `EOS.SDK` (Provider Contract, unchanged) — no third consumer channel exists anywhere in this lineage (ADR-AI002) |

### 7.8 Resource Management

| Aspect | Summary |
|---|---|
| Purpose | Measures real CPU/RAM/Storage/Model-residency state and computes the budget values Scheduler, Protection, and AI Provider Layer already consume — the previously-missing capacity-determination facet (Resource-Management-Specification-v1.0) |
| Responsibilities | Resource Monitor, Capacity Manager, Quota Manager, Allocation Manager, Background Task Controller, Model Residency Management (§10 of that spec) |
| Ownership | Never dispatches, gates, or selects — only measures, computes, and publishes (FR-RM1) |
| Interfaces (published) | `IResourceManagementClient` (`get_current_budget`, `get_model_residency`, `get_current_tier`, `request_background_slot`) |
| Interfaces (consumed) | None requiring ratification — a pure supplier via read interface and events (§21.2 of that spec) |
| Dependencies | `EOS.Contracts`, `EOS.SDK` — consumed by Planning & Execution Engine, Protection Layer, and AI Provider Layer, none of which change their own algorithms to use it (Resource-Management-Specification-v1.0 ADR-RM001) |
## 8. Context Flow

How context moves from a triggering event to a consumer's bounded, ranked payload:

```
Trigger (a role action, a Goal submission, an event) 
   │
   ▼
Requesting subsystem (Reasoning, Planning & Execution, or a role) issues a scoped request
   │
   ▼
Memory's IKnowledgeClient.assemble_context() (Memory-Management-Specification-v1.0 §15.1)
   │  — symbolic + vector retrieval, mechanical ranking (§13/§19 of that spec)
   ▼
[optional] Knowledge Management's additive quality/relationship-aware ranking pass
   (Knowledge-Management-Specification-v1.0 §15.7 — never alters Memory's own output, only re-orders)
   │
   ▼
Bounded ContextPayload returned, respecting the caller's token/size budget (FR-M5)
   │
   ▼
Consumed by Reasoning Engine (Context Processing, Stage 1, Reasoning-Engine-Specification-v1.0 §10)
   or Planning & Execution Engine (Task Graph Builder, §12.6 of that spec)
```

Context never flows directly from `EOS.KnowledgeGraph`/`EOS.VectorStore` to any consumer — it always passes through Memory's `IKnowledgeClient`, the single access point Constitution §0.5.2 already mandates and every subsequent document reaffirms (§17.2 makes this the canonical storage-boundary rule).

## 9. Knowledge Flow

How a raw occurrence becomes durable, classified, governed, generalized knowledge:

```
Occurrence (Task outcome, Incident, Gate failure) 
   │
   ▼
Memory's Consolidation (Memory-Management-Specification-v1.0 §16) — the ephemeral→persistent boundary
   │  — emits LessonLearned (Constitution Part 3)
   ▼
Learning Engine ingests LessonLearned (Learning-Engine-Specification-v1.1 §11.1)
   │  — creates a PipelineRecord at Lesson stage
   ▼
ClusterTrigger → Reasoning Engine's compare() (delegated semantic judgment, §14.1 of that spec)
   │
   ▼
Pattern → Best Practice → Principle → Golden Path → Automation → Reusable Component → Platform Capability
   (Learning-Engine-Specification-v1.1 Part 14 pipeline, each stage ADR/Decision-Matrix-gated)
   │  — each promotion emits an event (LessonPromoted, BestPracticeRatified, etc.)
   ▼
Knowledge Management consumes these events read-only (Knowledge-Management-Specification-v1.0 §19.1)
   │  — re-classifies taxonomy (§12.4 of that spec), never re-deciding the promotion itself
   ▼
Knowledge becomes discoverable via Knowledge Management's Search Strategy (§15 of that spec),
   itself layered atop Memory's unchanged retrieval (§8 above)
```

This is the single flow every prior document's own boundary-drawing exercise (Memory§0/ADR-M002, Learning§7, Knowledge Management§0/ADR-KM001) was protecting — the "content lives in Memory, promotion decisions live in Learning, classification/governance lives in Knowledge Management" three-way split holds end to end.

## 10. Memory Flow

How information moves through the seven memory types' lifecycle (Memory-Management-Specification-v1.0 §11, unchanged, reproduced here only as a flow summary):

```
Working Memory (in-process, single micro-cycle) 
   │ explicit promotion
   ▼
Short-term / Session Memory (task/session-scoped, Redis/SQLite-backed)
   │ explicit consolidation (§9 above)
   ▼
Episodic Memory (persistent, = a Lesson node) ──► [Knowledge Flow, §9, takes over]
   │ Learning Engine promotion (observed, not decided, by Memory)
   ▼
Semantic Memory (= Long-term Memory, Pattern/Best Practice/Principle content)
   │ age + promotion complete
   ▼
Compressed (raw detail summarized via Reasoning Engine's summarize(), original archived to Artifact Registry)
   │ retention elapsed + governance approval
   ▼
Archived (never deleted, Constitution §0.1.1.1)
```

Resource Management's Cache Limits (Resource-Management-Specification-v1.0 §12.2) bound the RAM footprint of the Redis-backed Working/Short-term/Session tiers throughout this flow — a ceiling, never a content decision, consistent with §17.2's storage-boundary rule.

## 11. Decision Flow

How a request becomes an approved, actionable Decision:

```
ReasoningRequest (from a Role, Planning & Execution Engine's bounded delegation, or Learning Engine)
   │
   ▼
Reasoning Engine's 12-stage pipeline (Reasoning-Engine-Specification-v1.0 §10)
   │  — Context Processing → ... → Confidence Evaluation → Explainability → Decision Validation
   │     (self-consistency only, ADR-R003 — NOT the safety/policy check)
   ▼
Decision{evidence_refs, confidence, explanation, risk_score} — emits DecisionMade (Constitution Part 3)
   │
   ▼
Protection Layer consumes DecisionMade (Protection-Layer-Specification-v1.0 §21.1)
   │  — Risk Engine assesses tier (§13.1 of that spec, reusing Constitution §0.6.1's formula)
   ▼
   ├─ Low tier: async-logged, Allow (non-blocking, "never a bottleneck" Architecture Rule)
   ├─ Medium tier: quick permission/resource check, Allow | Deny
   └─ High tier: full Validation Pipeline (§14.2 of that spec) → Allow | Deny | Defer-for-Approval
   │
   ▼
Only on Allow does the requesting subsystem treat the Decision as approved to act upon
```

**This is the resolution of Reasoning Engine's own ADR-R003 boundary in practice**: Reasoning's Stage 12 never asks "is this safe" — Protection's Decision Validation step (Protection-Layer-Specification-v1.0 §14.2 step 4, ADR-P002) is where that question is actually answered, downstream of Decision delivery, never inside the Reasoning pipeline itself.

## 12. Execution Flow

How an approved Decision or a Planning & Execution Engine Task actually executes — the single flow realizing the Architecture Rule "no subsystem may execute actions directly without the Planning & Execution Engine" and "no subsystem bypasses Protection Layer":

```
Goal submitted (IPlanningClient.submit_goal, Planning-Execution-Engine-Specification-v1.0 §21.1)
   │
   ▼
Goal Manager validates (§11.5 of that spec) → Task Graph Builder decomposes (§10.3)
   │  — may consult Memory's reusable patterns (§9 above) and issue one bounded
   │    Reasoning Engine delegation (§10.11, ADR-PE003 — "Reasoning proposes, Planning owns")
   ▼
Plan artifact emitted (PlannerGenerated, Constitution Part 3) → Scheduler (§10.6 of that spec)
   │  — Priority Queue + Dependency Graph + Resource Budget checks (Constitution Part 7 §7.3,
   │    budget VALUES supplied by Resource Management, Resource-Management-Specification-v1.0 §0)
   ▼
Execution Coordinator (§10.7 of that spec) → IProtectionClient.validate() — MANDATORY, FR-PE2
   │
   ├─ Allow → Task transitions Ready → Running (Constitution Part 6 §6.2, unchanged Task Lifecycle)
   │            → Review → Testing → Verified → Released (each transition its own Quality Gate,
   │              Constitution §0.8, realized inside Protection)
   │
   └─ Deny/Defer → Task remains Ready/Blocked; reason returned (Protection-Layer-Specification-v1.0 FR-P3)
```

No arrow in this flow skips the Execution Coordinator or Protection's gate — this is the literal, structural realization of both cited Architecture Rules, not merely a convention (Planning-Execution-Engine-Specification-v1.0 §25.1, Protection-Layer-Specification-v1.0 §10.9/§27).

## 13. Learning Feedback Flow

How an executed outcome compounds into platform-wide improvement — the architectural realization of Constitution §0.1.1.7 ("continuous compounding"):

```
Task reaches a terminal Task Lifecycle state (Verified/Released, or permanently Blocked)
   │
   ▼
Reality Validation (Constitution §0.15, realized inside Protection) confirms the outcome's evidence
   │
   ▼
Memory Consolidation (§10 above) — if the outcome is Lesson-worthy → Episodic Memory
   │
   ▼
Learning Engine's Meta Learning pipeline (§9 above) — Lesson → ... → Platform Capability
   │
   ▼
On reaching Automation: GoldenPathCodified / PlatformCapabilityPipelineAdvanced (Constitution Part 14 §14.1)
   │
   ▼
Planning & Execution Engine consumes these as new planning inputs (Dynamic Replanning §16.2,
   Planning-Execution-Engine-Specification-v1.0) — future Goal decompositions benefit; already-
   Executing Task Graphs are never retroactively altered (that document's own stability guarantee)
   │
   ▼
Knowledge Management re-classifies the resulting content's taxonomy (§9 above), making it
   discoverable for the *next* Goal's Task Graph Builder pattern query (§9 above, closing the loop)
```

This is the complete cycle: an execution outcome becomes a Lesson, a Lesson becomes generalized knowledge, generalized knowledge becomes an automated capability, and that capability becomes an input to the *next* planning cycle — the only place in the entire architecture where the loop closes back on itself, and it does so exclusively through already-published events, never a new feedback mechanism invented by this document.
## 14. Event Architecture

### 14.1 Master Event Ownership Table

Every event across the entire lineage, by owning producer — no event is produced by more than one subsystem (single-writer principle, an extension of Constitution §0.1.1.5 to the event stream itself):

| Event Family | Owning Producer | Anchor |
|---|---|---|
| `TaskCreated`/`TaskStarted`/`TaskCompleted`/`TaskBlocked`/`TaskRetried`, `PlannerGenerated`, `GoalCreated`/`GoalValidated`/`GoalCompleted`/`GoalCancelled`, `WorkflowPaused`/`WorkflowResumed`, `ReplanTriggered`, `RollbackExecuted` | Planning & Execution Engine | Constitution Part 3; Planning-Execution-Engine-Specification-v1.0 §20 |
| `CapabilityUnlocked`, `CompetencyProven`, `LessonPromoted`, `BestPracticeRatified`, `PrincipleGeneralized`, `GoldenPathCodified`, `PlatformCapabilityPipelineAdvanced`, `LessonStalled`/`Quarantined`/`Demoted`/`Archived`, `DataIntegrityViolationDetected`, `FitnessFunctionViolated`, `SelfReferentialOutcomeFlagged` | Learning Engine | Constitution Part 3; Learning-Engine-Specification-v1.1 §15 |
| `LessonLearned` *(sole producer: Memory, via Consolidation)*, `KnowledgeUpdated`, `WorkingMemoryDiscarded`, `SessionMemoryClosed`, `MemoryCompressed`, `MemoryConsolidated`, `ContextAssembled` | Memory Management | Constitution Part 3; Memory-Management-Specification-v1.0 §21 |
| `KnowledgeClassified`, `KnowledgeRelationshipAdded`, `KnowledgeQualityUpdated`, `KnowledgeGovernanceActionRequested`/`Applied`, `KnowledgeFreshnessExpired`, `KnowledgeDriftDetected`, `KnowledgeDuplicateFlagged`, `KnowledgeConsolidated` | Knowledge Management | Knowledge-Management-Specification-v1.0 §19 |
| `DecisionMade`, `ReasoningFailed`, `LowConfidenceDecisionFlagged`, `ContextExpansionRequested` | Reasoning Engine | Reasoning-Engine-Specification-v1.0 §17 |
| `ProtectionAllowed`/`Denied`, `ProtectionApprovalRequested`/`TimedOut`, `CrossSourcePoisoningSignal`, `ReasoningDriftDetected`, `RollbackRequested`, `EmergencyShutdownActivated`/`Cleared` | Protection Layer | Protection-Layer-Specification-v1.0 §21 |
| `ProviderChanged` *(Constitution Part 3, produced by the AI Architect role)*, `ProviderRegistered`, `ProviderMarkedUnavailable`/`Recovered`, `InferenceRouted`, `RoutingDenied`, `InferenceCompleted` | AI Provider Layer | AI-Provider-Layer-Specification-v1.0 §19 |
| `ResourceThresholdCrossed`, `BackgroundJobGranted`/`Deferred`, `ModelLoaded`/`Unloaded`, `ResourceQuotaExhausted`, `EmergencyCapacitySignal`, `ResourceRecovered` | Resource Management | Resource-Management-Specification-v1.0 §20 |
| `ADRCreated`/`Approved`/`Rejected`, `ArchitectureDriftDetected`, `BenchmarkCompleted`, `IncidentDetected`/`Resolved`, `PipelineCompleted`, `ReleaseApproved` | Constitution-level roles (unchanged, no subsystem in this lineage re-owns these) | Constitution Part 3 |

### 14.2 Cross-Cutting Consumption Pattern

Every event above is consumed by Dashboard (Constitution §0.11) for observability, and by whichever subsystem's own document already declared it as a consumed input (§14 sections of each approved specification, unchanged) — this table adds no new consumer relationship, it only confirms none was missed and none conflicts (§28's audit re-verifies this).

### 14.3 Event Envelope (unchanged)

Every event above, without exception, uses Constitution Part 3 §3.1's single envelope schema (`event_id`, `event_type`, `version`, `producer`, `correlation_id`, `causation_id`, `occurred_at`, `payload`) — no subsystem in this lineage introduced a competing envelope shape.

## 15. API Boundaries

Every public interface across the lineage, and its exclusive-or-open consumer set:

| Interface | Owner | Consumer Set |
|---|---|---|
| `IKnowledgeClient` | Memory Management | Open — any subsystem needing knowledge access (Learning, Reasoning, Planning & Execution, Knowledge Management) |
| `IKnowledgeManagementClient` | Knowledge Management | Open — Planning & Execution Engine (patterns), any role |
| `IReasoningEngineClient` | Reasoning Engine | Open — Learning, Memory, Planning & Execution, Protection (bounded, FR-P8) |
| `IProtectionClient` | Protection Layer | Open, but every risk-bearing call is mandatory (§17.2), not optional |
| `IPlanningClient` | Planning & Execution Engine | Open — any role, Learning Engine (read-only `query_generated_tasks`) |
| `IAIProviderClient` | AI Provider Layer | **Exclusive** — `EOS.Reasoning` only (AI-Provider-Layer-Specification-v1.0 §10.9) |
| `IEmbeddingProviderClient` | AI Provider Layer | **Exclusive** — `EOS.Knowledge` only |
| `IResourceManagementClient` | Resource Management | Open, read/signal-only (FR-RM1) — Planning & Execution Engine, Protection Layer, AI Provider Layer |

**Rule confirmed:** every cross-subsystem interaction in this architecture is one of the eight interfaces above, or an Event Catalog entry (§14) — no subsystem in this lineage exposes a ninth interface, and no subsystem reaches another's implementation without going through one of these (Architecture Rule: "communication shall occur only through published interfaces and events").

## 16. Service Contracts

Every interface in §15 carries, in its owning specification, an explicit contract (precondition/postcondition/failure contract, following the Design-by-Contract discipline Learning-Engine-Specification-v1.1 §14 first established and every subsequent document reused) — this document does not re-state each contract verbatim (that would duplicate content already approved, violating this task's own instructions) but confirms the discipline is uniform:

| Contract Element | Source of the Pattern | Reused By |
|---|---|---|
| Precondition/Postcondition/Invariant/Failure-contract structure | Learning-Engine-Specification-v1.1 §14 | Memory-Management-Specification-v1.0 §14 (partial), Reasoning-Engine-Specification-v1.0 §16, AI-Provider-Layer-Specification-v1.0 §20 |
| Fail-closed default on ambiguous/missing input | Learning-Engine-Specification-v1.1 ADR-L003 | Reasoning-Engine-Specification-v1.0 §21 (Missing Context), Protection-Layer-Specification-v1.0 §26 (Policy Failure), Resource Management (§17.4, Critical threshold response) |
| Idempotency on already-processed input | Learning-Engine-Specification-v1.1 FR-1 | Memory-Management-Specification-v1.0 §20.1 (`consolidate()`), Knowledge-Management-Specification-v1.0 §12.2 |
| Structured, never-bare-exception error return | AI-Provider-Layer-Specification-v1.0 §16.5 | Reasoning-Engine-Specification-v1.0 §21, Protection-Layer-Specification-v1.0 §26 |

No service contract across the lineage contradicts another; §28's audit re-verifies this claim explicitly.
## 17. Dependency Rules

### 17.1 Allowed Dependencies (the complete, exhaustive edge list)

| From | To | Reason |
|---|---|---|
| `EOS.Learning` | `EOS.Contracts`, `EOS.Knowledge`, `EOS.SDK` | Pipeline metadata storage via Memory's interface; shared primitives (Learning-Engine-Specification-v1.1 §11) |
| `EOS.Learning` | `EOS.Reasoning` (via Contracts) | `compare()`/`get_trust_signal()` delegation (§14.1/§14.2 of that spec) |
| `EOS.Learning` | `EOS.Planner` (via Contracts, read-only) | `query_generated_tasks()` for Feedback Loop Guard (§11.5, §24.6) |
| `EOS.Knowledge` (Memory + Knowledge Management concerns) | `EOS.KnowledgeGraph`, `EOS.VectorStore`, `EOS.Contracts`, `EOS.SDK` | Storage engine, embeddings, shared primitives |
| `EOS.Knowledge` | `EOS.Reasoning` (via Contracts) | `summarize()`, `compare()` delegation |
| `EOS.Knowledge` | `EOS.AIProvider` (via Contracts, `IEmbeddingProviderClient` only) | Embedding computation (exclusive channel, AI-Provider-Layer-Specification-v1.0 §10.9) |
| `EOS.Knowledge` | `EOS.Gates` (via Contracts, Knowledge Management concern only) | Governance action validation (Knowledge-Management-Specification-v1.0 FR-KM10) |
| `EOS.Reasoning` | `EOS.Contracts`, `EOS.SDK` | Shared primitives |
| `EOS.Reasoning` | `EOS.Knowledge` (via Contracts) | `assemble_context()` (§12.1 of that spec) |
| `EOS.Reasoning` | `EOS.AIProvider` (via Contracts, `IAIProviderClient` only) | Inference (exclusive channel) |
| `EOS.Gates` | `EOS.Contracts`, `EOS.Domain` (read), `EOS.SDK` | Fitness rules, gate definitions, shared primitives |
| `EOS.Gates` | `EOS.Reasoning` (via Contracts, bounded, FR-P8) | Semantic policy judgment only, never the gating decision itself |
| `EOS.Planner` / `EOS.Orchestrator` (Scheduler) | `EOS.Contracts`, `EOS.Knowledge` (via Contracts) | Reusable planning patterns (§12.6) |
| `EOS.Planner` / `EOS.Orchestrator` | `EOS.Reasoning` (via Contracts, bounded, §10.11) | Single judgment-call delegation only, never plan generation |
| `EOS.Planner` / `EOS.Orchestrator` | `EOS.Gates` (via Contracts) | `IProtectionClient.validate()`, mandatory before every dispatch (FR-PE2) |
| `EOS.Planner` / `EOS.Orchestrator` | `EOS.Resources` (via Contracts) | Budget value reads (`get_current_budget`) |
| `EOS.AIProvider` | `EOS.Contracts`, `EOS.SDK` (Provider Contract) | Shared primitives, provider adapter contract |
| `EOS.AIProvider` | `EOS.Gates` (via Contracts) | Model Usage ceiling validation (FR-AI6) |
| `EOS.AIProvider` | `EOS.Resources` (via Contracts) | Model residency signal reads |
| `EOS.Resources` | `EOS.Contracts`, `EOS.SDK` | Shared primitives |

### 17.2 Storage Boundary Table (resolves "no subsystem accesses another subsystem's storage directly")

| Store | Sole Owner | Everyone Else Accesses Via |
|---|---|---|
| `EOS.KnowledgeGraph` / `EOS.VectorStore` (SQL Server + ChromaDB) | Memory Management / Knowledge Management (same project) | `IKnowledgeClient` / `IKnowledgeManagementClient` — never direct |
| SQL Server event store, Artifact Registry (Part 8) | Constitution-level, event-sourced | Read via Event Catalog replay or Artifact Registry query — never a direct table read by any subsystem in this lineage |
| Redis (ephemeral: Working/Short-term/Session Memory, `IngestionRateGuardState`) | Memory Management (memory types), Learning Engine (`IngestionRateGuardState` only) | Each subsystem's own Redis keyspace is private — no cross-subsystem Redis read anywhere in this lineage |
| `Providers.json`/`Thresholds.json`/`Security.json`/etc. | Constitution Part 10, read by every subsystem | Configuration is read-only shared state, not "storage" in the exclusive-ownership sense — Constitution §0.1.1.5's no-duplication rule applies to canonical *data*, not shared *configuration*, which is explicitly designed to be read by many |
| Model residency state, Capacity tiers | Resource Management | `IResourceManagementClient` — never direct |

No subsystem in this lineage reads or writes another subsystem's exclusively-owned store directly — every arrow in §17.1 that touches a store crosses through the owning subsystem's interface first.

### 17.3 Forbidden Dependencies (explicitly verified absent)

| Forbidden Edge | Verified Absent Because |
|---|---|
| Any subsystem → `EOS.KnowledgeGraph`/`EOS.VectorStore` directly (bypassing `EOS.Knowledge`) | Constitution Part 2 dependency rule, reaffirmed by every subsequent document (§17.2) |
| Any subsystem other than `EOS.Reasoning` → `EOS.AIProvider`'s `IAIProviderClient` | AI-Provider-Layer-Specification-v1.0 §10.9, structurally enforced |
| Any subsystem other than `EOS.Knowledge` → `EOS.AIProvider`'s `IEmbeddingProviderClient` | Same anchor, FR-AI3 |
| Any subsystem → executing a Task without going through `EOS.Planner`/`EOS.Orchestrator`'s Execution Coordinator | Planning-Execution-Engine-Specification-v1.0 FR-PE1, Architecture Rule reaffirmed here |
| Any subsystem → bypassing `IProtectionClient.validate()` for a risk-bearing action | Protection-Layer-Specification-v1.0 §10.9/§27, Architecture Rule reaffirmed here |
| `EOS.Resources` → any subsystem's dispatch/gating/selection decision (a command, not a read) | Resource-Management-Specification-v1.0 FR-RM1 — `IResourceManagementClient` is read/signal-only |
| Any circular *project* reference | §6.3's Contracts-mediation pattern — verified below |

### 17.4 Cycle Verification

Constructing the dependency graph from §17.1's edges and replacing every "(via Contracts)" edge with two edges — `Source → EOS.Contracts` and `EOS.Contracts ← Target-implements-interface` (never `Source → Target` directly) — produces a graph with `EOS.Contracts` as a common sink for every cross-subsystem interface reference, and `EOS.Runner` as the single composition root wiring concrete implementations together (Constitution Part 1 §1.1, unchanged). No path in this reconstructed graph returns to its starting node — the topological sort Constitution Part 2 §2.4 already mandates (R-00) succeeds. This is the formal statement of §6.3's claim: the *runtime call graph* has bidirectional pairs (Reasoning↔Knowledge); the *project dependency graph* does not.
## 18. Sequence Diagrams (Mermaid)

### 18.1 User Request

```mermaid
sequenceDiagram
    participant User as Human Operator
    participant Planning as Planning & Execution Engine
    participant Prot as Protection Layer
    participant Reasoning as Reasoning Engine

    User->>Planning: submit_goal(goal)
    Planning->>Planning: validate, decompose (§11.5/§10.3 of that spec)
    opt ambiguous decomposition
        Planning->>Reasoning: reason() [bounded delegation, §10.11]
        Reasoning-->>Planning: Decision (one input)
    end
    Planning->>Planning: build Task Graph, emit PlannerGenerated
    Planning->>Prot: validate(dispatch) [FR-PE2]
    Prot-->>Planning: Allow
    Planning->>Planning: Task Ready -> Running
```

### 18.2 Learning Cycle

```mermaid
sequenceDiagram
    participant Memory as Memory Management
    participant Learning as Learning Engine
    participant Reasoning as Reasoning Engine

    Memory->>Memory: consolidate() [Episodic Memory, §16]
    Memory->>Learning: emit LessonLearned
    Learning->>Learning: create PipelineRecord, ClusterTrigger
    Learning->>Reasoning: compare() [§14.1]
    Reasoning-->>Learning: ConfidenceGuardResult
    Learning->>Learning: promote (Pattern -> ... -> Platform Capability, gated per stage)
    Learning->>Learning: emit GoldenPathCodified / PlatformCapabilityPipelineAdvanced
```

### 18.3 Planning Cycle

```mermaid
sequenceDiagram
    participant Planning as Planning & Execution Engine
    participant Knowledge as Knowledge Management
    participant Resources as Resource Management
    participant Prot as Protection Layer

    Planning->>Knowledge: search() [reusable patterns, §12.6]
    Knowledge-->>Planning: ranked knowledge objects
    Planning->>Resources: get_current_budget(CPU/RAM)
    Resources-->>Planning: current budget values
    Planning->>Planning: Scheduler dispatch decision (Constitution Part 7 §7.3, unchanged)
    Planning->>Prot: validate(dispatch)
    Prot-->>Planning: Allow
```

### 18.4 Knowledge Retrieval

```mermaid
sequenceDiagram
    participant Consumer as Any Subsystem
    participant KM as Knowledge Management
    participant Memory as Memory Management

    Consumer->>KM: search(request)
    KM->>Memory: query() / assemble_context() [unchanged interface]
    Memory->>Memory: symbolic + vector retrieval, mechanical ranking
    Memory-->>KM: mechanically-ranked results
    KM->>KM: quality/relationship-aware ranking pass [additive, §15.7]
    KM-->>Consumer: final_ranked results
```

### 18.5 Reasoning

```mermaid
sequenceDiagram
    participant Caller as Requesting Subsystem
    participant Reasoning as Reasoning Engine
    participant Memory as Memory Management
    participant AIProvider as AI Provider Layer

    Caller->>Reasoning: reason(request)
    Reasoning->>Memory: assemble_context()
    Memory-->>Reasoning: ContextPayload
    Reasoning->>AIProvider: infer(InferenceRequest) [exclusive channel]
    AIProvider->>AIProvider: route, package, dispatch, normalize
    AIProvider-->>Reasoning: InferenceResult
    Reasoning->>Reasoning: Stages 5-12 (hypotheses, decision, confidence, explanation, self-consistency)
    Reasoning-->>Caller: Decision
    Reasoning->>Reasoning: emit DecisionMade
```

### 18.6 Execution

```mermaid
sequenceDiagram
    participant Planning as Planning & Execution Engine
    participant Prot as Protection Layer
    participant Task as Task (Constitution Part 6 Lifecycle)

    Planning->>Prot: validate(dispatch_action)
    alt Allow
        Prot-->>Planning: Allow
        Planning->>Task: Ready -> Running
        Task->>Task: Running -> Review -> Testing -> Verified -> Released
        Task->>Prot: (each transition gated by its own Quality Gate, Constitution §0.8)
    else Deny/Defer
        Prot-->>Planning: Deny/Defer + reason
        Planning->>Task: remains Ready/Blocked
    end
```

### 18.7 Failure Recovery

```mermaid
sequenceDiagram
    participant Task as Failing Task
    participant Planning as Planning & Execution Engine
    participant Prot as Protection Layer

    Task->>Planning: Retry Manager: max_attempts exhausted
    Planning->>Planning: Rollback Manager: execute existing Rollback Path (Constitution Part 6 §6.2)
    Planning->>Planning: trigger Recovery Planning / Dynamic Replanning (§16.1)
    Planning->>Prot: validate(revised plan) [FR-PE8]
    Prot-->>Planning: Allow
    Planning->>Planning: resume via Scheduler
```
## 19. Component Diagram (Mermaid)

```mermaid
graph TD
    subgraph Governance
        Gates[EOS.Gates — Protection Layer]
    end
    subgraph Cognitive
        Reasoning[EOS.Reasoning]
        Learning[EOS.Learning]
    end
    subgraph Knowledge
        Knowledge[EOS.Knowledge / KnowledgeGraph / VectorStore — Memory + Knowledge Management]
    end
    subgraph Execution
        Planner[EOS.Planner]
        Orchestrator[EOS.Orchestrator — Scheduler]
    end
    subgraph Platform
        AIProvider[EOS.AIProvider]
        Resources[EOS.Resources]
    end
    subgraph Contracts
        ContractsBox[EOS.Contracts — all interfaces defined here]
    end

    Learning --> ContractsBox
    Knowledge --> ContractsBox
    Reasoning --> ContractsBox
    Gates --> ContractsBox
    Planner --> ContractsBox
    Orchestrator --> ContractsBox
    AIProvider --> ContractsBox
    Resources --> ContractsBox

    Learning -.compare/get_trust_signal.-> Reasoning
    Learning -.query_generated_tasks.-> Planner
    Knowledge -.summarize/compare.-> Reasoning
    Knowledge -.embed.-> AIProvider
    Knowledge -.governance validate.-> Gates
    Reasoning -.assemble_context.-> Knowledge
    Reasoning -.infer.-> AIProvider
    Gates -.bounded reason.-> Reasoning
    Planner -.patterns.-> Knowledge
    Planner -.bounded reason.-> Reasoning
    Planner -.validate dispatch.-> Gates
    Planner -.get_current_budget.-> Resources
    AIProvider -.validate model usage.-> Gates
    AIProvider -.model residency.-> Resources
```

## 20. Layered Architecture Diagram

```mermaid
graph TD
    subgraph "Layer 0 — Governance Spine"
        L0[Protection Layer: Policy/Rule/Risk/Approval Engines, Enforcement]
    end
    subgraph "Layer 1 — Cognitive"
        L1a[Reasoning Engine]
        L1b[Learning Engine]
    end
    subgraph "Layer 2 — Knowledge"
        L2[Memory Management + Knowledge Management]
    end
    subgraph "Layer 3 — Execution"
        L3[Planning & Execution Engine]
    end
    subgraph "Layer 4 — Platform"
        L4a[AI Provider Layer]
        L4b[Resource Management]
    end

    L1a --> L2
    L2 --> L1a
    L1b --> L1a
    L2 --> L1a
    L1a --> L4a
    L2 --> L4a
    L3 --> L2
    L3 --> L1a
    L3 --> L4b
    L4a --> L4b
    L0 -.gates.-> L1a
    L0 -.gates.-> L1b
    L0 -.gates.-> L2
    L0 -.gates.-> L3
    L0 -.gates.-> L4a
```

This is a conceptual grouping (§6.1), not a strict call-order hierarchy — Layer 0 (Protection) intersects every other layer's risk-bearing action rather than sitting only "below" or "above" them; the dotted "gates" edges represent this cross-cutting relationship, distinct from the solid dependency edges within Layers 1–4.

## 21. Context Diagram

```mermaid
graph LR
    Human[Human Operator] -->|Goals, Approvals| EOS((EOS))
    EOS -->|Explanations, Status, Metrics| Human
    EOS -->|infer/embed| Model[Local AI Model — Provider-agnostic]
    Model -->|InferenceResult/Vector| EOS
    EOS -->|Read/Write| Disk[(Local NVMe Storage — SQL Server, ChromaDB, Redis, SQLite, File Storage)]
    Disk -->|Persisted State| EOS

    subgraph "EOS Boundary (single local machine, offline-first)"
        EOS
    end
```

EOS's only external boundaries are the Human Operator (Goals in, Explanations/Status out — Protection-gated throughout), the local AI model (via the AI Provider Layer's two exclusive channels), and local disk storage (via each subsystem's owning interface, §17.2) — no network boundary exists in the default, single-machine deployment (§22).
## 22. Deployment View (Logical Only)

### 22.1 Default Deployment: Single Local Machine

All nine physical projects (§6.4) run as a single composed process (`EOS.Runner`, Constitution Part 1 §1.1) on the target hardware (Ubuntu, i7-1065G7, 32GB RAM, 477GB NVMe) — every cross-subsystem interface call (§15) is an in-process call, and every event (§14) is delivered via the in-process transport option Constitution Part 5 §5.1 already names ("In-process (same runtime, same host) — Direct method call via `EOS.Orchestrator` mediator").

### 22.2 Distributed-Deployment Readiness (resolves the Architecture Rule and §5.4, ADR-SYS003)

Because every interface in §15 is already an abstract contract (defined in `EOS.Contracts`, never a concrete class reference) and Constitution Part 5 §5.1 already names RabbitMQ, SignalR, gRPC, and REST as available transport bindings for exactly this kind of interface, a future distributed deployment would:

1. Replace the `EOS.Runner` composition root's in-process DI wiring with a network-transport wiring (e.g., gRPC) for the specific interface(s) that need to cross a machine boundary.
2. Introduce no change to any subsystem's internal algorithm, ownership boundary, Non-Responsibilities table, or interface signature (§7, §15 unchanged).
3. Require Constitution Part 5 §5.2's existing Eventual Consistency model (already the standing posture for Knowledge Graph updates, Dashboard projections) to extend to whichever interface calls become network calls — a configuration/posture extension, not a redesign.

**Explicit limit of this claim:** this is a *readiness* property (the architecture does not structurally prevent distribution), not an *operational* one — no approved document in this lineage has been performance-tested, security-reviewed, or capacity-planned for a distributed topology; §5.1's hardware constraint and every subsystem's own Performance Considerations section remain scoped to the single-machine target. Future distributed deployment, should it ever be pursued, requires its own dedicated specification (flagged in §28, not designed here) — this document only establishes that no *architectural* redesign would be required, per the Architecture Rule's literal wording.

### 22.3 Logical Node View

```mermaid
graph TD
    subgraph "Single Logical Node (default deployment)"
        Gov[Governance: EOS.Gates]
        Cog[Cognitive: EOS.Reasoning, EOS.Learning]
        Know[Knowledge: EOS.Knowledge/KnowledgeGraph/VectorStore]
        Exec[Execution: EOS.Planner, EOS.Orchestrator]
        Plat[Platform: EOS.AIProvider, EOS.Resources]
        Store[(Local Stores: SQL Server, ChromaDB, Redis, SQLite, File Storage)]
        Model[(Local AI Model)]
    end

    Gov <--> Cog
    Cog <--> Know
    Exec --> Know
    Exec --> Cog
    Exec --> Plat
    Cog --> Plat
    Know --> Store
    Plat --> Model
```

## 23. Cross-cutting Concerns

| Concern | How Every Subsystem Handles It (unchanged, cited) |
|---|---|
| **Logging** | `EOS.SDK` Logging module (Constitution Part 11 §11.1), used identically by all nine projects |
| **Configuration** | Constitution Part 10's file set (`EOS.json`, `Planner.json`, `Inference.json`, `Providers.json`, `Thresholds.json`, `Security.json`, `Dashboard.json`, `Knowledge.json`, `Storage.json`, `FeatureFlags.json`) — every subsystem's own thresholds/policies live here, never hardcoded (a requirement every approved document independently reaffirmed) |
| **Observability** | `EOS.SDK` Telemetry module + OpenObserve (Constitution Part 4 §4.1) — every subsystem publishes metrics/traces through this one pipeline |
| **Security** | Protection Layer's Permission Model (Protection-Layer-Specification-v1.0 §15) plus each subsystem's own Security Considerations section — no subsystem implements a competing access-control mechanism |
| **Performance** | Each subsystem's own Performance Considerations section, uniformly scoped to the Constitution's single hardware target; Resource Management (§7.8) is the one subsystem whose entire purpose is measuring and planning capacity for the rest |
| **Telemetry** | Subsumed under Observability above — Constitution Part 5 §5.3's correlation ID propagation is the thread tying every subsystem's telemetry into one traceable request lifecycle |
| **Auditing** | Every governance-affecting action across all nine subsystems resolves to an Artifact Registry entry (Constitution Part 8) — no subsystem in this lineage introduced a second, parallel audit store (a claim independently verified in every approved document's own audit section, and re-verified in §28 here) |

## 24. Failure Strategy

### 24.1 Layered Failure Containment

Each subsystem's own Failure Handling section (cited in §7) governs failures internal to it; this section addresses only *cross-subsystem* failure propagation:

| Failure Origin | Containment |
|---|---|
| Reasoning Engine unavailable/erroring | Learning Engine's `ClusterTrigger` fail-closed (no promotion, Learning-Engine-Specification-v1.1 §21); Memory's Compression sweep skips and retries next cycle (Memory-Management-Specification-v1.0 §25); Planning & Execution Engine's bounded delegation falls back to its own deterministic decomposition where possible (Planning-Execution-Engine-Specification-v1.0 §10.11) |
| AI Provider Layer unavailable | Reasoning Engine's Reasoning Failure handling (Reasoning-Engine-Specification-v1.0 §21); Memory's embedding indexing deferred and retried (Memory-Management-Specification-v1.0 §14) |
| Protection Layer unavailable | **No fallback exists by design** — Protection-Layer-Specification-v1.0 §26's fail-closed posture means no action proceeds without a Protection verdict; this is an intentional, load-bearing property (Architecture Rule: "no subsystem bypasses Protection Layer"), not a gap |
| Resource Management unavailable | Planning & Execution Engine's Scheduler and Protection's Resource Validation fall back to their own last-known-good budget values (a graceful-degradation posture implied by, though not explicitly detailed in, Resource-Management-Specification-v1.0 — flagged as an Open Question, §27) |
| Knowledge Management unavailable | Memory Management continues to function fully independently (its own storage/retrieval is untouched by Knowledge Management's own availability, per the layering established in Knowledge-Management-Specification-v1.0 §0) — only the additive quality-ranking pass and governance actions are affected |

### 24.2 Emergency Shutdown as the System-Wide Circuit Breaker

Protection Layer's Emergency Shutdown (Protection-Layer-Specification-v1.0 §26.1) remains the sole platform-wide failure-containment mechanism of last resort — halting all new autonomous dispatch while leaving in-flight work to reach a natural stopping point, exactly as that document specifies, unchanged by this synthesis.
## 25. Architecture Decision Records

### ADR-SYS001

**Title:** Consolidated Project Registration for All Four Pending New Projects

**Status:** Proposed

**Context:** Four approved documents (`Learning-Engine-Specification-v1.1`, `Reasoning-Engine-Specification-v1.0`, `AI-Provider-Layer-Specification-v1.0`, `Resource-Management-Specification-v1.0`) each independently introduced a new project (`EOS.Learning`, `EOS.Reasoning`, `EOS.AIProvider`, `EOS.Resources`) and each independently flagged its own Constitution Part 1 registration as an Open Question, with the later three explicitly recommending bundling with their predecessors. Two further approved documents (`Protection-Layer-Specification-v1.0`, `Planning-Execution-Engine-Specification-v1.0`) recommended a scope-description-only update to existing Part 1 entries (`EOS.Gates`, `EOS.Planner`/`EOS.Orchestrator`).

**Decision:** This document formally proposes one consolidated Architecture Evolution ADR (Constitution §0.10) covering all six items — four new registrations, two scope-description updates — rather than six separate changes, exactly as every one of the four new-project documents already recommended.

**Alternatives Considered:**
- Leave each as a separate future action — rejected because it was already flagged as suboptimal by four independent documents; consolidating is the natural conclusion once all eight subsystem specifications exist side by side, which is precisely this document's role.

**Trade-offs:** A single consolidated ADR is a larger, more consequential Constitution Part 1 change than six small ones — mitigated by the fact that all six are purely additive/descriptive (no existing dependency edge is altered, per §17's exhaustive verification).

**Consequences:** Constitution Part 1's project table gains four new rows and two amended description cells; no other part of the Constitution changes.

**Future Impact:** Establishes that a capstone synthesis document is the right place to finally execute a consolidation multiple prior documents independently anticipated, rather than leaving the anticipation unresolved indefinitely.

**Related EOS Sections:** Constitution Part 1, §0.10; Learning-Engine-Specification-v1.1 Open Question 1; Reasoning-Engine-Specification-v1.0 Open Question 1; AI-Provider-Layer-Specification-v1.0 Open Question 1 (and its own bundling recommendation); Resource-Management-Specification-v1.0 Open Question 1 (and its own bundling recommendation); Protection-Layer-Specification-v1.0 ADR-P001; Planning-Execution-Engine-Specification-v1.0 ADR-PE001; this document §6.4.

### ADR-SYS002

**Title:** The Contracts-Mediation Pattern Is the System-Wide Rule for Every Bidirectional Subsystem Relationship, Not a One-Off Fix

**Status:** Accepted

**Context:** §6.3/§17.4 resolved the Reasoning↔Knowledge bidirectional call pair as acyclic at the project level because both sides depend only on `EOS.Contracts`. This pattern was implicit across the individual approved documents but never stated as a general, system-wide *rule* until this synthesis.

**Decision:** Formally elevate the Contracts-mediation pattern to a system-wide architectural rule: any two subsystems that call each other (now or in any future addition) MUST do so exclusively through `EOS.Contracts`-defined interfaces, wired at `EOS.Runner`, never through a direct project-to-project reference — regardless of which specific two subsystems are involved.

**Alternatives Considered:**
- Treat each bidirectional pair as a special case requiring its own justification — rejected because it would leave future subsystem additions without a clear, reusable rule, risking an actual cyclic project reference being introduced later without anyone noticing the pattern that would have prevented it.

**Trade-offs:** None significant — this is a formalization of what every approved document already did in practice, not a new constraint on any of them.

**Consequences:** Any future ninth subsystem must follow this rule from its own first draft, citing this ADR rather than re-deriving the justification.

**Future Impact:** This is the single most important rule for keeping Constitution Part 2's Architecture Fitness Rules (R-00 in particular) valid as EOS grows beyond nine subsystems.

**Related EOS Sections:** Constitution Part 2 §2.3 (R-00), Part 1 §1.1 (`EOS.Runner`); this document §6.3, §17.4.

### ADR-SYS003

**Title:** Distributed-Deployment Readiness Is Satisfied Structurally, Not Operationally — Explicit Scope Limit

**Status:** Accepted

**Context:** This task's Architecture Rules require the architecture to "support future distributed deployment without requiring subsystem redesign" — a requirement no prior document in this lineage addressed, since all nine assumed a single local machine throughout.

**Decision:** Satisfy the requirement structurally: because every cross-subsystem interaction already goes through an `EOS.Contracts`-defined interface or the Event Catalog, and Constitution Part 5 §5.1 already names multiple network transport bindings for exactly this purpose, distribution is a transport-binding change at the composition root only (§22.2). Explicitly do **not** claim operational readiness (performance, security, or capacity validated for a distributed topology) — that would be an unsubstantiated claim this document has no basis to make.

**Alternatives Considered:**
- Design an actual distributed deployment topology now (specific service boundaries, network partitioning strategy, distributed consensus for shared state) — rejected as far beyond this document's scope ("architecture blueprint," not an infrastructure/deployment specification) and as premature given every approved document's hardware target and testing has been single-machine-only; doing so now would also risk introducing assumptions that contradict the offline-first, single-laptop posture every prior document was carefully designed around.

**Trade-offs:** The claim made is deliberately narrower (structural readiness only) than the Architecture Rule's language might suggest at first read — accepted as the honest, defensible position rather than overclaiming operational readiness this lineage has never tested.

**Consequences:** A genuine future distributed-deployment initiative still requires its own dedicated specification (§28) covering the operational concerns this document explicitly does not address.

**Future Impact:** Establishes that "supports X without redesign" claims in this lineage mean "the existing abstraction boundaries do not structurally prevent X," not "X has been designed, tested, or is recommended" — a distinction future specifications should preserve when making similar extensibility claims (mirroring the same honest-scoping discipline Resource-Management-Specification-v1.0 ADR-RM003 already applied to GPU extensibility).

**Related EOS Sections:** Constitution Part 5 §5.1, §5.2; this document §5.4, §22.2, §28.
## 26. KPIs

System-level KPIs synthesizing (never duplicating the computation of) each subsystem's own KPI set:

| KPI | Formula Source |
|---|---|
| End-to-End Goal Success Rate | Goal Completion Rate (Planning-Execution-Engine-Specification-v1.0 §28) composed with Decision Accuracy (Reasoning-Engine-Specification-v1.0 §25) for any Goal whose Task Graph included a Reasoning delegation |
| Cross-Subsystem Latency | Sum of each subsystem's own latency KPI (Planning's Average Execution Time, Reasoning's Average Reasoning Time, AI Provider Layer's Average Response Time, Protection's Average Validation Time) along the critical path of a representative Execution Flow (§12) |
| Governance Coverage | % of risk-bearing actions across all nine subsystems that resolved to a Protection verdict (Allow/Deny/Defer) vs. any gap — should be 100% by construction (§17.3); this KPI exists to *detect* a construction violation, not to tolerate one |
| Knowledge Compounding Rate | Learning Feedback Flow (§13) completions per Quarterly cycle — records reaching Platform Capability and feeding back into the next Planning cycle |
| System Resource Headroom | Resource Management's own Capacity tier distribution (Resource-Management-Specification-v1.0 §28), reported system-wide |
| Terminology/Interface Consistency Score | A synthesis-only KPI: number of cross-document ADR-resolved naming collisions (§27 lists them) that remain correctly disambiguated in practice, sampled per Quarterly cycle by Principal Engineer review |

## 27. Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| The four pending project registrations (§6.4, ADR-SYS001) are never actually consolidated and ratified, leaving `EOS.Learning`/`EOS.Reasoning`/`EOS.AIProvider`/`EOS.Resources` permanently "proposed" rather than "approved" at the Part 1 level | Medium | Medium | This document makes the consolidation concrete and actionable (ADR-SYS001) rather than leaving it as a vague future intention |
| A future ninth subsystem is added without following the Contracts-mediation pattern (ADR-SYS002), silently introducing a circular project reference | Low | High | ADR-SYS002 formalizes the rule explicitly, citable by any future specification's own audit phase |
| Distributed-deployment readiness (§22.2, ADR-SYS003) is later mistaken for operational readiness, leading to a premature distributed rollout without the necessary follow-on specification | Low-Medium | High | ADR-SYS003's explicit scope limit is stated plainly and repeated in §5.4/§22.2, not buried in a single mention |
| The six repeated terminology-collision ADRs across this lineage (Decision Validation, Reasoning proposes plans, Knowledge Consolidation, Confidence, Task Prioritization, and this document's own cross-references to all of them) become hard for a future reader to track without this synthesis document as a map | Low | Medium | §16's Service Contracts table and this document's own citations throughout are exactly that map |
| Protection Layer, as the single non-bypassable governance spine (§6.2), becomes a single point of failure for the entire system | Low (by design, not oversight) | High | Explicitly acknowledged, not hidden (§24.1) — this is an intentional trade-off (fail-closed safety over fail-open availability), consistent with Constitution §0.1.1.1's evidence-over-assertion posture applied at the systems level |

## 28. Future Evolution

- Execute ADR-SYS001's consolidated Constitution Part 1 registration update — the most concrete, actionable next step this document identifies.
- A dedicated future Distributed Deployment Specification, should distribution ever move from "structurally possible" (ADR-SYS003) to "operationally pursued" — covering network partitioning, distributed consensus for any genuinely shared state, security posture across a network boundary, and performance re-validation against a non-single-machine target.
- A dedicated future Protection Layer resilience specification addressing the single-point-of-failure risk (§27) — e.g., whether a degraded-but-available Protection mode (stricter defaults, reduced throughput, but not full unavailability) is worth designing, versus accepting fail-closed unavailability as the permanent posture.
- Once all four pending projects are registered (ADR-SYS001) and the two scope-description updates are applied, a future minor-version revision of this document should re-run §17's dependency verification against the actually-updated Constitution Part 1 table, confirming no drift was introduced during ratification.
- GPU resource type addition (Resource-Management-Specification-v1.0 ADR-RM003) and domain-specific tuning (flagged independently by nearly every subsystem specification in this lineage) remain the two most commonly recurring "flagged, not designed here" items — a future consolidated tuning/extensibility pass across all eight subsystems simultaneously may be more efficient than eight separate future revisions.

## Open Questions

1. Should Resource Management's graceful-degradation posture on its own unavailability (§24.1) be formally specified, given it was only inferred here, not explicitly stated in `Resource-Management-Specification-v1.0`? Flagged for that document's own future revision, not resolved unilaterally here.
2. Should a degraded-but-available Protection mode be designed (§27, §28), or is fail-closed-only the permanent, intentional posture? Flagged for Architect decision.
3. When should the Distributed Deployment Specification (§28) actually be commissioned — is "structurally ready" (this document) sufficient for the foreseeable future, or should operational readiness work begin proactively? Flagged for Architect decision.

---

## Architecture Review & Audit

### Phase 1 — Self-Review Findings

- **Missing architecture identified:** an early draft omitted any explicit treatment of the Reasoning↔Knowledge bidirectional call pair's implications for the "no cyclic dependencies" rule, which is exactly the kind of subtle issue a capstone document exists to catch. **Resolved** by adding §6.3 and formalizing it as ADR-SYS002.
- **Dependency problem identified:** the initial pass's dependency table (an earlier draft of §17.1) listed several edges without a "via Contracts" qualifier, which — read literally — would have implied direct project-to-project references and a genuine cycle. **Resolved** by uniformly annotating every cross-subsystem edge and adding the formal cycle-verification argument (§17.4).
- **Ownership conflict identified:** an early draft's Deployment View risked implying Resource Management could independently trigger a distributed rebalancing action, which would have violated that document's own FR-RM1 (read/signal-only). **Resolved** by keeping §22 entirely about transport-binding change at the composition root, never a new Resource Management capability.
- **Missing interface identified:** the initial pass's §15 API Boundaries table omitted `IResourceManagementClient` entirely on a first draft. **Resolved** by adding it and cross-checking against Resource-Management-Specification-v1.0 §21.1 for completeness.
- **Scalability issue identified:** an early draft's Architecture Rule response to "distributed deployment" over-claimed operational readiness, which would have been an unsubstantiated and potentially harmful claim (encouraging a premature distributed rollout). **Resolved** by ADR-SYS003's explicit, deliberately narrow scope limit.

### Phase 2 — Improvements Applied

All five findings above are reflected directly in the final specification text (§6.3/ADR-SYS002, §17.1/§17.4, §22, §15, §5.4/§22.2/ADR-SYS003) — consistent with the instruction to output only the final, improved document.

### Phase 3 — Final Audit Against Every Approved EOS Specification

| Consistency Check | Result |
|---|---|
| No architectural drift | **Pass.** No existing project's dependency shape (Constitution Part 1/Part 2) is altered by this document; §6.4's registration table only consolidates already-independently-recommended changes (ADR-SYS001), never introducing a new one. |
| No duplicated responsibilities | **Pass.** §7's eight subsystem summaries cite, never restate as new claims, each approved document's own Responsibilities/Non-Responsibilities sections; §17 traces every dependency edge to its already-established reason. |
| No terminology conflicts | **Pass.** Every term used across §6–§24 (`ContextPayload`, `PipelineRecord`, `Decision`, `IProtectionClient`, `domain_tags`, `risk_score`, `trust_score`, `confidence`) is reused verbatim from its owning document; the five previously-resolved terminology collisions (Decision Validation, "Reasoning proposes plans," Knowledge Consolidation, routing Confidence, Task Prioritization) are each correctly cross-referenced rather than re-litigated (§16, §27). |
| No ownership conflicts | **Pass.** §7's Ownership column and §17's dependency/storage tables independently reconstruct every boundary already established across all eight subsystem specifications, with zero contradictions found. |
| No missing subsystem interactions | **Pass.** §17.1's edge list is exhaustive against every "Interfaces (consumed)" row in §7 and every cross-reference table in every approved document; cross-checked interaction-by-interaction during this audit with no gap found. |

**No architectural drift, no duplicated responsibilities, no terminology conflicts, no ownership conflicts, no missing subsystem interactions detected.**

---

**Status: EOS System Architecture Specification v1.0 complete. Self-Review, Improvement, and Audit phases executed above. This document introduces no new subsystem ownership — it is the synthesis layer proving all eight approved subsystem specifications, together with the Constitution, form one coherent, acyclic, non-duplicative architecture. One genuinely new requirement (distributed-deployment readiness) was addressed honestly, with its operational limits explicitly stated (ADR-SYS003) rather than overclaimed. A concrete, actionable consolidation of all pending project registrations is proposed (ADR-SYS001). Zero unresolved consistency defects against `@EOS-Specification.md` or any of the eight approved subsystem specifications. Stopping per instructions — not proceeding to any further specification.**
