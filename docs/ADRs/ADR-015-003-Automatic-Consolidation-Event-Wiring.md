# ADR-015-003 — Automatic Consolidation Event Wiring

## Status

**Accepted**

## Background

Two of `consolidate()`'s four triggers (§16.1) are automatic: novel Gate failure, and `IncidentResolved`. Both require `EOS.Knowledge` to receive a signal it did not itself produce. `EOS.Knowledge` has no dependency path to `EOS.Orchestrator`'s `EventMediator`, the only event-mediating component observed in this codebase — and that component itself has never been wired to anything in `Program.cs`. Cross-validation further found that Constitution §5.1's Transport Selection Matrix designates RabbitMQ, not the in-process `EventMediator`, as the transport for durable, cross-service "Event Catalog delivery" — and no RabbitMQ code exists anywhere in this codebase either.

## Problem Statement

By what mechanism does `EOS.Knowledge` receive Gate-failure and `IncidentResolved` signals, given no dependency path exists and no fully-specification-correct transport (RabbitMQ) exists in this codebase at all?

## Alternatives Considered

1. **`EOS.Knowledge` subscribes directly to `EventMediator`** — requires a new, ungranted dependency edge.
2. **Composition-root-mediated subscription** — `Program.cs` (already legitimately depending on both `EOS.Orchestrator` and `EOS.Knowledge`) subscribes to `EventMediator` and calls into `IKnowledgeClient` on receipt.
3. **Direct producer-to-consumer call** (e.g., `EOS.Gates` calls `EOS.Knowledge` directly) — blocked by the same category of missing dependency edge, on a different project.
4. **Defer both automatic triggers**, implementing only the two role-initiated triggers this WP, until real cross-service event delivery (RabbitMQ) exists.

## Decision

Adopt Alternative 2 — composition-root-mediated subscription via `EOS.Orchestrator`'s `EventMediator`, explicitly accepted as an **interim mechanism**, pending real Event Catalog delivery (RabbitMQ) being built in a future, dedicated infrastructure Work Package.

## Rationale

Alternative 1 and Alternative 3 both require a new dependency edge that Constitution Part 1 §1.2 does not grant, on `EOS.Knowledge` and `EOS.Gates` respectively. Alternative 4 was seriously weighed: the roadmap's WP-015 row explicitly lists "the four consolidation triggers (§16.1)" as an Included Component, not two of four — deferring two would be an unauthorized scope reduction against that explicit wording, and unlike ADR-015-002's Alternative 4 (which conflicted only with a postcondition), this would conflict with an explicit "Included components" enumeration. Alternative 2 requires zero new dependency edge (`EOS.Runner` already legitimately depends on everything) and satisfies the roadmap's full four-trigger requirement, at the accepted cost of using an in-process, non-durable mechanism for what the Constitution's own Transport Selection Matrix scopes to a durable, cross-service transport that does not yet exist in this codebase. This limitation is explicitly disclosed, not hidden.

## Consequences

- `Program.cs` will subscribe to `EventMediator` for the relevant event types and call into `IKnowledgeClient` on receipt — the first real, end-to-end exercise of `EventMediator` in this codebase's history.
- Because `EventMediator` is in-process and non-durable, automatic consolidation triggered this way is subject to the same-runtime, same-process limitation the Constitution's Transport Selection Matrix describes for "role-to-role coordination" — not the durable, replayable guarantee Part 3's event table implies for genuine Event Catalog delivery. This is accepted as a known, bounded interim limitation.
- A future, dedicated infrastructure Work Package is expected to introduce real RabbitMQ-backed Event Catalog delivery; at that point, this interim mechanism should be revisited.

## Specification References

- Memory-Management-Specification-v1.0 §16.1: "Automatic, on Gate failure (novel failure)"; "Automatic, on `IncidentResolved`"

## Constitution References

- Part 1 §1.2: `EOS.Orchestrator | ... | EOS.Contracts, EOS.Application | Role internals directly (role projects only via contracts)`
- Part 1 §1.2: `EOS.Runner | DevOps | Everything (composition root) | —`
- Part 2 §2.1, Rule 7: "Role projects never reference each other directly. All role-to-role communication is via `EOS.Orchestrator` + `EOS.Contracts` events."
- Part 5 §5.1: "In-process (same runtime, same host) | Direct method call via `EOS.Orchestrator` mediator | Role-to-role coordination within `EOS.Runner`"; "Durable async cross-service | RabbitMQ | Event Catalog delivery (Part 3), Scheduler task dispatch"

## Impact Analysis

No new dependency edge for `EOS.Knowledge` or `EOS.Orchestrator`. `Program.cs` gains new subscription wiring at implementation time (not performed by this ADR). Roadmap's WP-015 "Included components" (all four triggers) is satisfied in full, with the disclosed transport limitation above.

## Future Work

When real, durable Event Catalog delivery (RabbitMQ) is introduced by a future infrastructure Work Package, this ADR's interim mechanism should be revisited and, if warranted, superseded by a new ADR routing automatic consolidation triggers through that durable transport instead.
