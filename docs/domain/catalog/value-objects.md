# Catalog Value Objects

Catalog uses strongly typed identifiers and immutable value objects to prevent invalid primitive values from circulating through the domain.

## Strongly Typed Identifiers

The current identifiers are:

- `ProductId`
- `ProductVariantId`
- `ProductOptionId`

Both wrap UUID version 7 values.

Implicit conversions to and from `Guid` are intentionally not provided.

New identifiers must be generated through `Generate()`.
Existing identifiers must be reconstructed through `Create(Guid)`.

## Text Normalization

### Product Name

Product names:

- Preserve casing.
- Support Unicode.
- Collapse consecutive whitespace.
- Have a maximum length of 200 characters.

### Product Slug

Product slugs:

- Must already be in canonical form.
- Use lowercase ASCII letters and numbers.
- Use single hyphens as separators.
- Cannot begin or end with a hyphen.
- Are not generated automatically by the domain.

### Product Description

Descriptions:

- Are optional.
- Use a non-null empty value object when absent.
- Normalize line endings to LF.
- Preserve paragraph structure.
- Have a maximum length of 4000 characters.

### SKU

SKUs:

- Are normalized to uppercase.
- Use ASCII letters, numbers, periods, underscores and hyphens.
- Must begin and end with a letter or number.
- Have a maximum length of 64 characters.

### Product Options

Option names and values:

- Preserve casing.
- Support Unicode.
- Collapse consecutive whitespace.
- Cannot be empty.

Case-insensitive duplicate detection will be enforced by the `Product` aggregate.

## Error Semantics

Expected invalid business input returns `Result<T>` with a typed `DomainError`.

Invalid technical identifier construction, such as `Guid.Empty`, throws an argument exception.

## Persistence

Persistence mappings must store the wrapped primitive values and reconstruct value objects through their public factories.

Persistence concerns must not leak into the domain types.