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

An explicit invalidation port is used by the Catalog Outbox processor.

Product publication creates a durable cache-invalidation intent in the same
PostgreSQL transaction as the aggregate change.

The Worker first invalidates the `HybridCache` entry and shared Redis L2, then
publishes a Redis Pub/Sub signal. Each Catalog API instance consumes that
signal and invalidates its own local L1.

The PostgreSQL Outbox remains the durable source. Redis Pub/Sub is an
ephemeral cross-process propagation mechanism.

The detailed runtime decision is documented in
[ADR-0037](0037-use-leased-at-least-once-catalog-outbox-processing.md).

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

- A process disconnected from Redis Pub/Sub at publication time can retain its
  existing L1 value until the bounded local expiration or a later
  invalidation.
- Redis availability is relevant to shared caching and invalidation
  propagation.
- Serialization compatibility must be managed through cache-key versions.
- Multi-node simultaneous cold misses remain a future hardening concern.
