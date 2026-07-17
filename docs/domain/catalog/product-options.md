# Catalog Product Options

Catalog models configurable products through option definitions and variant selections.

## Option Definition

An option definition has:

- A stable `ProductOptionId`.
- A visible `OptionName`.
- A non-negative display order.

The identifier remains stable if the visible name changes while the product is still a draft.

Examples:

```text
ProductOptionId: 019...
Name: Color
DisplayOrder: 0

Option Selection

A selection associates an option definition with a visible option value.

ColorOptionId = Black
SizeOptionId = 42

Selections reference ProductOptionId, not the visible option name.

Equality

Option values preserve their original casing for presentation.

For duplicate detection, comparison is:

Case-insensitive.
Unicode-normalized using Form C.
Ordinal and culture-independent.

Therefore:

Black
BLACK
black

represent the same option value for a given option definition.

Variant Combination

VariantOptionCombination contains the selections that identify a variant.

The combination:

Rejects duplicate option identifiers.
Sorts selections deterministically.
Is independent from input order.
Produces a fixed-size SHA-256 canonical key.
Copies the input collection.
Supports an empty combination for simple products.
Simple Products

A simple product is represented by one variant with an empty option combination.

No separate simple-product hierarchy is required.

Deferred Aggregate Rules

The future Product aggregate will enforce:

Unique option names within a product.
Unique display orders.
Maximum option count.
Exact agreement between definitions and selections.
Unique variant combinations.
Option-schema immutability after publication.