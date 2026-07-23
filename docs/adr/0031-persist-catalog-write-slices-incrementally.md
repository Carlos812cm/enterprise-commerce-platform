# ADR-0031: Persist Catalog Write Slices Incrementally

## Status

Accepted

## Context

The Catalog aggregate already contains product content, option definitions and variants.

The first Application use case only creates an empty Draft product.

Mapping the complete aggregate graph before any use case writes options or variants would introduce untested persistence complexity.

## Decision

Persist the current write slice incrementally.

The initial Catalog migration stores only Product root scalar state:

- Product identity
- Name
- Slug
- Description
- Status
- Lifecycle timestamps

Option definitions and variants are explicitly ignored by the initial EF Core model.

They will be added when their first Application commands are implemented.

## Unit of Work

Catalog handlers depend on `ICatalogUnitOfWork`, which extends the shared `IUnitOfWork` contract.

This prevents ambiguity when multiple module DbContexts are registered in the same process.

## Mapping Strategy

The Domain aggregate is mapped directly.

EF Core uses the private Product constructor and Value Converters.

No public setters, persistence annotations or empty constructors are added to the Domain model.

## Concurrency

The initial slice only inserts products.

Optimistic concurrency tokens are deferred until an update command persists modifications to an existing Product.

The unique slug constraint is implemented immediately because it closes a race already present in Create Draft Product.

## Consequences

Benefits:

- The first vertical slice reaches PostgreSQL.
- Persistence remains aligned with active business capabilities.
- Domain encapsulation is preserved.
- Database uniqueness closes concurrent slug races.
- Integration tests prove real materialization.

Costs:

- Options and variants are not persisted yet.
- The EF model must be expanded in a later migration.
- Repositories cannot yet load a complete configurable product graph.

## Related

- ADR-0028
- ADR-0029
- ADR-0030
- `docs/persistence/catalog.md`