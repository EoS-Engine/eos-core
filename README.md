# EoS Core

**EoS — Engineering Operating System**

EoS Core is the implementation repository for an autonomous engineering operating system designed to turn engineering goals, execution, evidence, and learned outcomes into a traceable, governed, continuously improving engineering workflow.

The repository follows a deliberately incremental architecture: every capability is implemented as a small, independently verifiable Work Package while preserving the approved architecture as the single source of truth.

## Project Status

- **Current milestone:** Milestone 1 — Bootstrap Foundation
- **Completed:** WP-001, WP-002, WP-003
- **Next:** WP-004 — Data Store Foundations

The implementation roadmap contains **30 Work Packages across 7 milestones**. Each Work Package is independently implemented, tested, reviewed, and merged before the next Work Package begins.

## Core Engineering Principles

### Architecture Is the Source of Truth
Implementation follows the approved EoS specifications and roadmap. Code does not redesign the architecture implicitly.

### One Work Package at a Time
Only one Work Package is active at any point. A Work Package must be completed and formally closed before the next one starts.

### Vertical Slice First
Prefer the smallest real, executable path over large collections of isolated placeholders. A slice should prove real behavior end to end whenever the Work Package permits it.

### KISS / YAGNI / No Over-Engineering
Do not introduce abstractions, interfaces, frameworks, infrastructure, configuration, or extensibility mechanisms without a concrete current requirement. Future needs belong to the Work Package that actually requires them.

### Real Implementations Over Throwaway Stubs
When a Work Package requires working behavior, implement the smallest real version that satisfies its approved scope. Deliberate stub-then-harden sequences are only used where the roadmap explicitly defines them.

### Tests Are Part of the Implementation
Every Work Package defines its own verification criteria. A change is not considered complete merely because the solution builds.

## Architecture

The repository follows the approved EoS physical repository architecture and dependency rules.

Key foundational projects include:

- `EOS.Contracts` — shared inter-module contracts, DTOs, and events
- `EOS.SharedKernel` — shared primitives and foundational types
- `EOS.Orchestrator` — orchestration and in-process coordination
- `EOS.Infrastructure` — physical data-store and infrastructure ownership
- `EOS.Runner` — application/bootstrap entry point
- subsystem projects for planning, reasoning, protection, memory, knowledge, learning, AI providers, resources, and related capabilities

The architecture intentionally separates contracts, orchestration, infrastructure, and subsystem responsibilities. Dependency direction and architecture fitness rules are validated continuously.

## Event Backbone

WP-003 established the first real event backbone:

- `EventEnvelope<TPayload>` in `EOS.Contracts`
- synchronous in-process publish/subscribe through `EOS.Orchestrator`
- correlation ID propagation
- causation ID tracking
- tested multi-subscriber delivery
- two-hop correlation/causation verification

External messaging transports are not part of the current single-machine implementation scope.

## Data Store Foundation

WP-004 is the next Work Package.

Its approved objective is to establish real, tested connections from `EOS.Infrastructure` to:

- SQL Server
- Redis
- ChromaDB
- SQLite

It also establishes the event-store append-only write path required by the approved architecture.

WP-004 explicitly does **not** implement domain-specific `KnowledgeNode` schema or future Redis/ChromaDB conventions beyond the connectivity and smoke-test requirements defined by its approved scope.

## Work Package Workflow

Starting with WP-004, the repository follows this workflow:

1. Create a feature branch.
2. Produce and review the Work Package implementation plan.
3. Implement only the approved Work Package.
4. Run the local Architecture / Self-Review Gate.
5. Push the feature branch.
6. Open a Pull Request.
7. Run CodeRabbit review.
8. Fix valid findings without expanding scope.
9. Re-run verification.
10. Obtain approval.
11. Merge into `main`.
12. Tag the completed Work Package.
13. Produce the formal Closure Report.
14. Start the next Work Package only after closure.

No Work Package may silently absorb work belonging to another Work Package.

## Verification Expectations

A Work Package is expected to leave the repository in a verifiable state appropriate to its scope, including as applicable:

```text
dotnet restore
dotnet build
dotnet test
dotnet format --verify-no-changes
```

Architecture tests must continue to pass, and no unrelated Work Package should regress.

## Repository

Canonical repository: **EoS-Engine/eos-core**

The repository is maintained under the `EoS-Engine` GitHub organization.

## Documentation

The repository contains the governing architecture and implementation documents, including:

- `EOS-Specification.md` — the immutable Constitution / architectural source of truth
- `EOS-Implementation-Roadmap-v1.0.md` — the approved Work Package roadmap
- subsystem-specific specifications
- Work Package implementation and completion reports
- architecture and validation documentation

When implementation and documentation appear to disagree, the approved architecture and the current Work Package scope must be checked before changing code or documentation.

## License

EoS Core is released under the MIT License. See [`LICENSE.md`](LICENSE.md).

---

**EoS Core — build the smallest real thing, prove it, then move forward.**
