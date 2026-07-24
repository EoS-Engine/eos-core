# EOS Implementation Roadmap v1.0

**Document Type:** Implementation Roadmap (Work Packages), not an architecture specification
**Role:** Lead Software Engineer / Technical Delivery Lead
**Basis:** All eleven approved, frozen architecture documents, unchanged
**Scope:** 100% of the approved architecture, decomposed into ~30 independently deliverable Work Packages

This document introduces no new subsystem, technology, design pattern, or responsibility. Every Work Package (WP) below implements a citation-traceable slice of an already-approved specification. Where the approved architecture itself left a capability under-specified (`EOS.Dashboard`, `EOS.Pipeline` — flagged in `Architecture-Validation-Report-v1.0.md` §1.1/§16), this roadmap implements only what the Constitution actually specifies for them and says so explicitly, rather than inventing missing design.

## Milestone Overview

| # | Milestone | WPs | Theme |
|---|---|---|---|
| 1 | Bootstrap Foundation | WP-001–WP-004 | Solution skeleton, contracts, events, data stores |
| 2 | First Intelligence Slice | WP-005–WP-009 | Minimal AI/Protection/Memory/Reasoning + first working end-to-end demo |
| 3 | AI Provider & Protection Hardening | WP-010–WP-013 | Full Provider abstraction and full governance layer |
| 4 | Memory & Knowledge Management | WP-014–WP-018 | Full retrieval, lifecycle, taxonomy, governance |
| 5 | Reasoning & Resource Management | WP-019–WP-022 | Full reasoning pipeline and capacity management |
| 6 | Planning, Execution & Learning | WP-023–WP-027 | Goals to execution, and the Meta Learning pipeline |
| 7 | Autonomous Orchestration & Production Readiness | WP-028–WP-030 | The full Loop, Dashboard, backup automation, hardening |

**Total: 30 Work Packages.**

---

## Milestone 1 — Bootstrap Foundation

### Goal
A buildable, runnable solution skeleton with the Constitution's Contracts, Event Catalog envelope, and physical data stores in place — nothing cognitive yet, but everything downstream work depends on.

### Why This Milestone Exists
Every subsystem in every later milestone depends on `EOS.Contracts`, the event envelope, and at least one physical store. Building these first, and only these, means every subsequent WP builds on a real, tested foundation instead of a placeholder.

### Work Packages Included
WP-001, WP-002, WP-003, WP-004

### Expected Outcome
`EOS.Runner` starts, executes Constitution Part 12's Bootstrap sequence, connects to all four physical stores, and reaches "Ready" with zero cognitive subsystems implemented yet.

### Validation Checklist
- [ ] Solution builds with zero errors
- [ ] `EOS.Runner` reaches Ready
- [ ] All four stores (SQL Server, Redis, ChromaDB, SQLite) are reachable from `EOS.Infrastructure`
- [ ] A test event publishes and is received by at least one subscriber

### Exit Criteria
Bootstrap sequence completes reliably three consecutive times from a clean environment before Milestone 2 begins.

---

### WP-001 — Solution Skeleton, Core Kernel & Contracts

| Field | Value |
|---|---|
| Objective | Create the full `EOS.sln` project skeleton and populate `EOS.Core`, `EOS.SharedKernel`, and `EOS.Contracts` with the base types every other project references. |
| Why this exists | Nothing else can be built without a compiling solution and a shared contracts surface — this is the literal foundation. |
| Architecture documents | `EOS-Specification.md` |
| Related sections | Part 1 (Physical Repository Architecture), Part 2 (Module Dependency Rules) |
| Prerequisites | None — this is the first Work Package. |
| Included components | All 33 projects from Part 1 §1.1 scaffolded as empty/near-empty compiling projects (including `EOS.Learning`, `EOS.Reasoning`, `EOS.AIProvider`, `EOS.Resources`, now registered in Part 1 per the consolidated registration); base value objects and entity primitives in `EOS.SharedKernel`; interface *shapes* (no logic) for every Part 2-referenced contract boundary in `EOS.Contracts`. |
| Explicitly excluded | Any business logic; any role project's actual behavior; any data store connection (WP-004); any event publishing mechanism (WP-003). |
| Expected deliverables | A solution that builds clean with `dotnet build`; Architecture Fitness Rule R-00 (no circular references) passes via a build-graph check. |
| Projects affected | All 33 Part 1 projects (scaffold only) |
| Estimated implementation effort | 3–4 days |
| Estimated code size | ~900 lines (mostly project files, base types, empty interfaces) |
| Build verification | `dotnet build EOS.sln` — zero errors, zero warnings |
| Test verification | A single architecture test (`EOS.ArchitectureTests`) asserting no circular project references |
| Demo / acceptance criteria | Solution opens in VS Code, builds, and the architecture test passes |
| Git commit scope | One PR: solution + project scaffolds + architecture test |
| Dependencies on previous WPs | None |

### WP-002 — Configuration Strategy & Bootstrap Sequence

| Field | Value |
|---|---|
| Objective | Implement the ten Constitution Part 10 configuration files with a real (minimal but genuine) schema, and the Bootstrap sequence from Constitution Part 12. |
| Why this exists | Every later WP reads configuration and every later WP runs inside the Bootstrap sequence — this must exist before anything else is wired up. |
| Architecture documents | `EOS-Specification.md` |
| Related sections | Part 10 (Configuration Strategy), Part 12 (Bootstrap System) |
| Prerequisites | WP-001 |
| Included components | `EOS.json`, `Planner.json`, `Inference.json`, `Providers.json`, `Thresholds.json`, `Security.json`, `Dashboard.json`, `Knowledge.json`, `Storage.json`, `FeatureFlags.json` with real schemas (closing part of the Validation Report's configuration-schema gap); the ten-step Bootstrap sequence (Install → Validate → Generate Keys → Configure Providers → Start Infrastructure → Health Check → Initialize Knowledge → Seed Planner → Run Validation → Ready) as an idempotent, re-runnable procedure. |
| Explicitly excluded | Provider connection logic itself (WP-005); actual Knowledge Graph initialization content (later Memory WPs); hot-reload beyond `Thresholds.json`/`FeatureFlags.json` (Constitution's own stated scope). |
| Expected deliverables | Bootstrap runs end-to-end against stub/no-op steps for anything not yet implemented, logging each step, and reaches Ready. |
| Projects affected | `EOS.Runner`, `EOS.SharedKernel` (config types) |
| Estimated implementation effort | 3–4 days |
| Estimated code size | ~700 lines |
| Build verification | `dotnet build` clean |
| Test verification | Unit tests for config schema validation (malformed file rejected); integration test running Bootstrap twice consecutively (idempotency) |
| Demo / acceptance criteria | `dotnet run --project EOS.Runner` logs all ten Bootstrap steps and reaches Ready |
| Git commit scope | One PR: config files + schema validation + Bootstrap sequence |
| Dependencies on previous WPs | WP-001 |

### WP-003 — Event Backbone

| Field | Value |
|---|---|
| Objective | Implement the Event Catalog's envelope structure and an in-process publish/subscribe mechanism via `EOS.Orchestrator`. |
| Why this exists | Every subsystem from Milestone 2 onward communicates via events; this must exist and be tested before any subsystem emits or consumes one. |
| Architecture documents | `EOS-Specification.md` |
| Related sections | Part 3 (Event Catalog, §3.1 envelope, §3.2 cross-cutting rules), Part 5 §5.1 (in-process transport) |
| Prerequisites | WP-001 |
| Included components | The `EventEnvelope` type (`event_id`, `event_type`, `version`, `producer`, `correlation_id`, `causation_id`, `occurred_at`, `payload`); an in-process publish/subscribe mediator hosted in `EOS.Orchestrator`; correlation ID propagation (Part 5 §5.3). |
| Explicitly excluded | Any specific event type beyond a generic test event (each subsystem WP registers its own events as it is built); RabbitMQ/gRPC/other transport bindings (out of scope for the single-machine target; Constitution Part 5 names them as future options only). |
| Expected deliverables | A working pub/sub mechanism any future WP can register events against, with correlation ID propagation demonstrated across at least two hops. |
| Projects affected | `EOS.Orchestrator`, `EOS.Contracts` |
| Estimated implementation effort | 2–3 days |
| Estimated code size | ~500 lines |
| Build verification | `dotnet build` clean |
| Test verification | Unit tests: envelope serialization round-trip; publish/subscribe delivers to all registered subscribers; correlation ID survives a two-hop chain |
| Demo / acceptance criteria | A test harness publishes a sample event and two independent subscribers both receive it with matching correlation IDs |
| Git commit scope | One PR: envelope + mediator + correlation propagation + tests |
| Dependencies on previous WPs | WP-001 |

### WP-004 — Data Store Foundations

| Field | Value |
|---|---|
| Objective | Establish real, tested connections from `EOS.Infrastructure` to SQL Server, Redis, ChromaDB, and SQLite, per Constitution Part 4's store-ownership table. |
| Why this exists | Every cognitive subsystem from Milestone 2 onward reads/writes one of these four stores; connections must be proven reliable before any subsystem logic is built on top. |
| Architecture documents | `EOS-Specification.md` |
| Related sections | Part 4 (Data Architecture, §4.1 Store Ownership, §4.2 Ownership Rule) |
| Prerequisites | WP-001; the Docker Compose stack from the Infrastructure Roadmap's Phase 3/4 must already be running |
| Included components | Connection management and health-check probes for all four stores; the event store's append-only write path (SQL Server, Part 3 §3.2's replay guarantee foundation); no domain schema yet beyond what proves connectivity. |
| Explicitly excluded | The actual `KnowledgeNode` schema (WP-007); any Redis keyspace convention beyond a connectivity test key; any ChromaDB collection beyond a smoke-test collection. |
| Expected deliverables | `EOS.Infrastructure` exposes a health-check surface confirming all four stores are reachable, consumed by Bootstrap's Health Check step (WP-002). |
| Projects affected | `EOS.Infrastructure` |
| Estimated implementation effort | 2–3 days |
| Estimated code size | ~600 lines |
| Build verification | `dotnet build` clean |
| Test verification | Integration tests against the real Docker Compose stack: each store connects, a write/read round-trip succeeds, a deliberate connection failure produces a clean error (not a crash) |
| Demo / acceptance criteria | Bootstrap's Health Check step (WP-002) reports all four stores healthy on a clean `docker compose up` |
| Git commit scope | One PR: connection management + health checks + integration tests |
| Dependencies on previous WPs | WP-001, WP-002 (Health Check step integration) |
## Milestone 2 — First Intelligence Slice

### Goal
The smallest possible real, end-to-end path — a user request producing a locally-generated, evidence-backed response, persisted to real storage — built from minimal (not stubbed-and-thrown-away) implementations of AI Provider Layer, Protection Layer, Memory Management, and Reasoning Engine.

### Why This Milestone Exists
Per the Implementation Principles (Vertical Slice Architecture, earliest executable system), the project needs a real, demonstrable, end-to-end capability as early as possible — not eight subsystems built in isolation with no integration proof until the very end. This milestone is deliberately thin in each subsystem's depth and deliberately complete in its end-to-end reach.

### Work Packages Included
WP-005, WP-006, WP-007, WP-008, WP-009

### Expected Outcome
A CLI command produces a real, offline-generated response, backed by real inference, a real (if minimal) Reasoning pipeline, a real Protection gate, and a real Memory write.

### Validation Checklist
- [ ] End-to-end request succeeds with networking disconnected
- [ ] Every step's real interface (not a mock) is exercised
- [ ] `IProtectionClient.validate()` is called on the real path, even though its logic is minimal
- [ ] The interaction is queryable as a real `KnowledgeNode` row

### Exit Criteria
The vertical slice demo (WP-009) passes its acceptance criteria three consecutive times, including once with networking disabled.

---

### WP-005 — AI Provider Layer: Single-Adapter Inference Channel

| Field | Value |
|---|---|
| Objective | Implement `IAIProviderClient.infer()` against a single Ollama adapter — enough to satisfy Reasoning Engine's needs, not the full Provider/Model Registry yet. |
| Why this exists | Reasoning Engine (WP-008) needs a real inference channel to produce a real Decision; the full Registry/Routing/Health/Failover architecture (Milestone 3) is not needed to prove the vertical slice. |
| Architecture documents | `AI-Provider-Layer-Specification-v1.0.md` |
| Related sections | §10.1a (AI Provider Manager), §16.1 (`IAIProviderClient.infer`), §14 (Context Packaging, minimal), §16.5 (Error Translation) |
| Prerequisites | WP-003 (events), WP-004 (stores, for configuration only) |
| Included components | `IAIProviderClient.infer()` with the exact ratified signature; one Provider Adapter targeting Ollama's local REST API; the closed error-type set (§16.5); structural enforcement that only `EOS.Reasoning` may call this interface (§10.9). |
| Explicitly excluded | Provider/Model Registry (WP-010); Inference Routing logic beyond "the one configured adapter" (WP-010); Health Monitoring/Failover (WP-010); `IEmbeddingProviderClient` (WP-011); `discover_capabilities()` (WP-011). |
| Expected deliverables | A working `infer()` call that reaches Ollama and returns a normalized `InferenceResult`. |
| Projects affected | `EOS.AIProvider` (new project, first real code) |
| Estimated implementation effort | 3–4 days |
| Estimated code size | ~600 lines |
| Build verification | `dotnet build` clean |
| Test verification | Unit tests for request/response normalization and error translation; one integration test against a real running Ollama instance |
| Demo / acceptance criteria | A test harness calls `infer()` with a sample prompt and receives a normalized `InferenceResult` containing real model output |
| Git commit scope | One PR: `EOS.AIProvider` project + Ollama adapter + tests |
| Dependencies on previous WPs | WP-003, WP-004 |

### WP-006 — Protection Layer: Minimal Validation Gate

| Field | Value |
|---|---|
| Objective | Implement `IProtectionClient.validate()` with real tiering logic (Low/Medium/High) but a deliberately conservative, always-safe policy set — real structure, minimal policy content. |
| Why this exists | The Architecture Rule "no autonomous action may bypass the Protection Layer" must be true from the very first end-to-end demo, not retrofitted later — this WP ensures the call path exists and is exercised from day one, even before the full Policy/Rule/Risk/Approval Engines (Milestone 3) are built. |
| Architecture documents | `Protection-Layer-Specification-v1.0.md` |
| Related sections | §10.1 (Overview), §13.1 (Risk Levels/tiering), §14.1 (tiered validation depth), §20 (verdicts: Allow/Deny/Defer/Retry) |
| Prerequisites | WP-003 |
| Included components | The tiering algorithm (§14.1) with a minimal, hardcoded-conservative rule set; the four verdicts; structural wiring so every risk-bearing call in later WPs routes through this interface (§10.9). |
| Explicitly excluded | Policy Engine, Rule Engine, Risk Engine, Approval Engine, Trust Evaluation, Safety Gates, Governance Layer (all full implementations deferred to WP-012/WP-013); Emergency Shutdown (WP-013); all eleven Protection Domains beyond a generic pass-through (WP-013). |
| Expected deliverables | A real, callable `IProtectionClient.validate()` that returns Allow for the vertical slice's low-risk demo action, with the call itself logged and auditable. |
| Projects affected | `EOS.Gates` |
| Estimated implementation effort | 2–3 days |
| Estimated code size | ~450 lines |
| Build verification | `dotnet build` clean |
| Test verification | Unit tests for each tier's routing logic; a test confirming a deliberately high-risk test action is NOT auto-allowed |
| Demo / acceptance criteria | The vertical slice's request visibly passes through `validate()` (via log output) and receives Allow |
| Git commit scope | One PR: minimal Protection gate + tiering + tests |
| Dependencies on previous WPs | WP-003 |

### WP-007 — Memory Layer: Minimal Storage & Write Path

| Field | Value |
|---|---|
| Objective | Implement the real `KnowledgeNode` schema and a single write path (`update()`/`consolidate()`-equivalent) into SQL Server — not the full seven-memory-type lifecycle yet. |
| Why this exists | Reasoning Engine's output (WP-008) needs somewhere real to be persisted; building the real schema now avoids a throwaway prototype schema that Milestone 4 would otherwise need to migrate away from. |
| Architecture documents | `Memory-Management-Specification-v1.0.md` |
| Related sections | §9 (Domain Model / `KnowledgeNode`), §10.9-equivalent (mandatory metadata, minimal subset), §20.1 (`IKnowledgeClient.update()`, minimal) |
| Prerequisites | WP-004 |
| Included components | The `KnowledgeNode` table schema in SQL Server; a minimal `IKnowledgeClient` exposing only `update()` (write) for this milestone; Episodic-Memory-equivalent classification for the vertical slice's interaction record. |
| Explicitly excluded | The full seven memory-type lifecycle and Storage Strategy (WP-014); Retrieval Strategy and mechanical ranking (WP-014); Context Assembly (WP-015); Consolidation/Compression/Expiration (WP-015/WP-016); `EOS.VectorStore`/ChromaDB integration (WP-014, since retrieval is not needed for this milestone's write-only slice). |
| Expected deliverables | A working `update()` call that persists a real, schema-valid `KnowledgeNode` row. |
| Projects affected | `EOS.Knowledge`, `EOS.KnowledgeGraph` |
| Estimated implementation effort | 3–4 days |
| Estimated code size | ~550 lines |
| Build verification | `dotnet build` clean |
| Test verification | Unit tests for schema validation; an integration test writing a real row and reading it back directly via SQL |
| Demo / acceptance criteria | The vertical slice's interaction is queryable as a real row in SQL Server after a demo run |
| Git commit scope | One PR: `KnowledgeNode` schema + minimal write path + tests |
| Dependencies on previous WPs | WP-004 |

### WP-008 — Reasoning Engine: Minimal Pipeline

| Field | Value |
|---|---|
| Objective | Implement enough of the 12-stage Reasoning pipeline to produce one well-formed, evidence-backed `Decision` — not the full reasoning-type catalog yet. |
| Why this exists | This is the cognitive center of the vertical slice; it must call the real AI Provider Layer (WP-005) and produce a real, schema-valid `Decision` for Protection (WP-006) and Memory (WP-007) to act on. |
| Architecture documents | `Reasoning-Engine-Specification-v1.0.md` |
| Related sections | §10 (Stages 1, 2, 7, 10, 11, 12 only — Context Processing, Goal Understanding, Decision Making, Confidence Evaluation, Explainability, self-consistency Decision Validation), §13.3 (`Decision` structure), §16.1 (`reason()`) |
| Prerequisites | WP-005 (inference), WP-007 (for evidence reference resolution, read path only needed conceptually — actual read path is WP-014, so this WP writes evidence references that resolve once WP-014 exists) |
| Included components | `IReasoningEngineClient.reason()` with a single-hypothesis, minimal-stage pipeline; a real, non-empty `Explanation` object; real `evidence_refs`/`confidence` population (no placeholder values). |
| Explicitly excluded | The full 12-stage pipeline including Intent Analysis, Constraint Evaluation, Hypothesis Generation (multi-hypothesis), Alternative Exploration, Trade-off Analysis (WP-019); all 13 reasoning types beyond the one implicit default (WP-019); `compare()`, `get_trust_signal()`, `summarize()` (WP-020); Decision Ranking, Decision History (WP-020). |
| Expected deliverables | A working `reason()` call producing a schema-valid `Decision` from a real inference call. |
| Projects affected | `EOS.Reasoning` (new project, first real code) |
| Estimated implementation effort | 4–5 days |
| Estimated code size | ~750 lines |
| Build verification | `dotnet build` clean |
| Test verification | Unit tests per included stage; an integration test producing a real `Decision` from a real prompt via WP-005's Ollama adapter |
| Demo / acceptance criteria | `reason("explain the SOLID principles")` returns a `Decision` with non-empty evidence, confidence, and explanation |
| Git commit scope | One PR: `EOS.Reasoning` project + minimal pipeline + tests |
| Dependencies on previous WPs | WP-005 |

### WP-009 — First Vertical Slice Integration

| Field | Value |
|---|---|
| Objective | Wire WP-005 through WP-008 together behind a single CLI entry point, proving the full User Request → AI Provider → Reasoning → Memory → Response path. |
| Why this exists | This is the milestone's own reason for existing — the first real, demonstrable, offline, end-to-end capability. |
| Architecture documents | All four of the above, plus `EOS-System-Architecture-Specification-v1.0.md` §12 (Execution Flow, thin-slice analogue) |
| Related sections | System Architecture Specification §8 (Context Flow), §11 (Decision Flow, minimal) |
| Prerequisites | WP-005, WP-006, WP-007, WP-008 |
| Included components | A minimal CLI command (`eos ask "<text>"`); the wiring calling Reasoning → Protection (`validate()`) → Memory (`update()`) in sequence; structured error handling for a malformed request. |
| Explicitly excluded | Any Planning & Execution Engine involvement (Milestone 6) — this slice bypasses Goal/Task machinery entirely by design, exactly as the Infrastructure Roadmap's Phase 5 specifies; any Learning Engine involvement (Milestone 6). |
| Expected deliverables | A working CLI demo, offline-verified. |
| Projects affected | `EOS.Runner`, `EOS.Tools` (CLI host) |
| Estimated implementation effort | 2–3 days |
| Estimated code size | ~350 lines |
| Build verification | `dotnet build` clean |
| Test verification | An end-to-end integration test running the full CLI command against the real Docker Compose stack, asserting a real response and a real persisted row |
| Demo / acceptance criteria | `eos ask "explain the SOLID principles"` succeeds with networking disconnected; the interaction is queryable in SQL Server afterward |
| Git commit scope | One PR: CLI entry point + wiring + end-to-end test |
| Dependencies on previous WPs | WP-005, WP-006, WP-007, WP-008 |
## Milestone 3 — AI Provider & Protection Hardening

### Goal
Replace Milestone 2's minimal AI Provider and Protection implementations with their full, frozen-specification architecture — without breaking the working vertical slice.

### Why This Milestone Exists
Milestone 2 deliberately built the smallest viable version of these two subsystems. Every later milestone (Memory, Knowledge, Reasoning, Planning, Learning) depends on the *full* versions being real — the Provider Registry/Health/Failover for reliability, and the full Policy/Rule/Risk/Approval Engines for genuine safety — before autonomous behavior of any real sophistication is allowed to run.

### Work Packages Included
WP-010, WP-011, WP-012, WP-013

### Expected Outcome
`IAIProviderClient` and `IProtectionClient` both reach their full specified architecture; the Milestone 2 vertical slice continues to pass its own acceptance criteria unchanged, now running through the full implementations instead of the minimal ones.

### Validation Checklist
- [ ] The WP-009 vertical slice demo still passes, unmodified, against the new full implementations
- [ ] Provider/Model Registry supports at least the one Ollama/Qwen combination already in use, registered properly rather than hardcoded
- [ ] Protection's full tiered Validation Pipeline runs all six steps for a High-tier test action
- [ ] Emergency Shutdown can be triggered and cleared in a test environment

### Exit Criteria
No regression in the Milestone 2 demo; both subsystems' own approved KPIs (Inference Success Rate; Blocked Unsafe Requests) are computable from real data.

---

### WP-010 — AI Provider Layer: Registry, Routing, Health & Failover

| Field | Value |
|---|---|
| Objective | Replace WP-005's single hardcoded adapter with the full Provider Registry, Model Registry, Inference Router, Health Monitor, and Failover. |
| Why this exists | Reliability and future multi-provider/multi-model support (Milestone 7/Phase 9 growth) require the real registry-and-routing architecture, not a hardcoded binding. |
| Architecture documents | `AI-Provider-Layer-Specification-v1.0.md` |
| Related sections | §10.2–§10.4, §10.8 (Provider Registry, Model Registry, Inference Router, Health Monitor), §15 (Inference Routing), §17 (Provider Health) |
| Prerequisites | WP-005 |
| Included components | Full Provider/Model Registry with `Providers.json`-driven configuration; the complete routing algorithm (§15.7) including capability/health/resource/priority/policy filtering; Health Monitoring (availability, latency, failure detection); Failover to a ranked candidate list. |
| Explicitly excluded | `IEmbeddingProviderClient` (WP-011); `discover_capabilities()` (WP-011); Future Cloud/Vision/Specialized provider *types* (architecturally supported, not implemented — no concrete need yet, per YAGNI). |
| Expected deliverables | `infer()` now resolves through the full Registry/Router rather than a hardcoded call; a deliberately-failed provider triggers logged Failover. |
| Projects affected | `EOS.AIProvider` |
| Estimated implementation effort | 5–6 days |
| Estimated code size | ~1000 lines |
| Build verification | `dotnet build` clean |
| Test verification | Unit tests per routing filter stage; an integration test simulating a provider failure and confirming Failover; regression run of WP-009's vertical slice test |
| Demo / acceptance criteria | The vertical slice demo still passes; a deliberately-injected Ollama outage produces a clean `ProviderMarkedUnavailable` event and, if a second candidate is registered, a successful Failover |
| Git commit scope | One PR: Registry/Router/Health/Failover, replacing WP-005's hardcoded path |
| Dependencies on previous WPs | WP-005 |

### WP-011 — AI Provider Layer: Embedding Channel & Capability Discovery

| Field | Value |
|---|---|
| Objective | Implement `IEmbeddingProviderClient.embed()` and `IAIProviderClient.discover_capabilities()`. |
| Why this exists | Memory Management's full retrieval strategy (Milestone 4) requires a real embedding channel; Reasoning's reasoning-type selection (Milestone 5) benefits from capability discovery. |
| Architecture documents | `AI-Provider-Layer-Specification-v1.0.md` |
| Related sections | §13 (Model Capabilities), §16.1 (`discover_capabilities`), §20.2 (`IEmbeddingProviderClient`) |
| Prerequisites | WP-010 |
| Included components | The embedding channel, exclusively consumable by `EOS.Knowledge` (structurally enforced, §10.9); capability discovery query against the Model Registry. |
| Explicitly excluded | Any embedding model beyond one registered local option; Future Embedding Models as a distinct provider type (no concrete need yet). |
| Expected deliverables | A working `embed()` call producing a normalized `Vector`. |
| Projects affected | `EOS.AIProvider` |
| Estimated implementation effort | 2–3 days |
| Estimated code size | ~400 lines |
| Build verification | `dotnet build` clean |
| Test verification | Unit tests for vector normalization; an integration test confirming `EOS.Reasoning` cannot call `embed()` (structural enforcement test) |
| Demo / acceptance criteria | `embed("test content")` returns a real vector of the configured dimensionality |
| Git commit scope | One PR: embedding channel + capability discovery + enforcement test |
| Dependencies on previous WPs | WP-010 |

### WP-012 — Protection Layer: Policy, Rule, Risk & Approval Engines

| Field | Value |
|---|---|
| Objective | Replace WP-006's minimal tiering with the full Policy Engine, Rule Engine, Risk Engine, and Approval Engine. |
| Why this exists | Real autonomous behavior in Milestones 5–7 requires real policy evaluation, real risk scoring (Constitution §0.6.1's formula), and real Decision-Matrix-routed approval — not a hardcoded conservative default. |
| Architecture documents | `Protection-Layer-Specification-v1.0.md` |
| Related sections | §10.2–§10.4 (Policy/Rule/Approval Engines), §10.5 (Risk Engine), §12 (Policy Framework), §13 (Risk Assessment) |
| Prerequisites | WP-006 |
| Included components | The full six-step Validation Pipeline (§14.2); Global/Project/User/Runtime Policy tiers (Emergency deferred to WP-013); Constitution §0.6.1's risk formula; Constitution §0.6's Decision Matrix executed mechanically. |
| Explicitly excluded | Emergency Policies and Emergency Shutdown (WP-013); Trust Evaluation and Safety Gates as named components (folded into this WP's Risk/Approval Engines rather than built as separate later work, since the frozen specification does not require them to be sequenced separately); all eleven Protection Domains' specific per-domain logic (WP-013). |
| Expected deliverables | A real `validate()` implementing the full tiered pipeline, replacing WP-006's minimal version, with zero regression in the vertical slice. |
| Projects affected | `EOS.Gates` |
| Estimated implementation effort | 6–7 days |
| Estimated code size | ~1100 lines |
| Build verification | `dotnet build` clean |
| Test verification | Unit tests per engine; a full-pipeline integration test exercising Low/Medium/High tiers; regression run of WP-009 |
| Demo / acceptance criteria | A test High-tier action is correctly deferred for approval; a test Low-tier action is allowed with async-only logging; vertical slice regression passes |
| Git commit scope | One PR: full Policy/Rule/Risk/Approval Engines, replacing WP-006's minimal gate |
| Dependencies on previous WPs | WP-006 |

### WP-013 — Protection Layer: Governance Domains, Resource Ceilings & Emergency Shutdown

| Field | Value |
|---|---|
| Objective | Implement the eleven Protection Domains' specific logic, Resource Protection ceiling enforcement, and Emergency Shutdown. |
| Why this exists | Later milestones' Knowledge, Memory, Planning, and Resource actions each need their specific Protection Domain logic active; Emergency Shutdown is the system-wide safety net every later milestone assumes exists. |
| Architecture documents | `Protection-Layer-Specification-v1.0.md` |
| Related sections | §11 (Protection Domains), §16 (Resource Protection), §26.1 (Emergency Shutdown) |
| Prerequisites | WP-012 |
| Included components | Per-domain policy checks for Knowledge, Memory, Reasoning, Learning, Planning, AI Providers, Configuration, Resources (Local Files/Projects/System Settings implemented at a minimal governance level, sufficient for the domains actually exercised by this point in the roadmap); Resource ceiling checks (structurally ready, consuming Resource Management's values once WP-021 exists — a stub value until then); Emergency Shutdown activation/clearing. |
| Explicitly excluded | Full Resource Management integration (values are stubbed/conservative until WP-021); Cross-Source Poisoning Signal and Longitudinal Reasoning Accuracy Audit logic (WP-027, since they require Learning Engine and real Reasoning KPI history to be meaningful). |
| Expected deliverables | Emergency Shutdown demonstrably halts new dispatch and is clearable only by a simulated L4-authorized action. |
| Projects affected | `EOS.Gates` |
| Estimated implementation effort | 4–5 days |
| Estimated code size | ~800 lines |
| Build verification | `dotnet build` clean |
| Test verification | Unit tests per domain; an integration test activating and clearing Emergency Shutdown |
| Demo / acceptance criteria | Triggering a simulated Emergency condition halts new action dispatch; clearing it (with justification) resumes normal operation |
| Git commit scope | One PR: domain logic + resource ceiling stubs + Emergency Shutdown |
| Dependencies on previous WPs | WP-012 |
## Milestone 4 — Memory & Knowledge Management

### Goal
Replace WP-007's minimal write path with the full seven-memory-type architecture, and implement the entire Knowledge Management layer on top of it.

### Why This Milestone Exists
Reasoning Engine's full pipeline (Milestone 5) and Planning & Execution Engine (Milestone 6) both depend on real Context Assembly, real retrieval, and real Knowledge Management taxonomy/governance — none of which the vertical slice needed, but all of which real autonomous behavior does.

### Work Packages Included
WP-014, WP-015, WP-016, WP-017, WP-018

### Expected Outcome
The full `IKnowledgeClient` and `IKnowledgeManagementClient` interfaces are real; retrieval, ranking, consolidation, compression, expiration, taxonomy, relationships, quality, governance, and freshness all function against real data.

### Validation Checklist
- [ ] `assemble_context()` returns a real, budgeted, ranked payload
- [ ] Consolidation correctly promotes ephemeral memory to Episodic Memory and emits `LessonLearned`
- [ ] Knowledge Management's additive ranking pass runs after, never instead of, Memory's own ranking
- [ ] A duplicate-content scenario is correctly flagged (not auto-merged)

### Exit Criteria
No regression in the Milestone 2/3 vertical slice; a manual test Lesson can be created, consolidated, and classified end to end.

---

### WP-014 — Memory: Full Retrieval Strategy & Mechanical Ranking

| Field | Value |
|---|---|
| Objective | Implement the full seven memory-type Storage Strategy, hybrid symbolic+vector Retrieval Strategy, and the mechanical Retrieval Ranking formula. |
| Why this exists | WP-007's single write path is insufficient for any subsystem that needs to *find* relevant existing knowledge, which every subsystem from Reasoning onward does. |
| Architecture documents | `Memory-Management-Specification-v1.0.md` |
| Related sections | §10 (Memory Types), §12 (Storage Strategy), §13 (Retrieval Strategy), §19 (Retrieval Ranking) |
| Prerequisites | WP-007, WP-011 (embedding channel) |
| Included components | All seven memory types (Working, Short-term, Long-term, Episodic, Semantic, Project, Session) mapped to their Constitution Part 4 stores (Redis, SQLite, SQL Server, ChromaDB via `EOS.VectorStore`); the two-stage symbolic+vector retrieval algorithm; the four-weight mechanical ranking formula. |
| Explicitly excluded | Context Assembly's budget/truncation logic (WP-015); Consolidation/Compression/Expiration (WP-015/WP-016). |
| Expected deliverables | `IKnowledgeClient.query()`/`.query_similar()` return real, ranked results against real stored content. |
| Projects affected | `EOS.Knowledge`, `EOS.KnowledgeGraph`, `EOS.VectorStore` |
| Estimated implementation effort | 6–7 days |
| Estimated code size | ~1100 lines |
| Build verification | `dotnet build` clean |
| Test verification | Unit tests per memory type's storage mapping; an integration test seeding real content and confirming ranked retrieval order matches the formula's expected weighting |
| Demo / acceptance criteria | A seeded set of test knowledge items returns in the expected rank order for a sample query |
| Git commit scope | One PR: seven memory types + retrieval + ranking |
| Dependencies on previous WPs | WP-007, WP-011 |

### WP-015 — Memory: Context Assembly & Consolidation

| Field | Value |
|---|---|
| Objective | Implement `assemble_context()`'s budgeted composition logic and `consolidate()`'s ephemeral-to-persistent promotion. |
| Why this exists | Reasoning Engine's full pipeline (Milestone 5) requires real, budgeted context; the entire Learning Flow (Milestone 6) begins at `consolidate()` emitting `LessonLearned`. |
| Architecture documents | `Memory-Management-Specification-v1.0.md` |
| Related sections | §15 (Context Assembly), §16 (Memory Consolidation) |
| Prerequisites | WP-014 |
| Included components | The budgeted assembly algorithm (§15.1) with truncation transparency; the four consolidation triggers (§16.1) and the consolidation algorithm (§16.2) emitting `LessonLearned`. |
| Explicitly excluded | Compression and Expiration (WP-016); Knowledge Management's additive ranking pass (WP-018, since it depends on Knowledge Management existing first). |
| Expected deliverables | A working `assemble_context()` respecting a caller-specified budget; a working `consolidate()` producing a real Episodic Memory entry and a real `LessonLearned` event. |
| Projects affected | `EOS.Knowledge` |
| Estimated implementation effort | 4–5 days |
| Estimated code size | ~700 lines |
| Build verification | `dotnet build` clean |
| Test verification | Unit tests for budget enforcement and truncation flagging; an integration test confirming `consolidate()` emits a real, subscribable `LessonLearned` event |
| Demo / acceptance criteria | A manually-triggered consolidation produces a real Episodic Memory row and a real event visible on the event backbone (WP-003) |
| Git commit scope | One PR: Context Assembly + Consolidation |
| Dependencies on previous WPs | WP-014 |

### WP-016 — Memory: Compression & Expiration Lifecycle

| Field | Value |
|---|---|
| Objective | Implement Memory Compression and Expiration, completing the full memory lifecycle. |
| Why this exists | Without this, storage grows unbounded — a real operational concern even at this early stage of the roadmap, not a speculative future one. |
| Architecture documents | `Memory-Management-Specification-v1.0.md` |
| Related sections | §17 (Memory Compression), §18 (Memory Expiration) |
| Prerequisites | WP-015 |
| Included components | The compression eligibility check and summarization call (via `EOS.Reasoning`'s `summarize()`, WP-020 — this WP stubs that call until WP-020 exists, then is revisited); per-memory-type expiration rules; archival to the Artifact Registry pattern. |
| Explicitly excluded | The actual `summarize()` implementation (WP-020, this WP only calls it); Knowledge Management's Archiving lifecycle-state tagging (WP-018, layered on top of this WP's mechanism). |
| Expected deliverables | A Sprint-cycle-boundary sweep that correctly identifies and compresses eligible Episodic entries, and expires ephemeral memory types on schedule. |
| Projects affected | `EOS.Knowledge` |
| Estimated implementation effort | 3–4 days |
| Estimated code size | ~550 lines |
| Build verification | `dotnet build` clean |
| Test verification | Unit tests for eligibility rules; an integration test confirming Working/Short-term Memory expires on schedule without manual intervention |
| Demo / acceptance criteria | A test entry marked eligible for compression is correctly compressed with its original content archived, not deleted |
| Git commit scope | One PR: Compression + Expiration sweep |
| Dependencies on previous WPs | WP-015 |

### WP-017 — Knowledge Management: Taxonomy & Relationships

| Field | Value |
|---|---|
| Objective | Implement the Knowledge Type taxonomy and the nine Relationship types, stored as metadata on Memory's existing `KnowledgeNode`. |
| Why this exists | This is the first Knowledge Management capability, and the foundation every later Knowledge Management WP builds on. |
| Architecture documents | `Knowledge-Management-Specification-v1.0.md` |
| Related sections | §10.6 (Taxonomy/Relationship Manager), §11 (Knowledge Types), §14 (Knowledge Relationships) |
| Prerequisites | WP-014 |
| Included components | `IKnowledgeManagementClient.classify()`/`navigate_relationships()`; the full taxonomy table (§11); all nine relationship types with their Ontology constraints (§10.7). |
| Explicitly excluded | Quality/Governance/Freshness/Reuse (WP-018); the additive search ranking pass (WP-018). |
| Expected deliverables | A working `classify()` correctly tagging a test node's taxonomy; a working `navigate_relationships()` traversing a test relationship graph. |
| Projects affected | `EOS.Knowledge` (Knowledge Management concern) |
| Estimated implementation effort | 3–4 days |
| Estimated code size | ~600 lines |
| Build verification | `dotnet build` clean |
| Test verification | Unit tests per relationship type's Ontology constraint; an integration test creating and navigating a small relationship graph |
| Demo / acceptance criteria | A test Fact and a test Pattern are correctly classified and connected via a `Supports` relationship, then navigable via `navigate_relationships()` |
| Git commit scope | One PR: Taxonomy Manager + Relationship Manager |
| Dependencies on previous WPs | WP-014 |

### WP-018 — Knowledge Management: Quality, Governance, Freshness & Reuse

| Field | Value |
|---|---|
| Objective | Implement the Quality Profile, Governance actions (Protection-gated), Freshness scoring, and Discovery/Reuse (including the additive search ranking pass). |
| Why this exists | Completes Knowledge Management; the additive ranking pass is the mechanism Planning & Execution Engine (Milestone 6) uses for reusable-pattern search. |
| Architecture documents | `Knowledge-Management-Specification-v1.0.md` |
| Related sections | §13 (Knowledge Quality), §16 (Knowledge Governance), §17 (Knowledge Freshness), §18 (Knowledge Reuse), §15.7 (additive ranking pass) |
| Prerequisites | WP-017, WP-013 (Protection governance-action gating) |
| Included components | The `QualityProfile` aggregate; `request_governance_action()` routed through `IProtectionClient.validate()`; Freshness scoring and drift detection; structural (non-semantic) Duplicate Detection; the additive quality/relationship-aware ranking pass (`search()`). |
| Explicitly excluded | Semantic similarity delegation to Reasoning's `compare()` (WP-020, this WP stubs that call structurally until WP-020 exists). |
| Expected deliverables | A working `search()` that calls Memory's retrieval (WP-014) then applies the additive ranking pass; a working, Protection-gated `request_governance_action()`. |
| Projects affected | `EOS.Knowledge` (Knowledge Management concern) |
| Estimated implementation effort | 4–5 days |
| Estimated code size | ~750 lines |
| Build verification | `dotnet build` clean |
| Test verification | Unit tests for the ranking formula's independent weighting; an integration test confirming a governance action is denied when Protection returns Deny |
| Demo / acceptance criteria | `search()` returns Memory's results re-ranked by quality/relationship signals, with a stable sort confirmed on tied inputs |
| Git commit scope | One PR: Quality/Governance/Freshness/Reuse + additive ranking |
| Dependencies on previous WPs | WP-017, WP-013 |
## Milestone 5 — Reasoning & Resource Management

### Goal
Replace WP-008's minimal Reasoning pipeline with the full 12-stage, 13-reasoning-type architecture, and implement Resource Management from scratch (it had no minimal-slice equivalent, since Milestone 2 used hardcoded conservative budgets).

### Why This Milestone Exists
Planning & Execution Engine and Learning Engine (Milestone 6) both need the full Reasoning Engine (bounded delegation, `compare()`, `get_trust_signal()`) and real Resource Management budget values, not Milestone 2's placeholders.

### Work Packages Included
WP-019, WP-020, WP-021, WP-022

### Expected Outcome
`IReasoningEngineClient`'s full interface is real; `IResourceManagementClient` publishes real, measured budget values consumed by Protection (retrofitting WP-013's stub) and, later, Planning.

### Validation Checklist
- [ ] All 13 reasoning types are selectable and produce type-appropriate pipeline behavior
- [ ] `compare()` and `get_trust_signal()` are ready for Learning Engine's consumption (Milestone 6)
- [ ] Resource Monitor produces real, recorded CPU/RAM/Disk measurements
- [ ] Protection's Resource Protection ceiling (WP-013) now consumes real values instead of its stub

### Exit Criteria
No regression in the full vertical slice; Resource Management's own KPIs are computable from real measurement, not estimate.

---

### WP-019 — Reasoning Engine: Full 12-Stage Pipeline & Reasoning Types

| Field | Value |
|---|---|
| Objective | Extend WP-008's minimal pipeline to the full 12 stages and all 13 reasoning types. |
| Why this exists | Learning Engine's Comparative Reasoning use and Planning & Execution Engine's Architectural/Optimization/Strategic Reasoning use (Milestone 6) both require reasoning-type-specific pipeline behavior the minimal slice never implemented. |
| Architecture documents | `Reasoning-Engine-Specification-v1.0.md` |
| Related sections | §10 (all 12 stages), §11 (13 reasoning types), §10.2 (pipeline invocation by entry point) |
| Prerequisites | WP-008, WP-014 (full Memory retrieval, since richer Context Processing needs it) |
| Included components | Intent Analysis, Constraint Evaluation, multi-hypothesis Generation, Alternative Exploration, Trade-off Analysis (the stages WP-008 skipped); all 13 reasoning types' distinct pipeline-stage emphasis; Context Expansion/Reduction/Filtering/Prioritization (§12 of that spec). |
| Explicitly excluded | `compare()`, `get_trust_signal()`, `summarize()`, `query_history()` (WP-020); Decision Ranking for near-tied hypotheses (included here as it's part of Stage 7, but the caller-facing ranked-list API surface is finalized in WP-020 alongside the other entry points). |
| Expected deliverables | `reason()` now exercises the full pipeline and correctly selects a reasoning type based on request shape. |
| Projects affected | `EOS.Reasoning` |
| Estimated implementation effort | 6–7 days |
| Estimated code size | ~1100 lines |
| Build verification | `dotnet build` clean |
| Test verification | Unit tests per stage and per reasoning type; regression run of the vertical slice and Milestone 4 integration tests |
| Demo / acceptance criteria | A Diagnostic Reasoning request and a Rule-Based Reasoning request produce visibly different pipeline emphasis (confirmed via logged stage weighting) for the same underlying question shape |
| Git commit scope | One PR: full pipeline stages + reasoning type selection |
| Dependencies on previous WPs | WP-008, WP-014 |

### WP-020 — Reasoning Engine: compare(), get_trust_signal(), summarize() & Explainability Depth

| Field | Value |
|---|---|
| Objective | Implement the three narrow entry points Learning Engine and Memory Management already depend on by contract, plus full Explainability and Decision History. |
| Why this exists | Learning Engine (Milestone 6) cannot begin without `compare()`/`get_trust_signal()`; Memory's Compression sweep (WP-016) is stubbed until `summarize()` is real. |
| Architecture documents | `Reasoning-Engine-Specification-v1.0.md` |
| Related sections | §14.1/§14.2 (`compare`/`get_trust_signal` contracts), §16.1 (`summarize`, `query_history`), §13.7 (Decision History), §14 (Explainability) |
| Prerequisites | WP-019 |
| Included components | `compare()` and `get_trust_signal()` with their exact, already-ratified preconditions/postconditions/failure contracts; `summarize()` completing WP-016's stubbed call; `query_history()` as a read-only projection. |
| Explicitly excluded | Nothing further — this WP completes `IReasoningEngineClient` in full. |
| Expected deliverables | All five `IReasoningEngineClient` methods fully functional; WP-016's Compression sweep now calls a real `summarize()`. |
| Projects affected | `EOS.Reasoning` |
| Estimated implementation effort | 4–5 days |
| Estimated code size | ~700 lines |
| Build verification | `dotnet build` clean |
| Test verification | Contract tests for `compare()`/`get_trust_signal()` against their exact published pre/postconditions; regression run of WP-016's Compression sweep now using the real `summarize()` |
| Demo / acceptance criteria | Two similar test Lessons produce a high `compare()` similarity score; WP-016's Compression demo now produces a real, model-generated summary instead of a stub |
| Git commit scope | One PR: `compare`/`get_trust_signal`/`summarize`/`query_history` + Decision History |
| Dependencies on previous WPs | WP-019 |

### WP-021 — Resource Management: Monitor & Capacity Planning

| Field | Value |
|---|---|
| Objective | Implement real CPU/RAM/Disk/Model/Queue/Background/Cache measurement and the Safe/Warning/Critical/Emergency threshold computation. |
| Why this exists | Every subsequent milestone's dispatch/gating decisions should consume real measured capacity, not WP-013's stubbed conservative value. |
| Architecture documents | `Resource-Management-Specification-v1.0.md` |
| Related sections | §10.2–§10.3 (Resource Monitor, Capacity Manager), §17 (Capacity Planning), §18 (Monitoring) |
| Prerequisites | WP-004 |
| Included components | Sampling-based measurement for all seven monitored dimensions; the four-tier threshold computation; `IResourceManagementClient.get_current_budget()`/`get_current_tier()`. |
| Explicitly excluded | Quota Manager and Background Task Controller (WP-022); Model Residency Management (WP-022); Allocation Manager's GPU-extensibility registry (built here structurally per ADR-RM003, but no GPU type is registered — none exists on the target hardware). |
| Expected deliverables | `get_current_budget()` returns real, measured values; Protection's Resource Protection (WP-013) is updated to consume these instead of its stub. |
| Projects affected | `EOS.Resources` (new project, first real code) |
| Estimated implementation effort | 5–6 days |
| Estimated code size | ~900 lines |
| Build verification | `dotnet build` clean |
| Test verification | Unit tests for threshold computation; an integration test recording real measurements against the running Docker Compose stack and comparing them to the Infrastructure Roadmap's Phase 3/5 baseline figures |
| Demo / acceptance criteria | `get_current_budget(CPU)` returns a value derived from a live measurement, not a hardcoded constant; Protection's ceiling check now cites this real value in its logs |
| Git commit scope | One PR: `EOS.Resources` project + Monitor + Capacity Manager, plus the WP-013 stub replacement |
| Dependencies on previous WPs | WP-004, WP-013 (for the stub-replacement half) |

### WP-022 — Resource Management: Quotas, Background Task Controller & Model Residency

| Field | Value |
|---|---|
| Objective | Implement fair-share Quotas, the live Background Task Controller gate, and Model Residency Management. |
| Why this exists | Learning Engine's sweeps (Milestone 6) and Memory's sweeps (already running since WP-016) need real resource-class-aware gating instead of running unthrottled; AI Provider Layer's routing (WP-010) benefits from real residency signals. |
| Architecture documents | `Resource-Management-Specification-v1.0.md` |
| Related sections | §10.4 (Quota Manager), §10.6 (Background Task Controller), §14 (Model Resource Management), §16 (Resource Prioritization), §19 (Resource Policies) |
| Prerequisites | WP-021 |
| Included components | The five-tier resource-class hierarchy; `request_background_slot()`; Model Loading/Unloading/Residency tracking, feeding AI Provider Layer's routing as a Resource Availability input (retrofit to WP-010). |
| Explicitly excluded | GPU resource type registration (no hardware to register against); domain-specific tuning (flagged as future work by the frozen specification itself). |
| Expected deliverables | WP-016's Compression sweep and any Learning Engine sweep (Milestone 6) now request a slot before running, and are correctly deferred under simulated contention. |
| Projects affected | `EOS.Resources` |
| Estimated implementation effort | 4–5 days |
| Estimated code size | ~750 lines |
| Build verification | `dotnet build` clean |
| Test verification | Unit tests for quota fairness and Starvation Prevention; an integration test simulating CPU contention and confirming a background job is correctly deferred then later granted |
| Demo / acceptance criteria | Under simulated high CPU load, a test background job request is deferred; once load subsides, it is granted automatically without manual intervention |
| Git commit scope | One PR: Quota Manager + Background Task Controller + Model Residency |
| Dependencies on previous WPs | WP-021 |
## Milestone 6 — Planning, Execution & Learning

### Goal
Implement Planning & Execution Engine and Learning Engine in full — the last two cognitive subsystems, both depending on every subsystem built so far.

### Why This Milestone Exists
This is the milestone where EOS gains its first real Goal-to-execution capability and its first real capacity to learn from outcomes — the two capabilities that make it an "engineering operating system" rather than a request/response tool.

### Work Packages Included
WP-023, WP-024, WP-025, WP-026, WP-027

### Expected Outcome
A submitted Goal is decomposed into a Task Graph, scheduled, executed under real Protection gating, and its outcome flows into a real Learning Engine pipeline reaching at least the Pattern stage in a demo scenario.

### Validation Checklist
- [ ] A test Goal reaches `Completed` through the full Planning & Execution Engine pipeline
- [ ] Every dispatch passes through `IProtectionClient.validate()` using WP-012/013's full implementation
- [ ] A test failure triggers Retry then Rollback then Recovery Planning correctly
- [ ] A test Lesson reaches Pattern promotion via `compare()`

### Exit Criteria
No regression in any prior milestone's demo; the Learning Feedback Flow (System Architecture Specification §13) is demonstrated end to end at least once.

---

### WP-023 — Planning & Execution Engine: Goal Management & Task Graph Builder

| Field | Value |
|---|---|
| Objective | Implement Goal lifecycle, Goal hierarchy/dependencies, and the Task Graph Builder (including bounded Reasoning delegation and Memory pattern consultation). |
| Why this exists | This is the entry point for all Goal-driven work — nothing in this milestone functions without it. |
| Architecture documents | `Planning-Execution-Engine-Specification-v1.0.md` |
| Related sections | §10.1a (Goal Manager), §10.3 (Task Graph Builder), §11 (Goal Management), §12.6 (reusable planning patterns), §10.11 (bounded Reasoning delegation) |
| Prerequisites | WP-018 (pattern search), WP-020 (bounded Reasoning delegation), WP-013 (Protection validation for Goal validation) |
| Included components | `IPlanningClient.submit_goal()`; the full Goal Lifecycle state machine; Goal Hierarchy/Dependencies; Task decomposition into a Task Graph; the single-bounded-judgment-call Reasoning delegation (never full plan generation, per ADR-PE003). |
| Explicitly excluded | Scheduler/Execution Coordinator (WP-024); Retry/Rollback/Dynamic Replanning (WP-025). |
| Expected deliverables | `submit_goal()` produces a real, validated Task Graph and a real `Plan` artifact. |
| Projects affected | `EOS.Planner` |
| Estimated implementation effort | 6–7 days |
| Estimated code size | ~1050 lines |
| Build verification | `dotnet build` clean |
| Test verification | Unit tests for Goal validation and decomposition; an integration test confirming the bounded Reasoning delegation never returns a full task graph (ADR-PE003 enforcement test) |
| Demo / acceptance criteria | A test Goal ("add a logging statement to module X") is decomposed into a sensible, schema-valid Task Graph, citing at least one reusable pattern from Knowledge Management |
| Git commit scope | One PR: Goal Manager + Task Graph Builder |
| Dependencies on previous WPs | WP-018, WP-020, WP-013 |

### WP-024 — Scheduler & Execution Coordinator

| Field | Value |
|---|---|
| Objective | Implement the Scheduler (Priority Queue, Dependency Graph, budget checks) and Execution Coordinator, replacing the vertical slice's bypass of Planning entirely. |
| Why this exists | Real Task dispatch — the mechanism by which anything actually executes in EOS — depends on this WP. |
| Architecture documents | `Planning-Execution-Engine-Specification-v1.0.md` |
| Related sections | §10.6 (Scheduler), §10.7 (Execution Coordinator), §14 (Scheduling), §25.1 (mandatory Protection gate) |
| Prerequisites | WP-023, WP-022 (real budget values), WP-013 (full Protection gate) |
| Included components | The full Scheduling Algorithm (Constitution Part 7 §7.3, now with real Resource Management-supplied budgets); all seven Scheduling modes (§14); the Execution Coordinator's mandatory `IProtectionClient.validate()` call before every `Ready → Running` transition. |
| Explicitly excluded | Retry/Rollback (WP-025); Progress Tracking beyond raw Task Lifecycle state (WP-025, ETA estimation specifically). |
| Expected deliverables | A Task from WP-023's Task Graph is actually dispatched, gated by real Protection, and transitions through the real Task Lifecycle. |
| Projects affected | `EOS.Orchestrator` |
| Estimated implementation effort | 5–6 days |
| Estimated code size | ~900 lines |
| Build verification | `dotnet build` clean |
| Test verification | Unit tests for each Scheduling mode; an integration test confirming a Task is denied dispatch when Protection returns Deny |
| Demo / acceptance criteria | WP-023's test Goal now actually executes its Task Graph, with each dispatch visibly gated (logged) by Protection |
| Git commit scope | One PR: Scheduler + Execution Coordinator |
| Dependencies on previous WPs | WP-023, WP-022, WP-013 |

### WP-025 — Retry, Rollback, Progress Tracking & Dynamic Replanning

| Field | Value |
|---|---|
| Objective | Implement the Retry Manager, Rollback Manager, Progress Monitor, and all four Dynamic Replanning triggers. |
| Why this exists | Completes Planning & Execution Engine; without this, any Task failure has no recovery path, which is unacceptable even for early demos. |
| Architecture documents | `Planning-Execution-Engine-Specification-v1.0.md` |
| Related sections | §10.8–§10.10 (Progress Monitor, Retry Manager, Rollback Manager), §16 (Dynamic Replanning), §18 (Progress Tracking), §19 (Failure Handling) |
| Prerequisites | WP-024 |
| Included components | Retry Rules/Timeout Rules; execution of the existing per-transition Rollback Path; Recovery Planning re-invoking the Planning Engine; all four replanning triggers (failure, new knowledge, resource change, user intervention); Task/Goal/Workflow status aggregation and ETA estimation. |
| Explicitly excluded | Workflow-level parallel/conditional branch orchestration beyond what a single Task Graph already requires for this milestone's demo scope (full Workflow Orchestration richness is exercised incrementally as real multi-step Goals are built in later product work, not required to complete this roadmap's 100%-of-architecture coverage, since the specification's own Workflow concept is fully implemented here — this exclusion refers only to *exercising* complex branching in a demo, not to *implementing* the mechanism). |
| Expected deliverables | A deliberately-failing test Task correctly retries, then rolls back, then triggers Recovery Planning and resumes. |
| Projects affected | `EOS.Orchestrator`, `EOS.Planner` |
| Estimated implementation effort | 5–6 days |
| Estimated code size | ~950 lines |
| Build verification | `dotnet build` clean |
| Test verification | An integration test forcing a Task to exhaust its retry ceiling and confirming Rollback + Recovery Planning both fire correctly |
| Demo / acceptance criteria | A deliberately-injected failure in a test Task Graph is retried, rolled back, and replanned automatically, ending in either a successful resumed execution or a clean, explained failure |
| Git commit scope | One PR: Retry/Rollback/Progress/Replanning |
| Dependencies on previous WPs | WP-024 |

### WP-026 — Learning Engine: Ingestion, Clustering & Pattern Promotion

| Field | Value |
|---|---|
| Objective | Implement Lesson ingestion (with the Ingestion Rate Guard), Cluster Trigger (via `compare()`), and promotion through Pattern. |
| Why this exists | This is where the Learning Feedback Flow actually begins — the first real demonstration that EOS learns from its own outcomes. |
| Architecture documents | `Learning-Engine-Specification-v1.1.md` |
| Related sections | §11.1–§11.2 (Ingestion, Cluster Trigger), §9 (`PipelineRecord`), §16 (State Machine, Lesson→Pattern edge) |
| Prerequisites | WP-015 (Consolidation emitting `LessonLearned`), WP-020 (`compare()`, `get_trust_signal()`) |
| Included components | `PipelineRecord` creation on `LessonLearned`; the Ingestion Rate Guard (Knowledge Poisoning defense); Confidence Guard using `compare()` and `get_trust_signal()`; promotion to Pattern on ≥3 corroborating matches. |
| Explicitly excluded | Promotion beyond Pattern (WP-027); ROI Gate, Fitness Functions, Quarantine-clearing workflow beyond the Quarantine *state* itself (WP-027). |
| Expected deliverables | A set of three deliberately-similar test Lessons correctly cluster and promote to a real Pattern. |
| Projects affected | `EOS.Learning` (new project, first real code) |
| Estimated implementation effort | 5–6 days |
| Estimated code size | ~900 lines |
| Build verification | `dotnet build` clean |
| Test verification | Unit tests for the Ingestion Rate Guard's anomaly threshold; an integration test with three similar test Lessons producing a real `LessonPromoted` event |
| Demo / acceptance criteria | Three manually-triggered, similar test outcomes (via WP-009's CLI or WP-023's Goal flow) consolidate into Lessons and promote to a Pattern, visible as a real Knowledge Graph node |
| Git commit scope | One PR: `EOS.Learning` project + Ingestion + Clustering + Pattern promotion |
| Dependencies on previous WPs | WP-015, WP-020 |

### WP-027 — Learning Engine: Full Pipeline, ROI Gate & Fitness Functions

| Field | Value |
|---|---|
| Objective | Complete the pipeline from Pattern through Platform Capability, implement the ROI Gate, Fitness Functions, and full Quarantine lifecycle. |
| Why this exists | Completes Learning Engine and closes the Learning Feedback Flow all the way to Automation — the point at which EOS's own accumulated knowledge starts feeding back into future planning (Milestone 7's Loop). |
| Architecture documents | `Learning-Engine-Specification-v1.1.md` |
| Related sections | §11.3 (ROI Gate), §11.5 (Feedback Loop Guard), §11.6 (Integrity Check), §16 (full state machine), §21 (Fitness Functions), §22 (Fitness Functions detail) |
| Prerequisites | WP-026 |
| Included components | Best Practice → Principle → Golden Path → Automation → Reusable Component → Platform Capability promotion, each Decision-Matrix-gated where the specification requires; the fail-closed ROI Gate; all six Fitness Functions; Quarantine clearing (Principal-Engineer-authority simulated); the Feedback Loop Guard and Integrity Checker. |
| Explicitly excluded | Nothing further — this WP completes Learning Engine's pipeline in full. |
| Expected deliverables | WP-026's test Pattern can be manually ratified through to Automation, at which point it is consumed by Planning & Execution Engine (WP-023) as a new reusable capability on the next test Goal. |
| Projects affected | `EOS.Learning` |
| Estimated implementation effort | 7–8 days |
| Estimated code size | ~1200 lines |
| Build verification | `dotnet build` clean |
| Test verification | Unit tests for the ROI Gate's fail-closed behavior on missing data; an integration test carrying a test Pattern through to Automation and confirming Planning & Execution Engine's Task Graph Builder cites it on a subsequent test Goal |
| Demo / acceptance criteria | A full, manually-walked demo: Lesson → Pattern (WP-026) → Best Practice → ... → Automation, then a new test Goal's decomposition visibly cites the new Automation as a reusable pattern |
| Git commit scope | One PR: full pipeline completion + ROI Gate + Fitness Functions |
| Dependencies on previous WPs | WP-026 |
## Milestone 7 — Autonomous Orchestration & Production Readiness

### Goal
Implement the Autonomous Engineering Loop's own genuinely new logic (Self-Evaluation, Operational Modes) over the now-complete eight subsystems, plus the minimal Dashboard/backup/hardening work needed for a real, usable development environment.

### Why This Milestone Exists
This is the final milestone — every cognitive, protective, and execution capability already exists from Milestones 1–6; this milestone only adds the sequencing/governance layer over them and the operational polish (observability, backup, hardening) a real environment needs.

### Work Packages Included
WP-028, WP-029, WP-030

### Expected Outcome
The full 18-step autonomous cycle runs under a selectable Operational Mode; basic observability exists via a minimal Dashboard; backups are automated and tested; the environment has undergone a documented security/performance hardening pass.

### Validation Checklist
- [ ] A trigger source (e.g., a manual request) drives a full loop iteration through all applicable steps
- [ ] An Operational Mode change is itself Protection-gated and cannot self-escalate
- [ ] Dashboard renders at least Task/Goal status and recent events
- [ ] A full backup/restore cycle has been tested at least once against the complete, now-populated data stores

### Exit Criteria
All 30 Work Packages complete; the full Architecture Traceability Matrix (below) shows zero unmapped items; three consecutive clean end-to-end demo runs, including one with networking disconnected.

---

### WP-028 — Autonomous Engineering Loop: Core Cycle & Trigger Sources

| Field | Value |
|---|---|
| Objective | Implement the 18-step cycle sequencing and the nine Trigger Sources (with the File/Git Event gap explicitly left unimplemented and documented, per the frozen specification's own acknowledgment). |
| Why this exists | This is the mechanism that turns eight independently-capable subsystems into one continuously-operating autonomous system. |
| Architecture documents | `Autonomous-Engineering-Loop-Specification-v1.0.md` |
| Related sections | §7 (Autonomous Loop Overview), §8 (Trigger Sources), §9–§12 (Observation/Decision/Execution/Learning Phase sequencing) |
| Prerequisites | WP-027 (all eight subsystems complete) |
| Included components | The Loop Controller sequencing all 18 steps by citation into the already-real subsystems; Trigger Manager for the eight implementable trigger sources (User Requests, Scheduled Tasks, Learning Opportunities, Knowledge Updates, Performance Degradation, Failures, Manual Requests — File Changes/Git Events excluded per below). |
| Explicitly excluded | File Changes/Git Events (no owning subsystem exists for change detection, per Autonomous-Engineering-Loop-Specification-v1.0 §8.9's own acknowledgment — this WP does not invent an owner, consistent with "do not redesign the architecture"); Self-Evaluation/Improve (WP-029); Operational Modes (WP-029). |
| Expected deliverables | A manual trigger drives a full, real 18-step iteration (minus steps that structurally don't apply per §7.3, e.g. a pure query skipping Plan/Execute). |
| Projects affected | `EOS.Orchestrator` |
| Estimated implementation effort | 5–6 days |
| Estimated code size | ~850 lines |
| Build verification | `dotnet build` clean |
| Test verification | An end-to-end integration test driving one full iteration from a User Request trigger through Learning Phase completion |
| Demo / acceptance criteria | Submitting a test Goal via the Loop's entry point results in a fully-logged, traceable 18-step iteration (or the correct subset, per §7.3) ending in `LoopIterationCompleted` |
| Git commit scope | One PR: Loop Controller + Trigger Manager |
| Dependencies on previous WPs | WP-027 |

### WP-029 — Autonomous Engineering Loop: Operational Modes, Human Governance & Self-Evaluation

| Field | Value |
|---|---|
| Objective | Implement the eight Operational Modes (as Protection Runtime Policy selections), Human Governance checkpoints, and the Self-Evaluation/Improve steps. |
| Why this exists | Completes the Loop's own two genuinely new mechanisms and the human-governance guarantee the Architecture Rules require above all else. |
| Architecture documents | `Autonomous-Engineering-Loop-Specification-v1.0.md` |
| Related sections | §13 (Continuous Improvement), §14 (Human Governance), §22 (Operational Modes) |
| Prerequisites | WP-028, WP-012 (Protection's Policy Engine, for Runtime Policy selection) |
| Included components | `ILoopControlClient.set_operational_mode()`, Protection-gated per §22.9; all eight modes mapped to Runtime Policy profiles or Resource Management resource-class adjustments; Human Governance approval-checkpoint insertion; the Self-Evaluation KPI aggregation and Improve scheduling action. |
| Explicitly excluded | Nothing further for the Loop itself — this WP completes the Autonomous Engineering Loop Specification's scope in full. |
| Expected deliverables | A mode change request routes through Protection and cannot self-escalate; a full iteration produces a real `loop_health_score`. |
| Projects affected | `EOS.Orchestrator` |
| Estimated implementation effort | 5–6 days |
| Estimated code size | ~900 lines |
| Build verification | `dotnet build` clean |
| Test verification | A test confirming a mode-change request is denied when Protection returns Deny (no self-escalation); an integration test computing a real `loop_health_score` from live subsystem KPIs |
| Demo / acceptance criteria | Switching from Assisted to Autonomous Mode requires and receives a simulated approval; a completed iteration's `loop_health_score` is visible and traceable to its five component KPIs |
| Git commit scope | One PR: Operational Modes + Human Governance + Self-Evaluation/Improve |
| Dependencies on previous WPs | WP-028, WP-012 |

### WP-030 — Production Readiness: Dashboard, Backup Automation & Hardening Pass

| Field | Value |
|---|---|
| Objective | Implement a minimal `EOS.Dashboard` read-projection view (to the extent the Constitution actually specifies it — no invented design), automate the backup/restore procedure from the Infrastructure Roadmap's Phase 8, and perform a documented security/performance hardening pass. |
| Why this exists | Closes the Architecture Validation Report's Dashboard gap at the minimal level the frozen architecture actually supports, and converts the Infrastructure Roadmap's manual backup procedure into an automated, tested one — both genuine production-readiness needs, neither a new architectural capability. |
| Architecture documents | `EOS-Specification.md` (§0.11, Dashboards), `Architecture-Validation-Report-v1.0.md` (§17.2, Blocker #4, partial closure) |
| Related sections | Constitution §0.11 ("Dashboards are read-only projections... read exclusively from Contracts, never implementation") |
| Prerequisites | All prior WPs (Dashboard renders their events) |
| Included components | A minimal web or CLI-based Dashboard rendering Task/Goal status, recent events, and KPI values, strictly as read-only projections per Constitution §0.11 and Part 2's R-04 fitness rule; a cron-automated version of the Infrastructure Roadmap's backup procedure with a tested restore; a documented pass confirming every risk-bearing path still routes through Protection, every offline guarantee still holds with networking disabled, and every KPI identified as computable in the Architecture Validation Report actually computes from live data. |
| Explicitly excluded | `EOS.Pipeline` (CI/CD) implementation — the Architecture Validation Report identified this as needing its own lightweight specification before implementation, which is out of scope for a roadmap that must not "redesign the architecture"; any Dashboard feature beyond what Constitution §0.11's one paragraph actually specifies. |
| Expected deliverables | A working Dashboard view; an automated, tested daily backup; a written hardening-pass report confirming no regression against any prior milestone's acceptance criteria. |
| Projects affected | `EOS.Dashboard`, `EOS.Web` (or a CLI equivalent), deployment scripts under `deploy/` |
| Estimated implementation effort | 5–6 days |
| Estimated code size | ~800 lines |
| Build verification | `dotnet build` clean across the full solution |
| Test verification | A full backup/restore cycle test against the complete, now-populated data stores; a full regression suite run across all 30 Work Packages' own test verification steps |
| Demo / acceptance criteria | The Dashboard displays a live view of a running demo iteration; a backup taken today is successfully restored into a clean environment and the restored system reaches Ready and reproduces the same Dashboard view |
| Git commit scope | One PR: Dashboard + backup automation + hardening report |
| Dependencies on previous WPs | All prior WPs |
## Work Package Dependency Graph

```
WP-001 (Solution Skeleton)
   |
   +--> WP-002 (Config/Bootstrap)
   +--> WP-003 (Event Backbone) --+
   +--> WP-004 (Data Stores) -----+---> WP-005 (AI Provider: single adapter)
                                  |         |
                                  |         v
                                  +---> WP-006 (Protection: minimal gate)
                                  |         |
                                  +---> WP-007 (Memory: minimal write) |
                                            |                          |
                                            v                          v
                                        WP-008 (Reasoning: minimal) <--+
                                            |
                                            v
                                        WP-009 (First Vertical Slice) [needs WP-005,006,007,008]
                                            |
        +-------------------+--------------+-------------------+
        v                   v                                  v
   WP-010 (AIP: full)   WP-011 (AIP: embed)              WP-012 (Protection: engines)
        |                   |                                  |
        |                   |                                  v
        |                   |                             WP-013 (Protection: domains/emergency)
        |                   |                                  |
        v                   v                                  |
   WP-014 (Memory: retrieval/ranking) <--------------------------+
        |
        +--> WP-015 (Memory: context/consolidation)
        |         |
        |         +--> WP-016 (Memory: compression/expiration) [stub summarize until WP-020]
        |
        +--> WP-017 (KM: taxonomy/relationships)
                  |
                  +--> WP-018 (KM: quality/governance/reuse) [needs WP-013]
        |
        v
   WP-019 (Reasoning: full pipeline) [needs WP-008, WP-014]
        |
        v
   WP-020 (Reasoning: compare/trust/summarize) --> retrofits WP-016
        |
        v
   WP-021 (Resources: monitor/capacity) [needs WP-004] --> retrofits WP-013
        |
        v
   WP-022 (Resources: quota/background/residency) --> retrofits WP-010
        |
        +----------------------+-------------------+
        v                      v                    v
   WP-023 (Planning: Goal/TaskGraph) [needs WP-018, WP-020, WP-013]
        |
        v
   WP-024 (Scheduler/ExecutionCoordinator) [needs WP-023, WP-022, WP-013]
        |
        v
   WP-025 (Retry/Rollback/Replanning)
        |
        v
   WP-026 (Learning: ingestion/clustering) [needs WP-015, WP-020]
        |
        v
   WP-027 (Learning: full pipeline/ROI/Fitness)
        |
        v
   WP-028 (Loop: core cycle/triggers) [needs all of WP-001..027]
        |
        v
   WP-029 (Loop: modes/governance/self-eval) [needs WP-028, WP-012]
        |
        v
   WP-030 (Dashboard/backup/hardening) [needs all prior WPs]
```

## Dependency Validation

- **No Work Package depends on a future Work Package.** Verified by construction of the graph above: every arrow points from a lower-numbered WP to a higher-numbered one, with the sole exception of documented "retrofit" edges (WP-020→WP-016, WP-021→WP-013, WP-022→WP-010), each of which is a later WP *replacing a stub* in an earlier WP's already-merged code — a standard, planned refactor pattern, not a forward dependency the earlier WP's own completion relied upon. Each stub point is explicitly named in the "Explicitly excluded" field of the WP that created it.
- **Every dependency is satisfied at the point it is needed.** Cross-checked against each WP's own "Prerequisites" and "Dependencies on previous Work Packages" fields — no WP lists a prerequisite that appears later in the sequence than itself.
- **Every architecture component is implemented exactly once.** Verified in the Architecture Traceability Matrix below — no component appears in two WPs' "Included components" fields (retrofits are edits to existing code, not re-implementations).
- **No duplicated implementation exists.** Same verification as above.
- **No missing implementation exists**, with two explicitly acknowledged and justified exceptions: File System/Git Event trigger detection (no owning subsystem in the frozen architecture, per Autonomous-Engineering-Loop-Specification-v1.0 §8.9 — WP-028 does not invent one) and `EOS.Pipeline` (CI/CD, out of scope per the Architecture Validation Report's own recommendation that it receive a dedicated lightweight specification before implementation, which this roadmap — bound by "do not redesign the architecture" — cannot itself author).
## Architecture Traceability Matrix

Every item below maps to exactly one Work Package. Items marked **(gap)** are explicitly unmapped, per the Dependency Validation section above, and are reported rather than silently omitted.

### EOS-Specification.md (Constitution)

| Item | Type | Mapped WP |
|---|---|---|
| Physical Repository Architecture (all 33 projects) | Components | WP-001 |
| Module Dependency Rules / Fitness Rules R-00–R-09 | Governance | WP-001 (structural), spot-checked throughout |
| Event Catalog envelope & versioning | Infrastructure | WP-003 |
| All Constitution-Part-3 events (`TaskCreated`...`ReleaseApproved`) | Events | WP-024/WP-025 (Task events), WP-012 (`ReleaseApproved` consumption) |
| Data Architecture (store ownership) | Services | WP-004, WP-014 |
| Agent Communication Architecture (in-process transport) | Services | WP-003 |
| Task Lifecycle (Part 6) | Responsibility | WP-024, WP-025 |
| Scheduler (Part 7) | Responsibility | WP-024 |
| Artifact Registry (Part 8) | Services | WP-004 (foundation), used throughout |
| Prompt Management (Part 9) | Responsibility | Not separately implemented as a WP — prompts are authored inline where each subsystem needs them (Reasoning Engine, WP-019/020); no dedicated Prompt Registry UI is in scope for this roadmap's 30 WPs. **(gap, minor)** — flagged for a future WP if prompt volume grows enough to need dedicated tooling. |
| Configuration Strategy (Part 10) | Services | WP-002 |
| EOS SDK (Part 11) | Components | WP-001 (base), extended incrementally by every WP needing Logging/Retry/etc. |
| Bootstrap System (Part 12) | Responsibility | WP-002 |
| Disaster Recovery Testing (Part 13) | Responsibility | WP-030 |
| Meta Learning (Part 14) | Responsibility | WP-026, WP-027 |
| Flutter Mobile Engineering Platform (Part 15) | Domain | Out of scope — no mobile client is part of this roadmap's 30 WPs; `domain_tags` mechanism itself (used for Mobile equality) is implemented generically in WP-014/WP-017 and is mobile-ready without mobile-specific code. |
| Decision Matrix / Risk Scoring (§0.6/§0.6.1) | Governance | WP-012 |
| Quality Gates (§0.8) | Governance | WP-012, WP-013 |
| Reality Validation (§0.15) | Governance | WP-012 (mechanism), exercised throughout by every WP's own test verification |

### Learning-Engine-Specification-v1.1.md

| Item | Type | Mapped WP |
|---|---|---|
| `ILearningEnginePublicApi`, `query_generated_tasks` consumption | Interface | WP-026 (creation), WP-023 (consumption) |
| `IReasoningEngineClient.compare()`/`.get_trust_signal()` consumption | Interface | WP-026 |
| PipelineRecord, TransitionRecord | Components | WP-026 |
| Ingestion Rate Guard, Confidence Guard | Components | WP-026 |
| Stage transitions Lesson→Pattern | Responsibility | WP-026 |
| Stage transitions Pattern→...→Platform Capability | Responsibility | WP-027 |
| ROI Gate | Responsibility | WP-027 |
| Fitness Functions LF-1–LF-6 | Governance | WP-027 |
| Quarantine, Demotion | Responsibility | WP-027 |
| Feedback Loop Guard, Integrity Checker | Components | WP-027 |
| All Learning Engine events (`LessonPromoted`...`SelfReferentialOutcomeFlagged`) | Events | WP-026 (early stages), WP-027 (later stages) |

### Memory-Management-Specification-v1.0.md

| Item | Type | Mapped WP |
|---|---|---|
| `IKnowledgeClient` (full) | Interface | WP-007 (minimal), WP-014/WP-015/WP-016 (full) |
| Seven memory types | Components | WP-014 |
| Storage Strategy | Responsibility | WP-014 |
| Retrieval Strategy, Retrieval Ranking | Responsibility | WP-014 |
| Context Assembly | Responsibility | WP-015 |
| Consolidation | Responsibility | WP-015 |
| Compression, Expiration | Responsibility | WP-016 |
| `IEmbeddingProviderClient` consumption | Interface | WP-014 (creation dependency on WP-011) |
| All Memory events (`LessonLearned`...`ContextAssembled`) | Events | WP-015 (Consolidation-related), WP-016 (lifecycle-related) |

### Reasoning-Engine-Specification-v1.0.md

| Item | Type | Mapped WP |
|---|---|---|
| `IReasoningEngineClient` (full) | Interface | WP-008 (minimal `reason`), WP-019 (full pipeline), WP-020 (`compare`/`get_trust_signal`/`summarize`/`query_history`) |
| 12-stage pipeline | Responsibility | WP-008 (partial), WP-019 (full) |
| 13 reasoning types | Responsibility | WP-019 |
| Context Management (§12) | Responsibility | WP-019 |
| Decision Model (§13) | Responsibility | WP-008 (minimal), WP-020 (full, incl. History) |
| Explainability | Responsibility | WP-008 (minimal), WP-020 (full) |
| `IAIProviderClient.infer()` consumption | Interface | WP-008 (via WP-005) |
| All Reasoning events (`DecisionMade`...`ContextExpansionRequested`) | Events | WP-008 (`DecisionMade`), WP-019 (`ContextExpansionRequested`, `LowConfidenceDecisionFlagged`), WP-020 (`ReasoningFailed` handling depth) |

### Protection-Layer-Specification-v1.0.md

| Item | Type | Mapped WP |
|---|---|---|
| `IProtectionClient` (full) | Interface | WP-006 (minimal), WP-012 (engines), WP-013 (domains/emergency) |
| Policy/Rule/Risk/Approval Engines | Components | WP-012 |
| Trust Evaluation, Safety Gates | Components | WP-012 (folded in, per WP-012's own note) |
| Governance Layer, Enforcement Layer | Components | WP-006 (structural wiring), WP-012 (full logic) |
| Eleven Protection Domains | Responsibility | WP-013 |
| Resource Protection ceilings | Responsibility | WP-013 (structural, stubbed values), WP-021 (real values) |
| Emergency Shutdown | Responsibility | WP-013 |
| All Protection events (`ProtectionAllowed`...`EmergencyShutdownCleared`) | Events | WP-006 (basic Allow/Deny), WP-012/WP-013 (full set) |

### Planning-Execution-Engine-Specification-v1.0.md

| Item | Type | Mapped WP |
|---|---|---|
| `IPlanningClient` (full) | Interface | WP-023 |
| Goal Manager, Goal Management (§11) | Components | WP-023 |
| Task Graph Builder, Dependency/Priority Manager | Components | WP-023 |
| Scheduler (§10.6) | Components | WP-024 |
| Execution Coordinator | Components | WP-024 |
| Progress Monitor, Retry Manager, Rollback Manager | Components | WP-025 |
| Dynamic Replanning (all four triggers) | Responsibility | WP-025 |
| Planning Model (§12), Task Management (§13), Scheduling (§14) | Responsibility | WP-023, WP-024 |
| Execution Orchestration (§15) | Responsibility | WP-024, WP-025 (Workflow mechanism implemented; complex-branch exercising deferred per WP-025's own note) |
| All Planning events (`PlannerGenerated`...`RollbackExecuted`) | Events | WP-023 (`GoalCreated` family), WP-024 (`PlannerGenerated`, Task events), WP-025 (`ReplanTriggered`, `RollbackExecuted`) |

### Knowledge-Management-Specification-v1.0.md

| Item | Type | Mapped WP |
|---|---|---|
| `IKnowledgeManagementClient` (full) | Interface | WP-017 (`classify`/`navigate_relationships`), WP-018 (`get_quality`/`search`/`request_governance_action`/`find_duplicates`) |
| Taxonomy Manager, Relationship Manager | Components | WP-017 |
| Quality/Metadata Manager | Components | WP-018 |
| Governance Manager | Components | WP-018 |
| Freshness Manager | Components | WP-018 |
| Discovery/Reuse Engine | Components | WP-018 |
| All Knowledge Management events | Events | WP-017 (`KnowledgeClassified`, `KnowledgeRelationshipAdded`), WP-018 (remainder) |

### AI-Provider-Layer-Specification-v1.0.md

| Item | Type | Mapped WP |
|---|---|---|
| `IAIProviderClient` (full) | Interface | WP-005 (`infer`, minimal), WP-010 (full routing), WP-011 (`discover_capabilities`) |
| `IEmbeddingProviderClient` | Interface | WP-011 |
| Provider Registry, Model Registry | Components | WP-010 |
| Inference Router | Components | WP-005 (minimal), WP-010 (full) |
| Context Builder, Response Adapter | Components | WP-005 (minimal), WP-010 (full) |
| Capability Manager | Components | WP-011 |
| Health Monitor | Components | WP-010 |
| All AI Provider events | Events | WP-010 (Provider/Health events), WP-005 (`InferenceCompleted`, minimal) |

### Resource-Management-Specification-v1.0.md

| Item | Type | Mapped WP |
|---|---|---|
| `IResourceManagementClient` (full) | Interface | WP-021 (`get_current_budget`/`get_current_tier`), WP-022 (`get_model_residency`/`request_background_slot`) |
| Resource Monitor, Capacity Manager | Components | WP-021 |
| Quota Manager, Allocation Manager | Components | WP-021 (Allocation), WP-022 (Quota) |
| Background Task Controller | Components | WP-022 |
| Model Residency Management | Components | WP-022 |
| Performance Collector, Health Monitor | Components | WP-021 |
| All Resource Management events | Events | WP-021 (`ResourceThresholdCrossed`), WP-022 (remainder) |
| GPU resource-type extensibility (ADR-RM003) | Extensibility | WP-021 (structural support only — no GPU registered, no hardware present) |

### EOS-System-Architecture-Specification-v1.0.md

This document is itself a synthesis with no independently-implementable component beyond what the eight subsystem documents above already contain. Its Dependency Rules (§17) are validated by this roadmap's own Dependency Validation section above; its Contracts-mediation pattern (§6.3/ADR-SYS002) is realized by WP-001's `EOS.Contracts` project and honored by every subsequent WP's own dependency declarations.

### Autonomous-Engineering-Loop-Specification-v1.0.md

| Item | Type | Mapped WP |
|---|---|---|
| `ILoopControlClient` | Interface | WP-028 (`get_current_status`), WP-029 (`set_operational_mode`, `emergency_stop`) |
| 18-step cycle sequencing | Responsibility | WP-028 |
| Trigger Sources | Responsibility | WP-028 (eight of nine — see gap below) |
| Operational Modes (all eight) | Responsibility | WP-029 |
| Human Governance | Responsibility | WP-029 |
| Self-Evaluation, Improve | Responsibility | WP-029 |
| All Loop events | Events | WP-028 (`LoopIterationStarted/Completed`), WP-029 (`OperationalModeChanged`, `LoopIterationEvaluated`) |
| File Changes / Git Events trigger | Responsibility | **(gap)** — no owning subsystem exists in the frozen architecture (Autonomous-Engineering-Loop-Specification-v1.0 §8.9's own acknowledgment); not implemented by any WP, consistent with "do not redesign the architecture." Reported here as required by the governing instructions, not silently dropped. |

### Architecture-Validation-Report-v1.0.md

| Blocker | Mapped WP |
|---|---|
| #1 — Constitution Part 1 registration | WP-001 (the four new projects are scaffolded as part of the solution skeleton, functionally closing this blocker as part of implementation rather than as a separate documentation exercise) |
| #2 — Configuration schema | WP-002 (initial real schema authored; full validation hardening continues through WP-030's hardening pass) |
| #3 — Hardware capacity validation | WP-021 (Resource Monitor produces the first real measurements); cross-referenced against the Infrastructure Roadmap's own Phase 3/5 baseline figures |
| #4 — `EOS.Dashboard`/`EOS.Pipeline` specifications | WP-030 implements Dashboard at the Constitution's own already-specified minimal level; `EOS.Pipeline` remains unimplemented, explicitly flagged as out of scope pending its own future specification |

### Summary of Reported Gaps

Three items are explicitly unmapped to any Work Package, each for a documented, architecture-respecting reason rather than an oversight:

1. **Prompt Registry as dedicated tooling** (Constitution Part 9) — prompts are authored inline where needed; no dedicated registry UI/versioning tool is built. Minor; revisit if prompt volume warrants it.
2. **File System / Git Event trigger detection** — no owning subsystem exists anywhere in the frozen architecture; this roadmap cannot invent one without violating "do not redesign the architecture."
3. **`EOS.Pipeline` (CI/CD)** — registered in Constitution Part 1 but never specified beyond one line in any of the eleven approved documents; the Architecture Validation Report recommended a dedicated lightweight specification before implementation, which is outside this roadmap's mandate.
## Implementation Order Review

### Method

Reviewed the sequence WP-001 through WP-030 against four criteria: lowest implementation risk, fastest developer feedback, earliest executable system, and simplest incremental growth.

### Findings

- **Earliest executable system:** satisfied by design — Milestone 2 (WP-005–WP-009) delivers a real, demoable, offline end-to-end capability by roughly the two-week mark, well before any subsystem is built out in full. This was the primary criterion driving the milestone structure, per the Implementation Principles' emphasis on Vertical Slice Architecture.
- **Fastest developer feedback:** satisfied by keeping every WP in the 300–1200 line range with its own build/test verification — no WP requires more than roughly a week of work before a concrete pass/fail signal is available.
- **Lowest implementation risk:** the ordering deliberately front-loads Protection Layer's *structural* wiring (WP-006) far earlier than its full logic (WP-012–WP-013), so that no later WP is ever built against an unguarded action path, even temporarily. This is the single most important risk-reduction decision in the sequence.
- **Simplest incremental growth:** each subsystem's "minimal-then-full" pattern (AI Provider: WP-005→WP-010→WP-011; Protection: WP-006→WP-012→WP-013; Memory: WP-007→WP-014→WP-015→WP-016; Reasoning: WP-008→WP-019→WP-020) means every later WP in a subsystem's own sequence extends already-real, already-tested code rather than replacing a throwaway prototype.

### Reordering Considered and Rejected

- **Building Resource Management (WP-021/022) before Milestone 2's vertical slice** — considered, since Resource Management values are consumed by both Protection and AI Provider Layer. Rejected because the vertical slice's whole purpose is proving the thinnest possible real path exists; a hardcoded conservative budget (as WP-006/WP-005 use) is sufficient for that purpose, and building the full Resource Monitor first would delay the "earliest executable system" milestone by roughly a week for a benefit (real budget values) the vertical slice does not need. Resource Management's real implementation is still sequenced early (Milestone 5, immediately after Reasoning) rather than late.
- **Building Knowledge Management (WP-017/018) before completing all of Memory (WP-014–016)** — considered, since Knowledge Management's own document frames it as a "complementary concern" that could in principle be built alongside Memory rather than strictly after. Rejected in favor of the current strict-after ordering because Knowledge Management's every operation routes through Memory's `IKnowledgeClient` (per that document's own FR-KM1) — building it in parallel risks the two teams (or the same developer, context-switching) coordinating around a moving target instead of a finished interface.
- **Building Learning Engine (WP-026/027) before Planning & Execution Engine (WP-023–025)** — considered, since Learning Engine has no direct dependency on Planning. Rejected because Learning Engine's Feedback Loop Guard (Learning-Engine-Specification-v1.1 §11.5) reads Planning's `query_generated_tasks()`, and validating that read path against a real (not stubbed) Planning implementation is more convincing evidence of correctness than validating it against a mock.
- **Deferring Protection's full engines (WP-012/013) to immediately before Planning & Execution Engine (Milestone 6)**, i.e., much later than currently scheduled — considered, on the theory that Milestone 2–4's actions are low-risk enough not to need full Protection yet. Rejected because Milestone 5's Reasoning Engine work (WP-019/020) already produces real, non-trivial Decisions, and running real cognitive sophistication behind a still-minimal Protection gate for two more milestones was judged an avoidable risk — this is the same reasoning already given in Milestone 6's own rationale, reaffirmed here after considering the alternative explicitly.

### Conclusion

The sequence as presented (WP-001–WP-030) is the reviewed, final order. No further reordering is recommended.

## Quality Review

### Work Packages Too Large

**None identified as requiring a split.** Every WP's estimated code size falls within the requested 300–1200 line range. WP-012 (Protection Engines, ~1100 lines) and WP-027 (Learning full pipeline, ~1200 lines) are at the upper bound — both were considered for splitting (e.g., WP-012 into separate Policy/Rule and Risk/Approval packages; WP-027 into separate "promotion stages" and "ROI/Fitness" packages) but rejected as artificial splits: each represents one logically indivisible capability (a complete tiered validation pipeline; a complete promotion-to-Automation pipeline) whose sub-parts have no independent value or demoability on their own, which the governing instructions explicitly warn against ("do NOT artificially split... every Work Package should represent one logical unit of work").

### Work Packages Too Small

**None identified as requiring a merge.** WP-011 (AI Provider embedding channel, ~400 lines) and WP-006 (Protection minimal gate, ~450 lines) are at the lower bound but were kept separate from their neighbors because each has independent demo value (a standalone `embed()` call; a standalone `validate()` call) and merging either into an adjacent WP would produce a package spanning two distinct interfaces, working against clean PR boundaries (Small Pull Requests principle).

### Hidden Dependencies

One was found and is now explicit rather than hidden: **WP-016 (Memory Compression) calls `EOS.Reasoning`'s `summarize()` before WP-020 implements it.** This is documented in both WP-016's and WP-020's "Explicitly excluded"/"Dependencies" fields as a deliberate stub-then-retrofit pattern, and is listed as a retrofit edge in the Dependency Graph — this is now a *managed* dependency, not a hidden one, but is called out here because it was the least obvious ordering choice in the entire roadmap and merited explicit scrutiny during this review. The same pattern (WP-021 retrofitting WP-013, WP-022 retrofitting WP-010) was checked for the same risk and found equally well-documented.

### Missing Validation

Two gaps were identified and closed during this review:
- WP-013 (Emergency Shutdown) initially lacked an explicit test for the *clearing* half of the mechanism, not just activation — added to that WP's Test Verification field.
- WP-029 (Operational Modes) initially lacked an explicit test proving a mode change *cannot* be self-approved — added as the primary Test Verification item, since this is the single most safety-relevant property of that WP.

### Missing Testing

No subsystem-level WP was found lacking a Test Verification entry. One cross-cutting gap remains, consistent with the Architecture Validation Report's own §12.5 finding: **no dedicated Work Package exists for cross-subsystem integration test infrastructure itself** (as opposed to each WP's own integration tests against its immediate dependencies). This roadmap does not add a 31st WP for this, since the governing instructions cap the count at approximately 30 ±5 and every existing WP already carries its own integration test obligation against real dependencies (not mocks) — but it is worth stating plainly that a *dedicated* cross-subsystem test suite (e.g., replaying the System Architecture Specification's own seven named sequence diagrams as automated tests) remains a reasonable candidate for a future, explicitly-scoped Work Package beyond this roadmap's 30, not a defect in the 30 as presented.

### Missing Deliverables

None found — every WP's "Expected deliverables" field names a concrete, checkable artifact, and every milestone's "Expected outcome" is a direct rollup of its constituent WPs' deliverables.

### Revisions Made During This Review

1. Added explicit clearing-mechanism tests to WP-013.
2. Added explicit non-self-approval test to WP-029.
3. Confirmed (did not change) the WP-012/WP-027 sizing after considering and rejecting artificial splits.
4. Confirmed (did not change) the milestone ordering after considering and rejecting four alternative sequences.
## Final Summary

**30 Work Packages, 7 Milestones, 100% of the approved architecture mapped**, with three explicitly reported and justified gaps (Prompt Registry tooling, File/Git Event trigger detection, `EOS.Pipeline`), none of which this roadmap is permitted to close without either inventing new architecture (forbidden) or exceeding its own mandate (a dedicated future specification for `EOS.Pipeline`, already recommended by the Architecture Validation Report).

The sequence delivers a real, offline, end-to-end demonstrable system by the end of Milestone 2 (roughly WP-009, two weeks in), and reaches full architectural coverage — including the complete Autonomous Engineering Loop — at WP-030. Every Work Package builds, tests, and merges independently; no Work Package depends on a future one; every stub-then-harden pattern (AI Provider, Protection, Memory, Reasoning) is explicit and tracked rather than assumed.

This roadmap introduces no new subsystem, no new technology, no new design pattern, and changes no responsibility from the frozen architecture. It is a sequencing and packaging document only.

---

**Awaiting explicit approval before beginning WP-001, per the governing instructions. No code has been generated. No implementation details beyond Work Package scoping have been produced. No architecture has been redesigned.**
