# Enterprise Commerce Platform

Engineering portfolio project targeting a hybrid B2C and B2B e-commerce platform.

## North Star

Apple-grade visual clarity\
+ Amazon-grade transactional intensity\
+ Grainger-grade B2B utility\
+ VTEX/SAP/commercetools-grade enterprise logic\
+ Baymard-grade UX discipline

## Current Status

This revision includes the Catalog Product aggregate, explicit PostgreSQL
persistence, draft creation, administrative and Storefront queries, and protected
product publication through an Application command and HTTP endpoint.

The Catalog transactional Outbox persists dispatch intent with the aggregate.
The existing Worker processes those intents with leases, fencing, retries,
RabbitMQ publisher confirmations and Redis cache invalidation propagation.
External delivery remains at-least-once.

Publication is `POST /api/catalog/products/{productId}/publish`, without a request
body. A successful response confirms database publication and durable intents,
not completed Worker delivery. Products need prepared Draft variants; HTTP
variant-management operations are not implemented by this slice.

See the [publication HTTP contract](docs/api/catalog/publish-product.md) and
[Application contract](docs/application/catalog/publish-product.md) for errors,
retry semantics, tested boundaries and known limitations.

The scenarios below are project goals, not a claim that end-to-end checkout or
B2B procurement is already implemented. Branch content and local test reports
are not proof of a completed phase; verify the Pull Request and its quality gates.

## Core Scenarios

1. B2C Flash Sale Checkout
2. B2B Private Catalog + Approval Workflow

## Tech Direction

- .NET 10 LTS
- ASP.NET Core
- PostgreSQL
- Redis
- RabbitMQ
- Keycloak
- OpenTelemetry
- Testcontainers
- k6
- Docker
- GitHub Actions

## Run Locally

Follow the [container guide](docs/operations/containers.md) and the
[Catalog Outbox runbook](docs/operations/runbooks/catalog-outbox.md).
Prepare the dependencies and apply the Catalog migrations before starting the
API and active Outbox Worker against a fresh database. Migration commands and
connection-string requirements belong to the runbook rather than an incomplete
one-line startup recipe here.

Test identities used by HTTP integration tests do not replace live JWT validation
or production authentication configuration.

## Documentation

See `/docs`.

## Architecture Decision Records

See `/docs/adr`.
