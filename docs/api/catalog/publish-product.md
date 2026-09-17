# Publish Product

`POST /api/catalog/products/{productId}/publish`

The route uses the `{productId:guid}` constraint internally.

## Authorization

Requires an authenticated caller and the existing Catalog management policy:

- Policy: `catalog.products.manage` (`ManageProductsPolicy`).
- Permission claim: `permissions`.
- Required permission: `catalog.products.write`.

Authorization is enforced before the endpoint dispatches the command.

## Request

There is no request-body contract. An empty POST is sufficient.

The product must already exist as a structurally valid Draft with at least
one Draft variant. This operation does not create options or variants.
The existing Create Draft Product endpoint alone does not prepare a
publishable product. Variant-management HTTP endpoints are not implemented
by this slice; integration tests prepare aggregates through the existing
Domain and repository APIs.

## Success

Returns `200 OK`, `application/json`, with exactly these fields:

| Field | Type | Meaning |
|---|---|---|
| `productId` | UUID string | The published Product identifier. |
| `status` | string | `published`. |
| `publishedAtUtc` | UTC timestamp | The timestamp used by the command for publication and Draft variant activation. |

The command obtains its timestamp from the server's `TimeProvider`; callers
cannot supply it. The endpoint returns it only after `SaveChangesAsync`
completes successfully.

The API response uses .NET timestamp precision. PostgreSQL timestamp columns
use microsecond precision, so round-trip comparisons account for that
representation difference. The Outbox JSON preserves the command timestamp.

A successful commit persists the Product, activated variants and exactly two
Outbox intents in the same local database transaction:

- `catalog.storefront-product-cache-invalidate.v1`
- `catalog.product-published.v1`

`200 OK` confirms that commit, not external delivery. Redis invalidation and
RabbitMQ publication are handled asynchronously by the existing Worker.
The endpoint neither runs the Worker nor waits for broker confirmation.

## Errors

| HTTP | Condition | Application/domain code |
|---:|---|---|
| `400` | `Guid.Empty`. | `Catalog.Product.InvalidId` |
| `400` | No publishable Draft variants. | `Catalog.Product.NoPublishableVariants` |
| `401` | Anonymous caller. | Host authentication response. |
| `403` | Authenticated caller lacks the required permission. | Host authorization response. |
| `404` | Well-formed, nonempty UUID does not identify a Product. | `Catalog.Product.NotFound` |
| `409` | Already Published. | `Catalog.Product.AlreadyPublished` |
| `409` | Discontinued Product. | `Catalog.Product.IsDiscontinued` |
| `409` | Concurrent write rejected by persistence. | `Catalog.Product.ConcurrencyConflict` |
| `500` | Unexpected technical failure. | Host exception handling; no exception details are returned by the endpoint. |

Domain validation errors retain their existing classification. Malformed GUID
text does not match the constrained route and receives a routing `404`, not
`Catalog.Product.InvalidId`. It never reaches this command.

Application failures use the existing Catalog Problem Details mapper,
including `code`, `traceId`, the request path as `instance`, and the stable
error `type`. Provider exceptions are not HTTP response contracts.

## Caching

Responses produced by the endpoint, both success and mapped Application
failures, include `Cache-Control: private, no-store`. Authentication,
authorization and unmatched-route responses are generated before this
handler; their headers are controlled by the host.

## Repetition and concurrency

A later publication request does not return a second success. It returns
`409` / `Catalog.Product.AlreadyPublished`, without changing the publication
timestamp or appending new Outbox intents.

There is no `Idempotency-Key` request-replay store. This behavior must not be
advertised as exactly-once message delivery. Concurrent command persistence
is covered by the PostgreSQL tests from B4.2. The HTTP reliability tests also
coordinate two request-scoped saves after both load Draft/version 1. They
require one `200` and one `409` / `Catalog.Product.ConcurrencyConflict`,
different DbContext instances, and only the winner's two Outbox intents.
A later request must return `AlreadyPublished` without additional writes.

## Testing boundary

`PublishProductEndpointTests` sends HTTP requests through the application
with `WebApplicationFactory`, the real command dispatcher and real
PostgreSQL/Redis Testcontainers. Independent Npgsql queries verify persisted
Product, variant and Outbox state. Anonymous and forbidden requests are
checked for absence of database changes.

The fixture uses its existing test authentication scheme. These tests prove
HTTP policy enforcement for the supplied identities, not Keycloak token
issuance, JWT signature validation or a live RabbitMQ consumer. Broker
publication is covered by the existing Outbox adapter tests.

The OpenAPI test checks operation registration, response metadata and the
absence of a request body. OpenAPI is exposed by the host in Development and
Testing, following the existing host configuration.

`PublishProductHttpReliabilityTests` adds HTTP concurrency and a real
PostgreSQL CHECK-constraint failure. A derived WebApplicationFactory uses
`ConfigureTestServices` and `ConfigureDbContext` to attach a test-only
SaveChanges interceptor. It observes the real context and exceptions and
coordinates the pre-save barrier; it does not replace the repository,
handler, unit of work, provider, HTTP result, or SQL result. It never
suppresses an exception or a save.

The forced database failure targets only reserved test slugs in the
disposable fixture database. The test requires the real `23514` failure and
its exact constraint name internally, but an HTTP `500` Problem Details
response without exception classes, SQL/schema details or stack traces.
It verifies unchanged Product/variant/Outbox state, removes the constraint
in `finally`, and retries successfully through a fresh HTTP request. These
checks cover response sanitization for that induced failure, not all
possible exceptions or production logging redaction.

The error response uses the host's exception handling. It retains `traceId`,
`instance`, and `no-store`; unlike mapped Application failures, its headers
need not retain the endpoint's `private` directive after error handling.
No production migration or runtime interceptor is introduced. The fixture's
configuration hook is in the test project only and leaves ordinary clients
unchanged. These tests do not claim live Keycloak authentication, RabbitMQ
delivery, client disconnect handling, or transport-level exactly-once behavior.

## Related

- [Catalog persistence](../../persistence/catalog.md)
- [Command dispatcher](../../application/catalog/command-dispatcher.md)
- [Catalog Outbox runbook](../../operations/runbooks/catalog-outbox.md)
