# WP-003 Implementation Plan — Event Backbone

**Revision:** 1
**Basis:** `EOS-Specification.md` Part 3 (Event Catalog, §3.1 envelope, §3.2 cross-cutting rules), Part 5 (Agent Communication Architecture, §5.1 transport matrix, §5.3 correlation IDs); `EOS-Implementation-Roadmap-v1.0.md` WP-003 row. Repository baseline inspected directly (WP-001/WP-002 code, current `EOS.slnx`, `.coderabbit.yaml`) — not assumed from prior conversation.

## Objective

Implement the Event Catalog's envelope structure (`EventEnvelope`) and an in-process publish/subscribe mediator hosted in `EOS.Orchestrator`, with correlation-ID propagation, exactly as scoped by the Roadmap's WP-003 row.

## Vertical Slice Definition

A producer constructs an `EventEnvelope<TPayload>`, publishes it through the mediator, and any number of registered subscribers for that payload type receive it synchronously — with correlation IDs demonstrably surviving a two-hop (cause → effect) chain. This is real, working, in-process infrastructure every later WP will register events against — not a stub.

## Scope

**Included (verbatim from the Roadmap's "Included components"):**
- `EventEnvelope` type: `EventId` (uuid), `EventType`, `Version` (semver string), `Producer`, `CorrelationId`, `CausationId`, `OccurredAt`, `Payload` (schema-versioned — realized as a generic `TPayload`).
- An in-process publish/subscribe mediator hosted in `EOS.Orchestrator`.
- Correlation ID propagation (Part 5 §5.3): generated at the originating event, carried through every hop.

**Existing components reused:** Nothing from WP-001/WP-002 needs modification. `EOS.Orchestrator` already references `EOS.Contracts` + `EOS.Application` (wired in WP-001); `EOS.Contracts` already references `EOS.SharedKernel`. No existing `.csproj` dependency edge changes.

## Explicitly Excluded

Per the Roadmap's own "Explicitly excluded" field — RabbitMQ/gRPC/any other transport binding (Constitution Part 5 §5.1 already lists in-process/direct-method-call as the approved transport for this exact use case — "Role-to-role coordination within `EOS.Runner`"; RabbitMQ is Part 5's own future option, not this WP's job). Also excluded: any specific named business event (`TaskCreated`, `LessonLearned`, etc. — Part 3 §3.1's catalog is populated incrementally, "each subsystem WP registers its own events as it is built"); the event store / persistence / replay (Part 4, WP-004 territory); `EOS.SDK`'s "Events" module (Part 11 §11.1 conceptually assigns publish/subscribe helpers there, but the Roadmap's WP-003 "Projects affected" field lists only `EOS.Orchestrator, EOS.Contracts` — `EOS.SDK` stays untouched, consistent with it being scaffolded empty in WP-001); wiring the mediator into `EOS.Runner`'s `BootstrapRunner` (WP-003's own acceptance criteria is test-harness-shaped, not `dotnet run`-shaped, unlike WP-002 — nothing in WP-003 needs Bootstrap integration, and no future WP has been started to justify it).

## Projects Affected

`EOS.Contracts`, `EOS.Orchestrator`, plus one new test project `tests/EOS.Orchestrator.Tests`.

## Files to Create

- `src/EOS.Contracts/EventEnvelope.cs` — the envelope type + a minimal `Create(...)` static factory (justified below).
- `src/EOS.Orchestrator/EventMediator.cs` — the publish/subscribe mediator.
- `tests/EOS.Orchestrator.Tests/EOS.Orchestrator.Tests.csproj`
- `tests/EOS.Orchestrator.Tests/EventEnvelopeTests.cs`
- `tests/EOS.Orchestrator.Tests/EventMediatorTests.cs`

## Files to Modify

- `EOS.slnx` — add the new test project (mechanical, same pattern as WP-001/WP-002).

Nothing in `src/EOS.Runner`, `src/EOS.SharedKernel`, or any WP-001/WP-002 file is touched — no baseline modification required.

## Dependency Changes

None. `EOS.Orchestrator` already references `EOS.Contracts`. The new test project references only `EOS.Orchestrator.csproj` (which transitively exposes `EOS.Contracts`' `EventEnvelope` — avoids the redundant double-reference noted as a WP-002 self-review finding). No new NuGet packages — `System.Text.Json` (envelope serialization test) is already part of the SDK, used since WP-002.

## Public Interfaces

None added to `EOS.Contracts` as a *published cross-subsystem interface* (no `I...Client` boundary is in scope here). `EventEnvelope<TPayload>` is a shared data-shape type, which is exactly what Part 5 §5.1 assigns to `EOS.Contracts` ("Cross-module data shape agreement — underpins all of the above"), not an interface.

`EventMediator` is a plain concrete class in `EOS.Orchestrator` — **no `IEventMediator` interface is introduced.** Nothing in WP-003's acceptance criteria requires substitutability, mocking, or multiple implementations; adding one now would be exactly the "unnecessary interface" Rule 3 forbids. *Why does WP-003 need this NOW? It doesn't — removed.*

## Data Model / Database Changes

None. Everything is in-memory, in-process (Part 4/event-store persistence is explicitly excluded — WP-004 territory).

## API Changes

None.

## Configuration Changes

None. No new `config/*.json` field, no `Options` record change.

## Implementation Flow

1. `EventEnvelope<TPayload>` (record, `EOS.Contracts`): `EventId` (`Guid`), `EventType` (`string`), `Version` (`string`), `Producer` (`string`), `CorrelationId` (`Guid`), `CausationId` (`Guid?`), `OccurredAt` (`DateTimeOffset`), `Payload` (`TPayload`). A static `Create(eventType, version, producer, payload, correlationId = null, causationId = null)` factory generates `EventId`/`OccurredAt` and defaults `CorrelationId` to a new `Guid` when the caller doesn't propagate one (i.e., this event originates a new correlation chain). *Why does WP-003 need this NOW? Every test that constructs an envelope needs it — without it, `EventId`/`OccurredAt` generation and correlation-defaulting logic would be duplicated in every call site, which is the actual over-engineering risk (duplicated logic), not the factory itself.*
2. `EventMediator` (sealed class, `EOS.Orchestrator`): `Subscribe<TPayload>(Action<EventEnvelope<TPayload>> handler)` registers a handler keyed by `typeof(TPayload)`; `Publish<TPayload>(EventEnvelope<TPayload> envelope)` synchronously invokes every registered handler for that payload type, in registration order. Matches Part 5 §5.2's "Synchronization: role coordination ... is synchronous via the Orchestrator mediator" — no `Task`/async needed. Publishing with zero subscribers is a safe no-op (not an error).
3. No production entry point changes.

## Test Strategy

`tests/EOS.Orchestrator.Tests` (xUnit, same pattern as `EOS.Runner.Tests`):
- `EventEnvelopeTests`: `System.Text.Json` serialize→deserialize round-trip preserves all fields (Roadmap's own required test).
- `EventMediatorTests`:
  - Publishing delivers to all registered subscribers of the matching payload type (≥2 subscribers, both receive it) — this is also literally the Roadmap's Demo/acceptance criteria.
  - A subscriber to a *different* payload type does not receive an unrelated publish (negative case, proves type-keyed dispatch actually discriminates).
  - Correlation ID survives a two-hop chain: Event 1 is published (originates `CorrelationId` = X); its subscriber reacts by publishing Event 2 via `EventEnvelope.Create(..., correlationId: X, causationId: event1.EventId)`; a second, independent subscriber to Event 2 asserts it received `CorrelationId == X` (Roadmap's own required test).
  - Publishing with no subscribers does not throw (defensive edge case of the same code path under test).

## Acceptance Criteria (verbatim from Roadmap)

"A test harness publishes a sample event and two independent subscribers both receive it with matching correlation IDs."

## Definition of Done

`dotnet restore/build/test/format` all succeed; zero warnings; `EOS.ArchitectureTests` (R-00) still passes against the now-larger graph; all new tests pass; repository clean; one commit.

## Risks

- **None architectural.** The only design choice worth flagging is the generic `EventEnvelope<TPayload>` vs. an `object`-payload envelope — generics were chosen for type safety and to avoid boxing/casting at every call site, a standard C# feature rather than a "framework," and every later WP registering a real event (`TaskCreated`, etc.) will define its own payload record and get compile-time safety for free.

## Future WP Boundaries

- Real named events (`TaskCreated`, `LessonLearned`, ...) are registered by the subsystem WPs that own them (Milestone 2 onward), not WP-003.
- Event-store persistence/replay is WP-004 (Data Store Foundations) plus later Memory/Knowledge WPs.
- `EOS.SDK`'s Events module (Part 11 §11.1) is populated whenever a future WP decides it needs shared publish/subscribe *base classes* on top of this mediator — not required or built here.
- RabbitMQ/other transport bindings are out of scope for the entire single-machine target per Constitution Part 5 §5.1, named there only as a future option.
- Wiring `EventMediator` into `EOS.Runner`'s Bootstrap sequence happens naturally once a WP has a real event to register at startup — not before.

## KISS / YAGNI Justification

- No `IEventMediator` interface (no consumer needs substitutability yet).
- No async/`Task`-based publish (Part 5 §5.2 specifies synchronous in-process dispatch).
- No event store, no persistence, no replay (explicitly excluded, future WP).
- No named business events beyond what tests need as a sample payload.
- No new NuGet package.
- No DI registration/Bootstrap wiring (no current consumer).
- The one addition beyond the bare minimum (`EventEnvelope.Create` factory) has concrete, immediate consumers in this WP's own test suite and removes duplicated ID/timestamp-generation logic — not speculative.
