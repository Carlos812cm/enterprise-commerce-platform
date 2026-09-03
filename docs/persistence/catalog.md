# Catalog Persistence

Catalog uses EF Core with the Npgsql PostgreSQL provider.

## Current Schema

```text
catalog.products
catalog.product_options
catalog.product_variants
catalog.product_variant_options
catalog.outbox_messages
```

The first migration stores:

- `id`
- `name`
- `slug`
- `description`
- `status`
- `published_at_utc`
- `discontinued_at_utc`

# Unique Constraints

`ux_products_slug` enforces global slug uniqueness.

The Application uniqueness checker improves the error returned before persistence.

The database constraint remains authoritative under concurrency.

# Materialization

EF Core reconstructs Product through its private constructor.

Value converters reconstruct:

- `ProductId`
- `ProductName`
- `ProductSlug`
- `ProductDescription`

Rehydration does not raise creation domain events.

# Connection Pool

Runtime persistence reuses the `NpgsqlDataSource` registered by Service Defaults.

Catalog does not create an independent runtime connection pool.

# Deferred Mapping

The initial model intentionally ignores:

- Product option definitions
- Product variants
- Variant option selections

These will be mapped when their write commands are introduced.

## Transactional Outbox

Product publication persists two independent durable intents:

- `catalog.storefront-product-cache-invalidate.v1`
- `catalog.product-published.v1`

Aggregate state and both intents are committed by one EF Core
`SaveChangesAsync` transaction.

Outbox processing occurs after that transaction through `Commerce.Worker`.
Claims use bounded PostgreSQL leases. External Redis and RabbitMQ calls never
run inside the aggregate transaction.

The Outbox state includes attempt, next-attempt, lease, processed,
dead-letter, bounded error-code and W3C trace-context fields.

See:

- [ADR-0036](../adr/0036-use-transactional-outbox-for-catalog-events.md)
- [ADR-0037](../adr/0037-use-leased-at-least-once-catalog-outbox-processing.md)
- [Catalog Outbox Runbook](../operations/runbooks/catalog-outbox.md)
# Migrations

Migrations are generated with the repository-local `dotnet-ef` tool.

The migration history table is stored at:

```
catalog.__ef_migrations_history
```

# Integration Tests

Integration tests use PostgreSQL 18.4 through Testcontainers.

They verify:

- Migration application
- Command persistence
- Aggregate rehydration
- Slug uniqueness queries
- Database unique constraints
- Transactional Product and Outbox persistence
- Rollback and retry without duplicate Outbox intents
- Leased claims, fencing, retry scheduling and dead-letter transitions
- RabbitMQ and Redis adapters against real infrastructure

Product domain aggregate
↕ ProductPersistenceMapper
Internal EF persistence records
↕ CatalogDbContext
PostgreSQL

## Optimistic Concurrency

The `products.version` column is the aggregate concurrency token.

Every aggregate update increments this value, including changes to option or variant rows.

A stale writer causes EF Core to throw `DbUpdateConcurrencyException`.

## Loading

ProductRepository loads the complete write aggregate using a split query.

Read-only storefront queries will use separate read models rather than loading the write aggregate.
