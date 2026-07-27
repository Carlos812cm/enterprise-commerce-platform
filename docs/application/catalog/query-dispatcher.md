# Application Query Dispatcher

The query dispatcher executes Application queries through an ordered,
framework-neutral behavior pipeline.

```text
IQueryDispatcher
        |
        v
Telemetry
        |
        v
Logging
        |
        v
Query Handler
        |
        v
Result<TResponse>
```

## IQueryDispatcher

Application and API code dispatch queries through `IQueryDispatcher`:

```csharp
Result<AdminProductDetailsReadModel> result =
    await queryDispatcher.DispatchAsync(
        new GetProductByIdQuery(productId),
        cancellationToken);
```

`IQueryDispatcher`:

- Accepts a `Query<TResponse>`.
- Resolves the keyed invoker registered for the concrete query type.
- Rejects a cancellation token that was already cancelled.
- Throws `InvalidOperationException` when no handler is registered.
- Returns the `Result<TResponse>` produced by the pipeline.

Each query and handler is registered explicitly:

```csharp
services.AddQueryHandler<
    GetProductByIdQuery,
    AdminProductDetailsReadModel,
    GetProductByIdQueryHandler>();
```

Registration also adds the dispatcher and the default Telemetry and Logging
behaviors. Behaviors are ordered by `IQueryBehavior.Order`.

## Telemetry

`QueryTelemetryBehavior<TQuery, TResponse>` is the outermost behavior. It
measures the complete execution of Logging and the Query Handler.

### Tracing

The behavior creates an internal Activity named:

```text
{QueryTypeName}.execute
```

The ActivitySource name is:

```text
Commerce.Application
```

Activity tags:

- `query.name`: concrete query type name.
- `query.outcome`: `success`, `failure`, `cancelled`, or `error`.
- `error.type`: domain error code or exception type when applicable.

A successful `Result<TResponse>` marks the activity as `Ok`. A failed result,
cancellation, or technical exception marks it as `Error`.

### Metrics

The `Commerce.Application` meter emits:

| Instrument | Type | Unit | Description |
|---|---|---|---|
| `commerce.application.query.executions` | `Counter<long>` | — | Adds `1` for every query pipeline execution. |
| `commerce.application.query.duration` | `Histogram<double>` | `s` | Records total query pipeline duration in seconds. |

Both instruments use bounded dimensions:

- `query.name`
- `query.outcome`

Metrics are recorded from a `finally` block, so domain failures, cancellations,
and technical exceptions are measured as well as successful queries.

Dispatch failures that happen before an invoker is entered, such as a missing
handler registration or a pre-cancelled token, do not execute the behavior
pipeline and therefore do not emit query behavior telemetry.

## Logging

`QueryLoggingBehavior<TQuery, TResponse>` logs the query lifecycle after
Telemetry has started:

- Start at `Debug`.
- Successful completion at `Information`.
- Domain failure at `Warning`, including the domain error code.
- Cancellation at `Information`.
- Technical exception at `Error`, including the exception.

Completion, failure, cancellation, and exception messages include elapsed
milliseconds.

Query payloads and response values are not logged automatically. This avoids
leaking sensitive data and prevents unbounded log cardinality.

## Query Handler

A handler implements `IQueryHandler<TQuery, TResponse>`:

```csharp
public sealed class GetProductByIdQueryHandler :
    IQueryHandler<
        GetProductByIdQuery,
        AdminProductDetailsReadModel>
{
    public Task<Result<AdminProductDetailsReadModel>> HandleAsync(
        GetProductByIdQuery query,
        CancellationToken cancellationToken)
    {
        // Validate the query, execute the read operation,
        // and return a successful or failed Result<T>.
    }
}
```

The handler owns query-specific validation and coordinates the read
abstractions required to build the response. Infrastructure-specific reads,
such as Dapper SQL, remain behind Application interfaces.

## Result<T>

Handlers return `Result<TResponse>` to distinguish expected domain outcomes
from technical failures:

- `Result.Success(value)` represents a successful query.
- `Result.Failure<TResponse>(error)` represents an expected validation,
  not-found, conflict, or other domain failure.
- Cancellation is propagated as `OperationCanceledException`.
- Unexpected technical exceptions are logged, traced, and rethrown.

The same `Result<TResponse>` passes through Logging and Telemetry unchanged
before being returned to the dispatcher caller.
