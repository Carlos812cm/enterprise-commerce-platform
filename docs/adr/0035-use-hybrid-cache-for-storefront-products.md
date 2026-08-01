# ADR-0035: Use HybridCache for Storefront Product Projections

## Status

Accepted

## Context

Storefront product reads are public, repetitive and significantly more frequent than Catalog writes.

Loading PostgreSQL for every request would waste database capacity and increase latency.

The platform already operates Redis through the shared infrastructure integration.

## Decision

Use `HybridCache` for published storefront product projections.

The cache uses:

- Local in-memory L1.
- Redis distributed L2.
- Versioned cache keys.
- Slug and global invalidation tags.
- Ten-minute distributed expiration.
- Thirty-second local expiration.
- No negative caching.

## Source

A Dapper source remains authoritative.

Only Published products and Active variants are projected.

## Stampede Protection

HybridCache combines concurrent requests for the same key inside a running application instance.

This decision does not claim a distributed lock across several cold application instances.

Distributed cold-miss coordination will be evaluated under load before flash-sale use cases rely on it.

## Invalidation

An explicit invalidation port is introduced.

Automatic invalidation is deferred until Domain Events are dispatched transactionally through an Outbox.

Before publication or product-editing endpoints are exposed, post-commit invalidation is mandatory.

## HTTP Caching

The storefront endpoint returns:

- A weak ETag based on Product identity and aggregate version.
- `Cache-Control: public, max-age=30, stale-while-revalidate=30`.
- `304 Not Modified` for matching `If-None-Match`.

## Security

Only public Catalog projection fields are cached.

Draft and Discontinued products are indistinguishable from missing products.

Cache keys and telemetry dimensions do not include customer data.

## Consequences

Benefits:

- Reduced PostgreSQL load.
- Low-latency warm reads.
- Shared cache across API replicas.
- In-process stampede protection.
- Explicit invalidation boundary.
- Conditional HTTP responses.

Costs:

- Temporary staleness is possible until event-driven invalidation exists.
- Redis availability becomes relevant to storefront performance.
- Serialization compatibility must be managed through cache-key versions.
- Multi-node simultaneous cold misses remain a future hardening concern.