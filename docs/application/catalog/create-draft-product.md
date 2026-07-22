# Create Draft Product

`CreateDraftProduct` is the first Catalog Application command.

## Input

- Product name
- Canonical product slug
- Optional description

## Flow

```text
ICommandDispatcher
-> Telemetry behavior
-> Logging behavior
-> CreateDraftProductCommandHandler
-> Catalog value objects
-> Slug uniqueness pre-check
-> Product.CreateDraft
-> Product repository
-> Unit of Work
```

# Validation Ownership

Catalog value objects own:

- Name syntax and length
- Slug syntax and length
- Description length and line-ending normalization

Application owns:

- Global slug availability
- Use-case orchestration
- Persistence coordination
- Cancellation propagation

PostgreSQL will later own the final unique constraint.

# Time

The handler receives `TimeProvider`.

Production uses `TimeProvider.System`.

Unit tests use `FakeTimeProvider`.

The domain never reads the system clock directly.

# Persistence

`IProductRepository.Add` is synchronous because it only attaches the aggregate to the persistence context.

`IUnitOfWork.SaveChangesAsync` performs the actual asynchronous persistence operation.

# Error Semantics

Expected input and uniqueness failures return `Result<T>`.

Unexpected technical failures propagate as exceptions and are handled by the host-level exception boundary.

# Concurrency

The slug uniqueness pre-check improves the client error but does not close concurrent races.

A future PostgreSQL unique constraint remains the authoritative guard.