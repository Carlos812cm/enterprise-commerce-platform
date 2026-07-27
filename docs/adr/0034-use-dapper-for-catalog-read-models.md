# ADR-0034: Use Dapper for Catalog Read Models

## Status

Accepted

## Context

Catalog write operations require the Product aggregate, its invariants and optimistic concurrency.

Administrative and storefront reads do not require aggregate behavior.

Loading and rehydrating the complete write aggregate for every read would add unnecessary tracking, mapping and allocation overhead.

## Decision

Use Dapper and explicit PostgreSQL SQL for Catalog read models.

The initial read model is `AdminProductDetailsReadModel`.

It is loaded through `IProductDetailsReader` using the shared `NpgsqlDataSource`.

## Query Pipeline

Queries execute through:

- `Query<TResponse>`
- `IQueryHandler<TQuery, TResponse>`
- `IQueryDispatcher`
- Ordered query behaviors

The query pipeline emits structured logs, traces and metrics independently from the command pipeline.

## SQL Strategy

The product details reader uses one parameterized command with multiple result sets:

- Product root
- Option definitions
- Product variants
- Variant selections

The read model is assembled explicitly in Infrastructure.

## Security

SQL values are parameterized.

Administrative read endpoints remain protected by the Catalog management policy.

Query payloads and results are not automatically logged.

## Caching

The administrative product endpoint uses `Cache-Control: private, no-store`.

Storefront caching is deferred until a public Published-product projection exists.

## Consequences

Benefits:

- No aggregate rehydration for reads.
- No EF Core tracking.
- Explicit SQL and result shape.
- One database round-trip.
- Read models can evolve independently from Domain.
- Query performance can be measured independently.

Costs:

- SQL and mapping code must be maintained.
- Schema changes must be coordinated with queries.
- Compile-time validation of SQL is unavailable.
- Integration tests are mandatory.

## Related

- ADR-0028
- ADR-0030
- ADR-0032
- `docs/application/query-dispatcher.md`
- `docs/api/catalog/get-product-by-id.md`