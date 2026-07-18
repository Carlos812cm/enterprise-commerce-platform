# Catalog Product Options

Catalog models configurable products through option definitions and variant selections.

## Aggregate Ownership

`Product` owns its option definitions and product variants.

Application code must modify the option schema through the `Product` aggregate root so that option and variant invariants are evaluated in one consistency boundary.

## Option Definition

An option definition contains:

- A stable `ProductOptionId`.
- A visible `OptionName`.
- A non-negative display order.

Example:

```text
ProductOptionId: 019...
Name: Color
DisplayOrder: 0
```

The identifier remains stable independently from its visible name.

# Defining Options

A product may define options only when:

- The product is in Draft.
- No variants have been added.

Option names must be unique using case-insensitive, Unicode-normalized comparison.

Display orders must also be unique.

Once the first variant is added, the option schema is locked.

After publication, the option schema remains frozen.

# Option Selection

An option selection associates a stable option identity with an option value:

```
ColorOptionId = Black
SizeOptionId = 42
```

Selections reference `ProductOptionId`, not the visible option name.

# Selection Equality

Visible values preserve their original casing.

For identity and duplicate detection, comparison is:

- Case-insensitive.
- Unicode-normalized using Form C.
- Ordinal.
- Culture-independent.

Therefore, for the same option:

```
Black
BLACK
black
```

represent the same selection.

# Variant Option Combination

`VariantOptionCombination` contains the selections that identify a product variant.

A combination:

- Rejects duplicate option identifiers.
- Sorts selections deterministically.
- Is independent from input order.
- Produces a fixed-size SHA-256 canonical key.
- Copies the input collection.
- Supports an empty value for simple products.

# Product Validation

For a configurable product, every variant must contain exactly one selection for every option definition.

Catalog rejects:

- Missing selections.
- Unknown option identifiers.
- Duplicate option identifiers.
- Duplicate non-discontinued combinations.

# Simple Products

A simple product has no option definitions and uses one non-discontinued variant with an empty option combination.

No separate simple-product hierarchy is required.