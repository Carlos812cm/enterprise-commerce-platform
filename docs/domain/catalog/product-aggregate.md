# Catalog Product Aggregate

`Product` is the aggregate root for Catalog product content, option definitions and variants.

## State

A product contains:

- `ProductId`
- `ProductName`
- `ProductSlug`
- `ProductDescription`
- `ProductStatus`
- Option definitions
- Product variants
- Publication timestamp
- Discontinuation timestamp
- Domain events

## Lifecycle

```text
Draft -> Published
Draft -> Discontinued
Published -> Discontinued
```

`Discontinued` is terminal.

# Content

Name and description may change while the product is Draft or Published.

The slug may change only while Draft.

# Option Schema

Option definitions may be added only while:

The product is Draft.
No variants have been added.

Option names and display orders must be unique.

The option schema is frozen after publication.

# Variants

Every variant must match the product option schema exactly.

A product without options requires an empty variant combination.

SKUs cannot be reused within a product.

Option combinations must be unique among non-discontinued variants.

A discontinued combination may be introduced again using a new SKU.

# Publication

A product requires at least one Draft variant before publication.

Publication activates all Draft variants using the same UTC timestamp.

# Published Products

A published product may receive additional Draft variants.

Those variants must be activated explicitly through the aggregate.

# Discontinuation

The last active variant cannot be discontinued while the product remains Published.

Discontinuing the product cascades to every non-discontinued variant.

# Domain Events

`Product` raises internal domain events for:

Draft creation
Variant addition
Variant activation
Variant discontinuation
Product publication
Product discontinuation

These events are not public integration contracts.