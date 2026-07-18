# Catalog Product Variants

A product variant represents a concrete commercial unit identified by a SKU.

Examples:

```text
RUN-X-BLK-42
RUN-X-BLK-43
RUN-X-WHT-42
```

# Aggregate Boundary

`ProductVariant` is an entity inside the `Product` aggregate.

It is not an aggregate root.

Application code must mutate variants through `Product`. Variant mutation methods remain internal to the Catalog domain assembly.

`Product` owns the aggregate domain-event collection.

# State

A variant contains:

- ProductVariantId
- Sku
- VariantOptionCombination
- ProductVariantStatus
- ActivatedAtUtc
- DiscontinuedAtUtc

# Lifecycle

Allowed transitions:

```
Draft -> Active
Draft -> Discontinued
Active -> Discontinued
```

`Discontinued` is terminal.

# Draft Mutability

While a variant is Draft, `Product` may change:

- SKU
- Option combination

The aggregate still verifies SKU and combination uniqueness before delegating the mutation to the entity.

# Active Immutability

After activation, Catalog cannot change:

- SKU
- Option combination
- Identity

Correcting an active variant requires:

```
Discontinue original variant
Create replacement variant
Activate replacement variant
```

# Aggregate Invariants

`Product` enforces:

- SKU uniqueness across all its variants.
- Unique option combinations among non-discontinued variants.
- Exact agreement between option definitions and selections.
- Publication with at least one Draft variant.
- Activation only while the product is Published.
- Prevention of discontinuing the last active variant.
- Cascading discontinuation when the product is discontinued.

A SKU cannot be reused, even after its original variant is discontinued.

A discontinued option combination may be introduced again with a new SKU.

# Idempotency

Activating an already active variant succeeds without replacing the original activation timestamp.

Discontinuing an already discontinued variant succeeds without replacing the original discontinuation timestamp.

Repeated successful lifecycle calls do not raise duplicate domain events.

# Time

Lifecycle timestamps must:

- Be supplied explicitly.
- Use UTC offset zero.
- Never be read from a global system clock inside the entity or aggregate.

Application handlers will provide timestamps through `TimeProvider`.

# Domain Events

`Product` raises internal domain events for successful variant transitions:

- ProductVariantAddedDomainEvent
- ProductVariantActivatedDomainEvent
- ProductVariantDiscontinuedDomainEvent

These domain events are internal model facts, not public integration contracts.

# Deferred Global Rules

Global SKU uniqueness across different products will be protected later through:

- An Application-layer pre-check.
- A PostgreSQL unique constraint.
- Translation of database conflicts into stable application errors.