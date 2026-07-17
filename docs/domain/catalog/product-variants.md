# Catalog Product Variants

A product variant represents a concrete commercial unit identified by a SKU.

Examples:

```text
RUN-X-BLK-42
RUN-X-BLK-43
RUN-X-WHT-42
```

# Aggregate Boundary

ProductVariant is an entity inside the future Product aggregate.

It is not an aggregate root.

Application code must mutate variants through Product, not by calling variant lifecycle methods directly.

The mutation methods are internal to the Catalog domain assembly.

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

Discontinued is terminal.

# Draft Mutability

While a variant is in Draft, Catalog may change:

- SKU
- Option combination

# Active Immutability

After activation, Catalog cannot change:

- SKU
- Option combination
- Identity

A correction requires discontinuing the original variant and creating a replacement.

# Idempotency

Activating an already active variant succeeds without replacing the original activation timestamp.

Discontinuing an already discontinued variant succeeds without replacing the original discontinuation timestamp.

# Time

Lifecycle timestamps must:

Be supplied explicitly.
Use UTC offset zero.
Never be read from a global system clock inside the entity.

Application handlers will provide timestamps through TimeProvider.

# Domain Events

The entity does not publish domain events directly.

The future Product aggregate will publish events after successful variant transitions because the aggregate root owns the transaction boundary and domain-event collection.

# Deferred Rules

The future Product aggregate will enforce:

- Global SKU uniqueness through Application and PostgreSQL.
- SKU uniqueness inside a product.
- Unique option combinations.
- Agreement between option definitions and selections.
- At least one active variant before publication.
- Prevention of discontinuing the last active variant.