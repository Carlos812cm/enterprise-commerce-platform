# ADR-0036: Use Transactional Outbox for Catalog Events

## Status

Accepted

## Context

Catalog aggregates raise Domain Events in memory.

Some committed Catalog changes must produce reliable post-commit
consequences outside the aggregate transaction.

Product publication currently requires two independent consequences:

- Invalidate the published Storefront product cache.
- Publish a versioned Product Published integration event for external
  consumers.

Executing Redis or RabbitMQ operations directly inside an application
command would create a dual-write problem.

The database commit could succeed while the external operation fails,
or the external operation could succeed while the database transaction
rolls back.

Catalog also uses an explicit persistence model.

EF Core tracks `ProductRecord`, while Domain Events belong to the
`Product` aggregate.

Therefore EF `ChangeTracker` scanning cannot be used as the authoritative
source of aggregate Domain Events without violating the explicit
persistence boundary.

## Decision

Use a PostgreSQL Transactional Outbox for Catalog events.

Catalog persists aggregate changes and their resulting Outbox dispatch
intents through the same `CatalogDbContext.SaveChangesAsync` operation.

The Outbox is stored in:

`catalog.outbox_messages`

A Catalog-specific scoped `CatalogDomainEventTracker` explicitly tracks
the `Product` aggregate instances participating in the current unit of
work.

`ProductRepository` enrolls aggregates in the current write unit of work
when they are:

- Added.
- Successfully applied to persistence through `Update`.

Rehydrating an aggregate does not enroll it automatically in a write unit
of work.

This prevents Domain Events from producing Outbox records when the
corresponding aggregate state has not been applied to the explicit EF Core
persistence model.

An explicit `CatalogUnitOfWork` coordinates Domain Event staging,
Outbox persistence and post-commit cleanup.

## Transaction Boundary

The save sequence is:

1. Inspect Domain Events from explicitly tracked aggregates.
2. Project supported Domain Events into Outbox records.
3. Add those records to the same EF Core `CatalogDbContext`.
4. Execute one `SaveChangesAsync`.
5. Only after successful persistence, dequeue the aggregate Domain Events.

Aggregate changes and Outbox records therefore participate in the same
PostgreSQL transaction created by EF Core for `SaveChangesAsync`.

If persistence fails:

- PostgreSQL rolls back the aggregate changes.
- PostgreSQL rolls back the Outbox inserts.
- Domain Events remain on the aggregate.
- Previously staged Outbox entities remain available for retry in the
  same unit of work.
- Re-staging the same Domain Event does not create duplicate Outbox
  entities in that unit of work.

## Domain Event Projection

Not every Domain Event becomes an Outbox message.

Outbox projection is explicit.

At the time of this decision, only
`ProductPublishedDomainEvent` produces durable dispatch intents.

Publication generates two independent Outbox messages:

- `catalog.storefront-product-cache-invalidate.v1`
- `catalog.product-published.v1`

Separating these intents allows cache invalidation and RabbitMQ delivery
to have independent processing, retry and failure semantics.

Domain Events without an explicit Outbox projection remain internal
domain facts and are discarded after a successful unit-of-work commit.

## Message Contracts

Durable message type names are stable logical identifiers.

CLR assembly-qualified type names are not persisted.

The public Product Published contract is:

`ProductPublishedIntegrationEventV1`

and belongs to `Catalog.Contracts`.

The contract uses transport-neutral primitives and does not expose:

- Catalog Domain types.
- EF Core types.
- RabbitMQ types.
- Infrastructure implementation details.

The publication Domain Event captures both Product identity and immutable
published slug so downstream processing does not need to reconstruct that
historical fact from current database state.

## Outbox State Model

Outbox processing state is derived from timestamps and lease fields rather
than an explicit status enum.

Relevant fields include:

- Message identifier.
- Logical message type.
- JSON payload.
- Occurrence timestamp.
- Enqueue timestamp.
- Attempt count.
- Next-attempt timestamp.
- Lease owner.
- Lease expiration.
- Processed timestamp.
- Dead-letter timestamp.
- Last error code.
- W3C trace context.

Database constraints protect invalid state combinations.

A partial pending-message index supports future worker polling.

## Delivery Semantics

The Transactional Outbox guarantees atomic persistence of:

- Catalog database state.
- Dispatch intent.

It does not provide exactly-once external delivery.

Catalog Outbox processing uses at-least-once semantics.

The runtime design is defined by
[ADR-0037](0037-use-leased-at-least-once-catalog-outbox-processing.md).

Consequently:

- Cache invalidation handlers must be idempotent.
- External consumers that perform non-idempotent work require Inbox or
  equivalent idempotency protection.
- RabbitMQ connection recovery is not considered a substitute for
  business-level delivery guarantees.

## Observability

When a W3C `Activity` is active, the Outbox captures bounded
`trace_parent` and `trace_state` values.

The Outbox does not persist unrestricted tracing baggage, stack traces,
customer identifiers or other high-cardinality telemetry.

## Failure and Retry

Outbox records are not removed when dispatch fails.

The Catalog Outbox processor uses:

- PostgreSQL `FOR UPDATE SKIP LOCKED` claims.
- Attempt counters and deterministic exponential retry scheduling.
- Bounded leases and fenced completion or failure writes.
- Dead-letter state for permanent failures and exhausted retry budgets.

A database transaction must not remain open while performing Redis,
RabbitMQ or other network I/O.

Claiming work and performing external dispatch are separate operational
steps.

## Testing

The Outbox schema is verified against PostgreSQL 18 through Testcontainers.

Integration tests demonstrate:

- The migration applies to a real PostgreSQL instance.
- Valid pending messages are accepted.
- Invalid attempt, lease, terminal and scheduling states are rejected.
- Product publication persists the aggregate and exactly two Outbox
  intents.
- Forced Outbox persistence failure rolls back Product publication.
- No Outbox message survives the failed transaction.
- Domain Events remain available after rollback.
- Retrying the same unit of work commits successfully without creating
  duplicate Outbox messages.
- Rehydrating and mutating a Product without calling repository `Update`
  does not persist Product changes or create Outbox messages.
- Explicitly updating that same aggregate later enrolls it and commits the
  Product state and Outbox messages together.

Mocks are not considered equivalent evidence for these transactional
properties.

## Consequences

Benefits:

- Eliminates the Catalog database / external-message dual-write window.
- Preserves Domain purity by keeping I/O outside aggregates.
- Preserves the explicit persistence model.
- Enables reliable post-commit Storefront invalidation.
- Establishes a durable boundary for RabbitMQ integration events.
- Provides explicit retry and dead-letter state.
- Preserves distributed tracing context across asynchronous processing.
- Gives transactional behavior executable integration-test coverage.

Costs:

- Introduces an additional durable table and processing lifecycle.
- Requires a background Outbox processor.
- External delivery remains at-least-once rather than exactly-once.
- Consumers must tolerate duplicate delivery.
- Outbox retention and cleanup policies will be required operationally.
- Message contract evolution must be versioned deliberately.

## Deferred

This decision and ADR-0037 do not implement:

- Generic Inbox processing.
- Exactly-once delivery.
- Distributed sagas.
- Multi-instance Storefront cold-miss coordination.
- Automated dead-letter replay.
- Advanced Outbox retention and archival.
