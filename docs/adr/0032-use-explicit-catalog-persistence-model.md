# ADR-0032: Use an Explicit Catalog Persistence Model

## Status

Accepted

## Context

The Catalog domain model uses private constructors, encapsulated collections and nested immutable value objects.

The complete aggregate includes:

- Product
- Option definitions
- Product variants
- Variant option combinations
- Option selections

Directly mapping the complete graph would require reshaping the domain around ORM materialization constraints.

## Decision

Use internal EF Core persistence records:

- `ProductRecord`
- `ProductOptionRecord`
- `ProductVariantRecord`
- `ProductVariantOptionRecord`

A dedicated mapper translates between the persistence graph and the domain aggregate.

The domain exposes internal rehydration factories to `Catalog.Infrastructure`.

## Concurrency

`ProductRecord.Version` is an application-managed concurrency token.

Every aggregate update increments the root version, including updates whose business changes occur only in child tables.

This ensures that the Product aggregate remains the concurrency boundary.

## Constraints

PostgreSQL enforces:

- Global slug uniqueness
- Global SKU uniqueness
- Option name uniqueness per product
- Option display-order uniqueness per product
- Live variant-combination uniqueness per product
- Same-product selection relationships

## Consequences

Benefits:

- Domain encapsulation remains independent from EF Core.
- Rehydration does not replay domain events.
- The relational model remains normalized.
- Aggregate-level optimistic concurrency includes child changes.
- Persistence corruption is detected during mapping.

Costs:

- Explicit mapping code must be maintained.
- Persistence and domain models can drift without tests.
- Updates must use a repository-loaded tracked aggregate.
- Mapping tests become mandatory.

## Supersedes

This ADR supersedes the direct aggregate-mapping portion of ADR-0031.

The incremental persistence strategy from ADR-0031 remains accepted.