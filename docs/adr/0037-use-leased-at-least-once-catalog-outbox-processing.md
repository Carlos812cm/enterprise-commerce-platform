# ADR-0037: Use Leased At-Least-Once Catalog Outbox Processing

## Status

Accepted

## Context

[ADR-0036](0036-use-transactional-outbox-for-catalog-events.md)
established atomic persistence of Catalog state and durable dispatch intents.

The remaining problem is operational delivery after the PostgreSQL transaction
commits.

The processor must:

- Scale across multiple Worker instances without claiming the same row
  concurrently.
- Recover work after a process crash.
- Avoid holding a database transaction open during Redis or RabbitMQ I/O.
- Keep cache invalidation and integration-event publication independently
  retryable.
- Preserve W3C trace context across the asynchronous boundary.
- Expose bounded operational telemetry.
- Remain explicit and Catalog-specific instead of introducing a generic
  messaging framework prematurely.

The Storefront cache also has independent in-process L1 caches. Invalidating
shared Redis L2 alone does not evict an already-hot L1 in another API process.

## Decision

Process Catalog Outbox messages with a leased, fenced, at-least-once runtime
owned by `Commerce.Worker`.

Catalog exposes only:

- `ICatalogOutboxBatchProcessor`
- `CatalogOutboxBatchResult`

The store, decoder, dispatcher, retry policy and transport adapters remain
internal to `Catalog.Infrastructure`.

Runtime activation is opt-in through `AddCatalogOutboxProcessing()`.
`AddCatalogInfrastructure()` does not implicitly require RabbitMQ or the
Worker runtime.

## Claiming and Fencing

Workers claim bounded batches in short PostgreSQL transactions using
`FOR UPDATE SKIP LOCKED`.

A claim records:

- A bounded lease owner.
- A lease expiration timestamp.
- The current attempt count.

External I/O occurs only after the claim transaction has completed.

Completion and failure writes are fenced by the current claim state. A stale
worker cannot overwrite a newer claim or a terminal message. When the expected
lease is no longer valid, processing returns `LeaseLost` instead of forcing a
state transition.

Expired leases make abandoned work eligible for recovery.

## Retry and Dead-Letter Policy

Dispatch outcomes are explicit:

- `Success`
- `TransientFailure`
- `PermanentFailure`

Transient failures use deterministic exponential backoff beginning at five
seconds and capped at five minutes.

The maximum attempt count is five. Exhausting that budget dead-letters the
message.

Permanent failures dead-letter immediately.

Failure persistence:

- Increments `attempt_count`.
- Stores a bounded stable error code.
- Schedules `next_attempt_at_utc` for a retry when eligible.
- Clears the lease.
- Sets `dead_lettered_at_utc` for terminal failure.
- Never marks the message processed.

## Dispatch Adapters

### Storefront Cache Invalidation

The cache invalidation intent executes in this order:

1. Invalidate the local `HybridCache` view and shared Redis L2 entry.
2. Publish a Redis Pub/Sub signal.
3. Mark the Outbox message processed only after both operations succeed.

Each Catalog API process hosts a subscriber that receives the Pub/Sub signal
and invalidates its own `HybridCache` instance, including its local L1.

Redis Pub/Sub is a propagation backplane, not the durable source of truth.
The PostgreSQL Outbox remains authoritative. A broadcast call failure is
transient and leaves the message retryable.

A subscriber that is disconnected at the exact publication instant can miss
the ephemeral signal. L2 has already been invalidated, but that process can
serve its existing L1 value until its bounded local expiration or a later
invalidation.

### Product Published Integration Event

`catalog.product-published.v1` is published through RabbitMQ with:

- A durable topic exchange.
- A versioned routing key.
- Mandatory publication.
- Publisher confirms.
- Persistent delivery mode.
- The Outbox identifier as RabbitMQ `MessageId`.
- Serialized access to the long-lived channel.

An unroutable mandatory publication is treated as a permanent failure.
Transport or confirmation failures remain retryable.

## Batch and Host Runtime

A batch runner claims work and starts processing all claimed messages without
serially parking later leases behind earlier messages.

`Commerce.Worker` runs the batch processor continuously through
`CatalogOutboxWorkerService`.

Default options are:

| Option | Default | Valid range |
|---|---:|---:|
| `CatalogOutbox:BatchSize` | `16` | `1` to `128` |
| `CatalogOutbox:LeaseDuration` | `00:01:00` | 10 seconds to 5 minutes |
| `CatalogOutbox:IdleDelay` | `00:00:01` | 100 milliseconds to 30 seconds |

The Worker delays only after an empty batch. Backlog is processed continuously.

Shutdown uses the host cancellation token. Expected cancellation is not
recorded as a message failure.

## Delivery Semantics

The runtime provides at-least-once external delivery.

An external effect can succeed before the fenced `processed_at_utc` update is
accepted. A later worker may therefore repeat the effect.

Consequently:

- Cache invalidation operations are idempotent.
- RabbitMQ consumers performing non-idempotent work require an Inbox or an
  equivalent idempotency boundary.
- Publisher confirms and client connection recovery do not create exactly-once
  business semantics.

## Observability

The processor restores persisted W3C `trace_parent` and `trace_state` into the
`Commerce.Catalog.Outbox` ActivitySource.

Each processing span is named:

`catalog.outbox.process`

Malformed or absent trace context starts an independent root instead of
attaching to an unrelated ambient Activity.

The `Commerce.Catalog.Outbox` Meter emits:

- `catalog.outbox.message.outcomes`
- `catalog.outbox.processing.duration`

The only metric dimension is `catalog.outbox.outcome`, bounded to:

- `processed`
- `retry_scheduled`
- `dead_lettered`
- `lease_lost`

Message identifiers, product identifiers, slugs, worker identifiers, lease
owners and error codes are not metric dimensions.

## Consequences

Benefits:

- Safe multi-worker claiming without a coordinator service.
- Crash recovery through bounded leases.
- Fenced persistence protects terminal and re-claimed rows.
- Cache L1 invalidation propagates across API processes.
- RabbitMQ publication is confirmed and detects unroutable messages.
- W3C trace continuity spans the transactional and asynchronous boundary.
- Operational metrics have bounded cardinality.
- Catalog internals remain hidden from the host.

Costs and risks:

- Delivery is at-least-once, so duplicate external effects remain possible.
- Redis Pub/Sub is ephemeral and cannot prove receipt by every API instance.
- A long-running effect can outlive its lease and complete after ownership was
  lost.
- Dead-letter replay is not automated.
- Retention and archival are still required.
- The Worker depends operationally on PostgreSQL, Redis and RabbitMQ.

## Alternatives Considered

- Perform Redis and RabbitMQ I/O inside the application transaction: rejected
  because it recreates the dual-write problem and holds database work open
  across network calls.
- Use an in-memory queue: rejected because process failure would lose durable
  intent.
- Poll without leases or fencing: rejected because multiple workers and stale
  writers could duplicate state transitions.
- Use Redis Pub/Sub as the durable queue: rejected because Pub/Sub is
  ephemeral.
- Invalidate only Redis L2: rejected because remote API L1 entries could
  remain stale.
- Introduce a generic service bus abstraction: deferred until multiple modules
  demonstrate a justified shared requirement.

## Related

- [ADR-0035](0035-use-hybrid-cache-for-storefront-products.md)
- [ADR-0036](0036-use-transactional-outbox-for-catalog-events.md)
- [Catalog Storefront Cache](../caching/catalog-storefront.md)
- [Catalog Outbox Runbook](../operations/runbooks/catalog-outbox.md)
- [Catalog Persistence](../persistence/catalog.md)
