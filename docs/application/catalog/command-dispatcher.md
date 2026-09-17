# Application Command Dispatcher

The command dispatcher executes Application commands through an ordered behavior
pipeline. It resolves the command invoker by the concrete command type.

## Dispatch

```csharp
await dispatcher.DispatchAsync(
    command,
    cancellationToken);
```

## Catalog Registration

`AddCatalogApplication()` explicitly registers both Catalog commands:

```csharp
services.AddCommandHandler<
    CreateDraftProductCommand,
    CreateDraftProductResponse,
    CreateDraftProductCommandHandler>();

services.AddCommandHandler<
    PublishProductCommand,
    PublishProductResponse,
    PublishProductCommandHandler>();
```

The registration helper adds a scoped handler and a scoped keyed invoker.
Repeating `AddCatalogApplication()` does not duplicate these registrations or the
shared command behaviors. Registering the handler alone is insufficient: the
matching keyed invoker is required by `ICommandDispatcher`.

## Pipeline

The implemented command pipeline is:

```text
Telemetry -> Logging -> Handler
```

Validation, authorization and transaction behavior positions are reserved for
future pipeline extensions. They are not currently implemented as command
behaviors. HTTP authorization and the explicit Catalog Unit of Work remain
separate responsibilities.

## Logging and Metrics

The pipeline logs completion, domain failure, cancellation and technical
exceptions. Command payloads are not automatically logged.

The `Commerce.Application` Meter emits:

- `commerce.application.command.executions`
- `commerce.application.command.duration`

Command metric dimensions are bounded to `command.name` and `command.outcome`.
`error.type` is a trace attribute, not a dimension of these command metrics.

## Tracing

The ActivitySource is `Commerce.Application`.

Command spans are internal child spans of the current request or
message-processing trace. Publication uses the existing command pipeline; it
does not introduce a second dispatcher or telemetry stack.

## Composition and Lifetime

`Catalog.Api` already composes `AddCatalogApplication()` and
`AddCatalogInfrastructure()` through `AddCatalogModule()`.

The write handlers use `IProductRepository` and `ICatalogUnitOfWork`.
Create Draft Product also requires `IProductSlugUniquenessChecker`.
The Catalog query handlers require `IProductDetailsReader` and
`IStorefrontProductReader`.

The command dispatcher, invokers and handlers are scoped. Resolve them inside
an explicit service scope or the HTTP request scope. Do not resolve them from
the root provider or turn the persistence scope into a singleton.

A previously registered `TimeProvider` is preserved; otherwise the command
registration helper supplies `TimeProvider.System`.

## Publication Boundary

`PublishProductCommand` is registered and can be invoked through the Application
dispatcher. The administrative HTTP endpoint uses this same pipeline; see the
[Publish Product HTTP contract](../../api/catalog/publish-product.md) for the
request, authorization, response and error contracts.

The handler loads the aggregate, calls `Product.Publish`, explicitly calls
repository `Update`, and awaits `ICatalogUnitOfWork.SaveChangesAsync` before
returning success. It translates `CatalogOptimisticConcurrencyException` into
`Catalog.Product.ConcurrencyConflict` without retrying the stale write.

No Redis or RabbitMQ I/O is performed by the handler. Outbox staging and event
cleanup remain owned by the existing Catalog Unit of Work.

## Composition Tests

`PublishProductRegistrationTests` verifies repeated registration, successful
dispatch with the configured clock and token, reuse within a scope, isolation
between scopes, root-resolution rejection, and conflict propagation without
retry. A negative test removes only the publication invoker and verifies that
dispatch fails before repository access, even though the handler remains
resolvable.

These tests use the real Application composition and domain aggregate with
persistence test doubles. They do not prove PostgreSQL transactions, Outbox
atomicity, RabbitMQ delivery, or HTTP authentication and authorization.
