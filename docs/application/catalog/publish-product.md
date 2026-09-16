# Publish Product Application Command

## Implementation boundary

`PublishProductCommand(Guid ProductId)` is registered explicitly through
`AddCatalogApplication()` and the keyed `ICommandDispatcher` pipeline.

The handler depends only on `IProductRepository`, `ICatalogUnitOfWork`,
`TimeProvider`, and the Domain/Application types. It does not reference EF Core,
Npgsql, HTTP, Redis, or RabbitMQ APIs.

The administrative HTTP adapter is documented in
[Publish Product API](../../api/catalog/publish-product.md).

## Execution

1. Reject a null command, pre-cancelled operation, or empty identifier.
2. Load the complete Product through `IProductRepository.GetByIdAsync`.
3. Observe cancellation again after loading; return NotFound if the Product is absent.
4. Read `TimeProvider.GetUtcNow()` once and call `Product.Publish`.
5. Return the Domain error without writing if publication is rejected.
6. Call `IProductRepository.Update` to apply state and enroll Domain Events.
7. Await `ICatalogUnitOfWork.SaveChangesAsync`.
8. Return Product identity, status, and the publication timestamp after save succeeds.

The Product owns publication invariants and activates all eligible Draft variants
using the same timestamp. The handler does not reproduce those rules.

Creating a product through the existing HTTP draft endpoint creates an empty
Draft. Publication requires a previously prepared Draft variant. Variant-management
HTTP endpoints are not part of this slice; tests prepare variants through the
Domain and repository instead of introducing a bypass in production.

## Transactions, conflicts, and retries

`CatalogUnitOfWork` stages Domain Events as Outbox intents and saves them with
Product and variant state through the same EF Core save operation.

One successful publication creates two independent intents:

- `catalog.storefront-product-cache-invalidate.v1`
- `catalog.product-published.v1`

A Product optimistic-concurrency conflict is translated in Infrastructure from
`DbUpdateConcurrencyException` into `CatalogOptimisticConcurrencyException`.
The handler translates only that exception into the stable conflict result
`Catalog.Product.ConcurrencyConflict`. Provider details are not included in
that result. Other unexpected failures and cancellation propagate to the caller.

There is no automatic retry inside the handler. A caller retry uses a fresh
request scope and reloads authoritative state; the failed scoped persistence
objects must not be treated as a successful publication or a reusable result.
If the Product is already Published when reloaded, the outcome is
`Catalog.Product.AlreadyPublished`, not a second success. This is distinct from
losing a concurrent save after both writers loaded Draft/version 1.

The handler does not clear Domain Events; the Unit of Work clears them only
after successful persistence. A successful HTTP response confirms database
state and durable intent, not completed RabbitMQ delivery or cache propagation.
External delivery remains the existing at-least-once Worker responsibility.

## Observability contract

The existing `Commerce.Application` ActivitySource creates the internal span
`PublishProductCommand.execute` beneath the ASP.NET Core server Activity.
For a valid sampled incoming W3C context, the intended chain is:

```text
remote traceparent
  -> ASP.NET Core server span
    -> PublishProductCommand.execute
      -> Product + variants + two Outbox intents
           trace_parent = command Activity.Id
           trace_state  = inherited tracestate
```

The persisted `trace_parent` is the command span identifier, not a copy of the
incoming parent header. Both belong to the same TraceId but have different
SpanIds. This preserves the correct parent for later asynchronous processing.
When no W3C Activity exists, the existing projector can persist null context;
these tests deliberately collect a sampled request, not every sampling mode.

The Application command metrics are:

| Instrument | Type | Dimensions |
|---|---|---|
| `commerce.application.command.executions` | `Counter<long>` | `command.name`, `command.outcome` |
| `commerce.application.command.duration` | `Histogram<double>`, seconds | `command.name`, `command.outcome` |

`command.name` is `PublishProductCommand`. Existing outcomes are `success`,
`failure`, `cancelled`, and `error`. The duration uses the existing monotonic
Stopwatch measurement. A Domain rejection is `failure`, not an unhandled
technical `error`.

The command span records `command.name`, `command.outcome`, and, for an expected
Domain rejection, the stable `error.type`. `error.type` is not a dimension on
these two command metrics. Product IDs, slugs, TraceIds, SpanIds, and message IDs
must not become dimensions on those metrics.

## Executable observability coverage

`PublishProductObservabilityTests` uses the existing HTTP fixture, real
PostgreSQL/Redis Testcontainers, and the real dispatcher, handler and Unit of Work.
It installs test-local ActivityListener and MeterListener collectors, not a new
production exporter or telemetry implementation.

The success case requires an HTTP 200, one successful command span under the
server span, exactly one execution increment and one finite non-negative duration
measurement. It reads the two Outbox rows through Npgsql and parses their stored
W3C context, checking the exact command span, inherited tracestate and pending
state.

The rejection case uses a Draft without variants. It requires HTTP 400 with
`Catalog.Product.NoPublishableVariants`, an Error-status command span, one
`failure` execution increment, one duration measurement, the matching HTTP trace
in Problem Details, no Outbox messages and unchanged Draft state.

Collectors retain only the synthetic per-test TraceId; it is an in-process test
filter, never an added metric tag. Captured tag arrays and Activity snapshots are
copied and are not used to mutate production telemetry. Both metric tag sets are
compared to the exact allowlist above. Listener disposal and async-flow context
restoration prevent test collectors from becoming permanent application state.

These tests do not establish OTLP export, Collector ingestion, a Grafana dashboard,
production sampling policy, live Keycloak/JWT verification, or Worker delivery.
They do not assert every logger field, nor cancellation/technical-failure metric
outcomes. Existing HTTP reliability and PostgreSQL tests cover separate failure
and concurrency boundaries. Do not count those results as new observability
coverage or claim exactly-once delivery.

## Related

- [ADR-0038: Publication and concurrency](../../adr/0038-use-explicit-product-publication-and-neutral-concurrency.md)
- [Command Dispatcher](command-dispatcher.md)
- [Catalog Persistence](../../persistence/catalog.md)
- [ADR-0036: Transactional Outbox](../../adr/0036-use-transactional-outbox-for-catalog-events.md)
- [ADR-0037: Leased Outbox Processing](../../adr/0037-use-leased-at-least-once-catalog-outbox-processing.md)
- [Catalog Outbox Runbook](../../operations/runbooks/catalog-outbox.md)
