# Catalog Outbox Runbook

## Scope

This runbook covers the `Commerce.Worker` runtime that processes
`catalog.outbox_messages`.

The PostgreSQL Outbox is the durable source of dispatch intent.

The two supported message types are:

- `catalog.storefront-product-cache-invalidate.v1`
- `catalog.product-published.v1`

## Runtime Topology

```text
Catalog transaction
        |
        v
catalog.outbox_messages
        |
        v
Commerce.Worker
        |
        +--> HybridCache invalidation --> Redis Pub/Sub --> Catalog API L1
        |
        +--> RabbitMQ confirmed publication
```

## Dependencies

The Worker requires:

- PostgreSQL
- Redis
- RabbitMQ

Local infrastructure:

```bash
docker compose up -d postgres redis rabbitmq
```

Do not use `--remove-orphans` merely to silence Compose warnings when the
observability overlay is also running.

## Database Preparation

Restore the repository-local EF Core tool:

```bash
dotnet tool restore
```

Apply Catalog migrations:

```bash
dotnet ef database update \
  --project src/Modules/Catalog/Catalog.Infrastructure/Catalog.Infrastructure.csproj \
  --startup-project src/Modules/Catalog/Catalog.Infrastructure/Catalog.Infrastructure.csproj \
  --context CatalogDbContext \
  --configuration Release
```

Catalog stores migration history at:

```text
catalog.__ef_migrations_history
```

## Worker Configuration

| Configuration key | Default | Valid range |
|---|---:|---:|
| `CatalogOutbox:BatchSize` | `16` | `1` to `128` |
| `CatalogOutbox:LeaseDuration` | `00:01:00` | 10 seconds to 5 minutes |
| `CatalogOutbox:IdleDelay` | `00:00:01` | 100 milliseconds to 30 seconds |

Environment-variable forms use double underscores:

```text
CatalogOutbox__BatchSize
CatalogOutbox__LeaseDuration
CatalogOutbox__IdleDelay
```

Required connection strings are:

```text
ConnectionStrings__Postgres
ConnectionStrings__Redis
ConnectionStrings__RabbitMq
```

Options are validated during host startup.

## Start and Stop

Run locally:

```bash
dotnet run --project src/Hosts/Commerce.Worker/Commerce.Worker.csproj
```

Use `Ctrl+C` for graceful shutdown.

The Worker stops claiming new batches through the host cancellation token.
An expected shutdown cancellation is not persisted as a message failure.

## State Interpretation

A row is pending while both terminal timestamps are null:

```sql
SELECT
    id,
    type,
    attempt_count,
    next_attempt_at_utc,
    lock_owner,
    locked_until_utc,
    last_error_code
FROM catalog.outbox_messages
WHERE processed_at_utc IS NULL
  AND dead_lettered_at_utc IS NULL
ORDER BY
    next_attempt_at_utc,
    occurred_at_utc,
    id
LIMIT 100;
```

Currently eligible work:

```sql
SELECT
    id,
    type,
    attempt_count,
    next_attempt_at_utc
FROM catalog.outbox_messages
WHERE processed_at_utc IS NULL
  AND dead_lettered_at_utc IS NULL
  AND next_attempt_at_utc <= CURRENT_TIMESTAMP
  AND (
      lock_owner IS NULL
      OR locked_until_utc <= CURRENT_TIMESTAMP
  )
ORDER BY
    next_attempt_at_utc,
    occurred_at_utc,
    id
LIMIT 100;
```

Active leases:

```sql
SELECT
    id,
    type,
    lock_owner,
    locked_until_utc
FROM catalog.outbox_messages
WHERE lock_owner IS NOT NULL
  AND locked_until_utc > CURRENT_TIMESTAMP
ORDER BY locked_until_utc;
```

Scheduled retries:

```sql
SELECT
    id,
    type,
    attempt_count,
    next_attempt_at_utc,
    last_error_code
FROM catalog.outbox_messages
WHERE processed_at_utc IS NULL
  AND dead_lettered_at_utc IS NULL
  AND attempt_count > 0
ORDER BY next_attempt_at_utc;
```

Dead-lettered messages:

```sql
SELECT
    id,
    type,
    attempt_count,
    dead_lettered_at_utc,
    last_error_code
FROM catalog.outbox_messages
WHERE dead_lettered_at_utc IS NOT NULL
ORDER BY dead_lettered_at_utc DESC;
```

Aggregate failure-code counts for investigation:

```sql
SELECT
    COALESCE(last_error_code, '<none>') AS error_code,
    COUNT(*) AS message_count
FROM catalog.outbox_messages
WHERE processed_at_utc IS NULL
GROUP BY last_error_code
ORDER BY message_count DESC;
```

Do not expose `last_error_code` as a metric dimension. Querying it during an
incident does not create a permanent high-cardinality telemetry series.

## Retry and Lease Recovery

Transient failures:

- Increment `attempt_count`.
- Clear the lease.
- Schedule `next_attempt_at_utc`.
- Remain unprocessed.

Permanent failures and exhausted retry budgets set
`dead_lettered_at_utc`.

A crashed Worker does not require manual lease clearing. The row becomes
eligible after `locked_until_utc` expires.

Do not manually rewrite lease or terminal fields unless an incident-specific
recovery plan has been reviewed.

## Cache Invalidation Incidents

The invalidation path removes shared L2 before publishing the Redis Pub/Sub
signal.

Check:

1. Worker logs for a non-success batch.
2. `catalog.outbox.message.outcomes` for `retry_scheduled` or
   `dead_lettered`.
3. Redis connectivity.
4. Pending rows grouped by `last_error_code`.
5. Catalog API subscriber connectivity.

Redis Pub/Sub is ephemeral. A connected process normally invalidates its L1
immediately. A process that misses the signal can retain its L1 value until
the bounded local expiration or a later invalidation.

## RabbitMQ Publication Incidents

The Product Published adapter uses mandatory publication, publisher confirms
and persistent messages.

Investigate:

1. RabbitMQ health and credentials.
2. Exchange and binding topology.
3. Worker batch logs.
4. Retry and dead-letter rows.
5. Consumer idempotency behavior.

An unroutable mandatory publication is permanent. Transport and confirmation
failures are retryable.

The Outbox identifier is used as RabbitMQ `MessageId`. Consumers can use it as
an idempotency key, but an Inbox is still required for a durable
non-idempotent boundary.

## Telemetry

Tracing:

```text
ActivitySource: Commerce.Catalog.Outbox
Activity:       catalog.outbox.process
```

Metrics:

```text
Meter:     Commerce.Catalog.Outbox
Counter:   catalog.outbox.message.outcomes
Histogram: catalog.outbox.processing.duration
Tag:       catalog.outbox.outcome
```

Allowed tag values:

```text
processed
retry_scheduled
dead_lettered
lease_lost
```

No message identifier, Product identifier, slug, worker identifier, lease
owner or error code is emitted as a metric dimension.

## Manual Replay

Automated dead-letter replay is not implemented.

Before any manual replay:

1. Identify and correct the original failure.
2. Confirm whether the external effect may already have happened.
3. Validate consumer idempotency.
4. Preserve the original row for auditability.
5. Use a reviewed incident-specific procedure.

Do not simply clear `dead_lettered_at_utc` or reset `attempt_count` in
production.

## Related

- [ADR-0036](../../adr/0036-use-transactional-outbox-for-catalog-events.md)
- [ADR-0037](../../adr/0037-use-leased-at-least-once-catalog-outbox-processing.md)
- [Catalog Storefront Cache](../../caching/catalog-storefront.md)
- [Catalog Persistence](../../persistence/catalog.md)
- [Observability](../observability.md)
