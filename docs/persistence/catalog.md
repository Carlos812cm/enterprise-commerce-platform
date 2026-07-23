# Catalog Persistence

Catalog uses EF Core with the Npgsql PostgreSQL provider.

## Current Schema

```text
catalog.products
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