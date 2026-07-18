# ADR-0028: Model Product as the Catalog Aggregate Root

## Status

Accepted

## Context

Catalog must protect invariants involving product content, option definitions and product variants.

Examples include:

- Unique option names.
- Unique option display orders.
- Unique SKUs within a product.
- Unique active option combinations.
- Exact agreement between option definitions and variant selections.
- At least one active variant for a published product.
- Prevention of discontinuing the last active variant.
- Option-schema immutability after publication.

Enforcing these rules through independently modified entities would require coordination across multiple consistency boundaries.

## Decision

Model `Product` as the aggregate root.

`OptionDefinition` and `ProductVariant` are entities inside the aggregate.

Application code mutates option definitions and variants through `Product`.

`Product` owns the domain-event collection and raises events for successful variant and product lifecycle transitions.

## Consequences

Benefits:

- Cross-entity invariants are enforced synchronously.
- Product publication is atomic.
- Variant activation is coordinated with product state.
- Domain-event ownership follows the consistency boundary.
- Application code cannot bypass aggregate rules.

Costs:

- Loading a product requires its definitions and variants.
- Very large variant collections may eventually make the aggregate expensive.
- Bulk catalog imports require careful batching.

## Future Re-evaluation

The boundary must be reconsidered if products routinely contain hundreds or thousands of variants, or if variant mutations create unacceptable contention.

A future design may separate variants into their own aggregate while preserving product-level policies through reservations, process managers or database constraints.

## Related

- `docs/domain/catalog/product-aggregate.md`
- `docs/domain/catalog/product-options.md`
- `docs/domain/catalog/product-variants.md`