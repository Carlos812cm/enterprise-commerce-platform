# ADR-0038: Use Explicit Product Publication and a Neutral Concurrency Boundary

## Status

Accepted as the ECP-11G.3 implementation decision.

This record describes the implementation in the containing revision. It does
not certify phase completion, a successful deployment, or a merged Pull Request.
Those outcomes require their own verification evidence.

## Context

The Product aggregate already owns its publication invariants. ADR-0032 defines
its explicit persistence model and aggregate-level version token. ADR-0036 and
ADR-0037 define durable Outbox intent and subsequent at-least-once processing.

Publication needs an Application operation and an administrative HTTP adapter
without moving domain rules, ORM types or external network effects into the
wrong layer. A stale writer must produce an expected conflict instead of
exposing a persistence-provider exception over HTTP.

## Decision

Use `PublishProductCommand(Guid ProductId)` through the existing keyed command
dispatcher. Register its scoped handler explicitly in `AddCatalogApplication()`.

The handler validates input and cancellation, loads the complete Product, reads
`TimeProvider.GetUtcNow()` once, calls `Product.Publish`, applies the result with
`IProductRepository.Update`, and awaits `ICatalogUnitOfWork.SaveChangesAsync`.
It returns success only after the save completes. The Product, not the handler,
validates publication and activates its eligible Draft variants.

Keep the HTTP adapter limited to dispatch and response mapping. Its route is
`POST /api/catalog/products/{productId:guid}/publish`, without a request-body
contract, protected by `catalog.products.manage` and `catalog.products.write`.
Success is HTTP 200 with `productId`, `status`, and `publishedAtUtc`.
Endpoint-produced administrative responses use `private, no-store`.

A value that fails the `:guid` routing constraint does not select this endpoint.
A syntactically valid `Guid.Empty` reaches the command and returns validation
failure. See the API document for the complete status-code contract.

## Transaction and delivery boundary

Product state, variant activation and the two existing dispatch intents use one
Catalog Unit of Work save operation:

- `catalog.storefront-product-cache-invalidate.v1`
- `catalog.product-published.v1`

`Update` applies the explicit persistence graph and enrolls its Domain Events.
The Unit of Work stages those events and clears them only after persistence
succeeds. The handler neither clears events nor calls Redis or RabbitMQ.

HTTP 200 confirms database publication and durable dispatch intent. It does not
confirm that the Worker has delivered either external effect. The existing
at-least-once processing, lease, retry and dead-letter policies remain unchanged.
No new message version or production migration is introduced by this slice.

## Technology-neutral concurrency boundary

`ProductRecord.Version` remains the application-managed concurrency token.
Updating child state also advances the root version, preserving the aggregate
as the write boundary.

`CatalogUnitOfWork` translates only `DbUpdateConcurrencyException` into
`CatalogOptimisticConcurrencyException`, declared with the Application
persistence contracts. The original exception is preserved as the inner cause
for internal diagnosis. It is not copied into the Application error response.

This translation is part of the Catalog Unit of Work contract for all callers,
not an exception handler installed only for the publication endpoint. Existing
low-level callers and tests must expect the neutral exception at that boundary.

`PublishProductCommandHandler` converts the neutral exception into
`Catalog.Product.ConcurrencyConflict`. The existing Problem Details mapper
returns HTTP 409. Unexpected persistence errors and cancellation propagate;
they are not relabeled as conflicts.

Optimistic concurrency protects the interval between the server's aggregate
load and save. This endpoint does not accept an expected client version or
implement an `If-Match` precondition. It therefore does not promise protection
against every change between a client's earlier GET and its later POST.

## Repeat requests and retries

If publication is requested after loading an already Published product, the
result is `Catalog.Product.AlreadyPublished`. If two writers loaded the same
Draft and one wins the save, the losing writer receives `ConcurrencyConflict`.
These are different outcomes, even though both map to HTTP 409.

The handler attempts one save and performs no automatic retry. A caller retry
uses a fresh request scope and reloads database state. A rejected scope can hold
modified in-memory entities and staged events after rollback; those objects
are not evidence of a successful commit and are not reused by a new request.

The lower-level same-Unit-of-Work retry demonstrated in the Outbox integration
test covers a controlled storage failure removed before another save. It is
not a policy for resolving stale versions or resubmitting an HTTP command in
the same scope. Worker delivery retries occur after commit and are a third,
independent mechanism. ADR-0036 records this distinction explicitly.

There is no Idempotency-Key store or original-response replay. When a response
is lost, clients must reconcile against authoritative product state rather
than assume that the transaction rolled back. Rejecting a repeat publication
without appending intents does not establish exactly-once external delivery.

## Observability and security

Reuse the existing command pipeline. In the sampled W3C case covered by tests,
the command span is a child of the HTTP server span; both Outbox rows preserve
the command span context and inherited tracestate for later processing.

For the command execution counter and duration histogram, the metric dimensions
remain exactly `command.name` and `command.outcome`. Product IDs, slugs, message
IDs and trace identifiers are not added as metric dimensions.

Expected domain failures expose stable error codes. HTTP reliability tests
exercise a real database failure and require safe Problem Details without SQL,
constraint names, exception types or stack traces in the response. This is
not a claim that every log field or every possible provider failure is covered.

## Executable evidence and limits

The slice has focused tests for handler orchestration, scoped/keyed DI,
PostgreSQL publication and rollback, competing command scopes, HTTP status
mapping, competing HTTP requests, forced HTTP persistence failure, and sampled
trace/metric propagation to the two durable intents.

Concurrency coordination and induced CHECK failures exist only in tests.
They do not suppress the real save or replace its result with a mock exception.
HTTP fixtures use PostgreSQL and Redis Testcontainers and test identities;
they are not live Keycloak signature-validation tests.

The observability tests capture native listeners. They do not prove OTLP
export, Collector ingestion, Grafana rendering or every sampling mode.
The phase does not introduce variant-management HTTP endpoints, a consumer
Inbox, distributed sagas, automatic replay or Outbox retention.

## Consequences and alternatives

Benefits are explicit layer boundaries, predictable HTTP conflicts, durable
post-commit intent, and real database coverage of losing writers and rollback.
Costs include preserving the neutral exception contract, maintaining explicit
mapping and acknowledging duplicate external delivery and ambiguous client
outcomes after a lost response.

Rejected alternatives:

- Reference EF Core in Application merely to catch its concurrency exception.
- Map every persistence exception to HTTP 409 and conceal technical failures.
- Retry stale tracked objects automatically without reloading authoritative state.
- Wait for or perform broker/cache delivery inside the publication transaction.
- Introduce a new generic dispatcher or messaging framework for this one slice.

## Related

- [ADR-0032: Explicit persistence model](0032-use-explicit-catalog-persistence-model.md)
- [ADR-0036: Transactional Outbox](0036-use-transactional-outbox-for-catalog-events.md)
- [ADR-0037: Leased Outbox processing](0037-use-leased-at-least-once-catalog-outbox-processing.md)
- [Publication Application contract](../application/catalog/publish-product.md)
- [Publication HTTP contract](../api/catalog/publish-product.md)
- [Catalog Persistence](../persistence/catalog.md)
