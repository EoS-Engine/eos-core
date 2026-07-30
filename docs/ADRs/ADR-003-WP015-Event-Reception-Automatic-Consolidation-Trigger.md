# ADR-003 — WP-015 Event Reception / Automatic Consolidation Trigger Mechanism

> **Superseded by `ADR-015-003-Automatic-Consolidation-Event-Wiring.md`** (Status: Accepted, Option B ratified as an interim mechanism). This document is preserved unmodified as the historical Proposed-stage analysis.

## Status

Proposed

## Context

Two of the four consolidation triggers (§16.1) are automatic: novel Gate failure, and `IncidentResolved`. Both require `EOS.Knowledge` to receive a signal it did not itself produce. `EOS.Knowledge`'s Constitution-declared dependency shape grants no path to `EOS.Orchestrator`, the only event-mediating component (`EventMediator`) observed anywhere in this codebase. A repository check confirms `EventMediator`/`EventEnvelope` exist but have never been wired to anything in `Program.cs` — this would be first-of-its-kind usage, not an extension of a working precedent.

## Problem Statement

By what mechanism does `EOS.Knowledge` receive Gate-failure and `IncidentResolved` signals, given its current dependency shape and the absence of any existing event-subscription wiring anywhere in this codebase?

## Evidence

`EOS-Specification.md` Part 1 §1.2:
> "| EOS.Orchestrator | Principal Engineer | EOS.Contracts, EOS.Application | Role internals directly (role projects only via contracts) |"

`EOS-Specification.md` Part 1 §1.2:
> "| EOS.Knowledge | Principal Engineer | EOS.KnowledgeGraph, EOS.VectorStore | Role projects |"

`EOS-Specification.md` Part 1 §1.2:
> "| EOS.Gates | QA / Principal Engineer | EOS.Contracts, EOS.Domain (read) | EOS.Web, EOS.Mobile |"

`EOS-Specification.md` Part 1 §1.2:
> "| EOS.Runner | DevOps | Everything (composition root) | — |"

`EOS-Specification.md` Part 2 §2.1, Rule 7:
> "Role projects never reference each other directly. All role-to-role communication is via `EOS.Orchestrator` + `EOS.Contracts` events (Part 5). Fitness rule R-06."

Repository check (not a specification citation, a factual verification): `EOS.Orchestrator/EventMediator.cs` and `EOS.Contracts/EventEnvelope.cs` exist; `src/EOS.Runner/Program.cs` contains zero references to either.

`EOS-Specification.md` Part 5 §5.1 (Transport Selection Matrix):
> "In-process (same runtime, same host) | Direct method call via `EOS.Orchestrator` mediator | Role-to-role coordination within `EOS.Runner`"
> "Durable async cross-service | RabbitMQ | Event Catalog delivery (Part 3), Scheduler task dispatch"

**Additional evidence found during Phase 2 cross-validation:** §5.1 identifies *two distinct transports* — `EOS.Orchestrator`'s in-process mediator (scoped explicitly to "role-to-role coordination," a synchronous, same-process concern) versus RabbitMQ (scoped explicitly to "Event Catalog delivery," the durable, cross-service mechanism Part 3's `LessonLearned`/`IncidentResolved` rows actually describe, both marked "Replayable"). Reading `EventMediator.cs`'s actual implementation confirms it is a bare in-memory `Dictionary<Type, List<Delegate>>` — no durability, no cross-process delivery, nothing resembling "Event Catalog delivery." A repository-wide search found **no RabbitMQ package reference or client code anywhere in this codebase.** This means the specification's own designated transport for genuine Event Catalog delivery (RabbitMQ) does not exist at all — only the narrower, same-process `EventMediator` does, and it has never been wired to anything.

## Considered Options

**Option A — `EOS.Knowledge` subscribes directly to `EventMediator`.** Requires a new `EOS.Knowledge`→`EOS.Orchestrator` edge, not currently granted.

**Option B — Composition-root-mediated subscription.** `Program.cs` (already legitimately depending on both `EOS.Orchestrator` and `EOS.Knowledge`, per its "Everything" row) subscribes to `EventMediator` for the relevant event types and, on receipt, calls into `IKnowledgeClient` directly.

**Option C — Direct producer-to-consumer call** (e.g., `EOS.Gates` calls `EOS.Knowledge` directly). `EOS.Gates`'s own row (`EOS.Contracts, EOS.Domain (read)`) grants no path to `EOS.Knowledge` either — this requires the same category of new edge as Option A, on a different project.

**Option D — Defer automatic triggers entirely**; implement only the two role-initiated triggers (explicit action, session close) this WP, leaving Gate-failure/`IncidentResolved`-triggered consolidation for a future WP once real cross-service event delivery (RabbitMQ, per §5.1) exists.

## Pros / Cons

| Option | Pros | Cons |
|---|---|---|
| A | Simplest to reason about (direct subscription). | Requires a new dependency edge Constitution Part 1 §1.2 does not currently grant. |
| B | Zero new dependency edge for either `EOS.Knowledge` or `EOS.Orchestrator`; consistent with Part 2 §2.1 Rule 7's general architecture ("all... communication is via `EOS.Orchestrator` + `EOS.Contracts` events"), even though `EOS.Knowledge` is not itself a role project under that rule's literal scope. | No existing precedent anywhere in this codebase for `Program.cs` actually wiring `EventMediator` to anything — this would be genuinely new, first-of-its-kind infrastructure, carrying more first-use risk than ADR-001's adapter pattern (which has a working precedent). |
| C | No new pattern needed. | Blocked by the same missing dependency edge as Option A, just relocated to `EOS.Gates`. |
| D | Matches roadmap's own "Included components" wording literally requiring only §16.1's four triggers be *specified*, while honestly reflecting that the correct transport (RabbitMQ, per §5.1) doesn't exist yet anywhere in this codebase; avoids using the in-process `EventMediator` for a durable, cross-service concern it was never scoped for. | The roadmap explicitly lists "the four consolidation triggers (§16.1)" as an Included Component — deferring two of four is a scope reduction requiring explicit sign-off, not a free option. |

## Consequences

Choosing B means WP-015 would be the first WP in this codebase's history to actually exercise `EventMediator` end-to-end — and, per the newly-found §5.1 transport distinction, would be doing so for a purpose (durable, cross-service Event Catalog delivery) that specification text assigns to RabbitMQ, not the in-process mediator. This is a materially higher risk than originally assessed: Option B as previously framed may itself be using the wrong transport for what `LessonLearned`/`IncidentResolved` actually are.

## Recommendation

**Recommendation revised on this pass.** The original recommendation (Option B) is not disproved outright, but its risk is now understood to be higher than stated: using `EventMediator` for automatic Gate-failure/`IncidentResolved` triggers may be using an in-process transport for a durable cross-service concern the specification itself assigns elsewhere (RabbitMQ). Given RabbitMQ does not exist anywhere in this codebase either, **Option D (defer the two automatic triggers) is now equally defensible** and arguably lower-risk, at the cost of an explicit, disclosed scope reduction against the roadmap's "Included components" wording. This ADR presents both B and D to the Board rather than picking one — the choice depends on whether the Board considers `EventMediator` an acceptable interim substitute for genuine Event Catalog delivery, a judgment call outside this ADR's evidence.

## Open Questions

- Should the "session close with flagged content" trigger (§16.1's fourth, non-automatic trigger) use the same or a different mechanism, since it is role-initiated rather than event-driven?
- What filtering/routing logic determines which `EventMediator`-observed events specifically trigger `consolidate()` versus being ignored? Not addressed by any reviewed specification.

## Decision Required

Cross-cutting architecture decision per Constitution §0.6. Requires ratification before any `Program.cs`/`EventMediator` wiring is built.
