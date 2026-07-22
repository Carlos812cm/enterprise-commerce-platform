# Application Command Dispatcher

The command dispatcher executes Application commands through an ordered behavior pipeline.

## Dispatch

```csharp
await dispatcher.DispatchAsync(
    command,
    cancellationToken);
```

# Registration

Each module registers its commands explicitly:

```
services.AddCommandHandler<
    CreateDraftProductCommand,
    CreateDraftProductResponse,
    CreateDraftProductCommandHandler>();
```

# Pipeline

```
Telemetry

-> Logging
-> Future validation
-> Future authorization
-> Future transaction
-> Handler
```

# Logging

The pipeline logs:

- Completion
- Domain failure
- Cancellation
- Technical exception

Command payloads are never logged automatically.

# Metrics

The pipeline emits:

- `commerce.application.command.executions`
- `commerce.application.command.duration`

Metrics use bounded tags:

- `command.name`
- `command.outcome`
- `error.type`

# Tracing

The ActivitySource name is:

`Commerce.Application`

Command spans are internal child spans of the current request or message-processing trace.

# Registration Boundary

`AddCatalogApplication()` is not connected to executable hosts until Catalog Infrastructure provides:

- `IProductRepository`
- `IProductSlugUniquenessChecker`
- `IUnitOfWork`

This prevents an incomplete dependency graph from entering the host.