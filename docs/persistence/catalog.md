# Catalog Persistence

Catalog uses EF Core with the Npgsql PostgreSQL provider for writes and explicit
read models for queries. This document describes the model in the containing
revision, not the initial scalar-only persistence slice.

## Current schema and materialization

```text
catalog.products
catalog.product_options
catalog.product_variants
catalog.product_variant_options
catalog.outbox_messages
```

EF Core tracks internal persistence records: `ProductRecord`,
`ProductOptionRecord`, `ProductVariantRecord`, `ProductVariantOptionRecord`, and
`OutboxMessageRecord`. It does not materialize the Product aggregate through a
direct ORM mapping.

`ProductPersistenceMapper` translates between the persistence graph and the
Domain. Internal rehydration factories reconstruct the aggregate and its value
objects, validate persisted state, and do not replay creation Domain Events.

Product options, variants and selections are already mapped. Their persistence
is not deferred until variant-management HTTP endpoints exist.

```text
Product aggregate
    <-> ProductPersistenceMapper
Internal EF persistence records
    <-> CatalogDbContext
PostgreSQL
```

See [ADR-0032](../adr/0032-use-explicit-catalog-persistence-model.md), which
supersedes the initial direct aggregate-mapping approach of ADR-0031.

## Constraints

`ux_products_slug` enforces global slug uniqueness. The Application uniqueness
checker improves the common error path, but the database constraint remains
authoritative when requests compete.

The mapped aggregate also enforces global SKU uniqueness, unique option names
and display orders per product, live variant-combination uniqueness, and
same-product selection relationships. Lifecycle and version checks protect
invalid stored states. Application validation is not a replacement for these
database constraints.

## Connection pool and aggregate loading

Runtime persistence reuses the `NpgsqlDataSource` registered by Service Defaults;
Catalog does not create an independent runtime connection pool.

`ProductRepository.GetByIdAsync` loads the complete write aggregate using a
split query, including options, variants and their selections. Loading alone
does not enroll the aggregate for Domain Event persistence.

`Add` creates and tracks the persistence graph. `Update` requires a Product
tracked by that repository, applies its state through the mapper, increments
the root version, and enrolls its Domain Events in the write Unit of Work.
Mutating only the Domain object and calling save is not an implicit write.

Administrative and Storefront queries use their existing Dapper read models
rather than loading the write aggregate. The Storefront reader's HybridCache
and Redis behavior is described in the cache documentation.

## Optimistic concurrency

The `products.version` column is the application-managed aggregate concurrency
token. Every repository update increments it, including changes in child rows.

EF Core detects a stale writer with `DbUpdateConcurrencyException`.
`CatalogUnitOfWork` translates that exception into the Application persistence
contract `CatalogOptimisticConcurrencyException`, preserving the provider
exception as its inner cause. Callers do not need an EF Core dependency to
recognize a concurrency conflict. Other failures are not translated by this
catch block.

The publication handler returns `Catalog.Product.ConcurrencyConflict`; its HTTP
adapter maps that result to 409. A product already Published when loaded instead
returns `Catalog.Product.AlreadyPublished`.

The version protects the server load/save interval. Publication does not expose
client `If-Match` or expected-version semantics. It attempts one save, with no
automatic retry of stale state. A new caller attempt uses a fresh request scope
and reads authoritative state.

## Transactional Outbox and retry boundaries

Product publication persists two independent durable intents:

- `catalog.storefront-product-cache-invalidate.v1`
- `catalog.product-published.v1`

Aggregate state, variant activation and both intents share one EF Core
`SaveChangesAsync` transaction. The Unit of Work stages explicitly enrolled
Domain Events and clears them only after successful persistence.

When that transaction rolls back, changed in-memory entities and staged events
can still exist in its scope. They do not represent committed publication.
The lower-level Outbox test retains that scope after removing a deliberately
induced storage failure and demonstrates a successful retry without duplicate
intents. That controlled test does not authorize automatic retry of arbitrary
errors, stale versions or HTTP requests in the failed scope.

A fresh HTTP request reloads state. If the previous publication committed,
repeating it is a conflict, not response replay. If a response was lost, callers
must reconcile state; a missing response does not prove rollback.

Outbox dispatch occurs after commit through `Commerce.Worker`. Bounded leases,
fencing, retries and dead-letter state belong to that runtime. Redis and
RabbitMQ I/O does not run inside the aggregate transaction. HTTP success does
not mean those external effects have already completed, and delivery remains
at-least-once.

Outbox records contain attempt, next-attempt, lease, terminal-state, bounded
error-code and W3C trace-context fields. Neither the message contract nor the
Outbox schema is changed by ECP-11G.3.

## Migrations

Migrations use the repository-local `dotnet-ef` tool. History is stored at:

```text
catalog.__ef_migrations_history
```

The schema added by `20260811051042_AddCatalogOutbox` is reused. This publication
slice introduces no new production migration. Test fixtures apply the existing
migrations to disposable PostgreSQL databases; induced test constraints are
not production migrations.

## Test coverage and limits

The integration suites cover migration application, aggregate round trips,
uniqueness constraints, explicit write enrollment, atomic Product/Outbox
persistence, rollback, and concurrency translation against real PostgreSQL.
Publication-specific suites execute through the real command dispatcher and
exercise competing command scopes and HTTP requests. Independent Npgsql reads
check committed state and absence of losing or rolled-back intents.

Temporary failure constraints and save coordination are confined to disposable
test databases and test services. Unit-test doubles do not substitute for this
transactional evidence. Existing leased Outbox, RabbitMQ and Redis integration
suites cover different boundaries; publication HTTP success does not imply
completion of their external effects.

HTTP authentication uses test identities in these fixtures, not live Keycloak
JWT validation. Native-listener observability tests cover sampled trace context
and bounded metrics, not Collector ingestion or every exporter/sampling mode.

## Related

- [ADR-0032: Explicit persistence](../adr/0032-use-explicit-catalog-persistence-model.md)
- [ADR-0036: Transactional Outbox](../adr/0036-use-transactional-outbox-for-catalog-events.md)
- [ADR-0037: Outbox processing](../adr/0037-use-leased-at-least-once-catalog-outbox-processing.md)
- [ADR-0038: Publication and concurrency](../adr/0038-use-explicit-product-publication-and-neutral-concurrency.md)
- [Publish Product Application contract](../application/catalog/publish-product.md)
- [Publish Product HTTP contract](../api/catalog/publish-product.md)
- [Storefront cache](../caching/catalog-storefront.md)
- [Catalog Outbox Runbook](../operations/runbooks/catalog-outbox.md)
