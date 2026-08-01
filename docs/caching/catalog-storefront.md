# Catalog Storefront Cache

The Catalog storefront uses `HybridCache` for public product projections. The
cache reduces repeated PostgreSQL reads while keeping the public HTTP response
cacheable for a short, bounded period.

## Cache Layout

| Layer | Expiration | Purpose |
|---|---:|---|
| L1, in-process memory | 30 seconds | Serves hot reads within one API instance. |
| L2, Redis | 10 minutes | Shares cached projections across API instances. |
| HTTP | 30 seconds | Allows public clients and intermediaries to reuse responses. |

The HTTP response uses:

```http
Cache-Control: public, max-age=30, stale-while-revalidate=30
```

## Key and Tags

Each product projection uses this versioned key:

```text
catalog:storefront:product:v1:{slug}
```

The `v1` segment is part of the serialization compatibility boundary. A
breaking projection or serialization change must use a new key version.

Each entry is associated with two invalidation tags:

```text
catalog:storefront:products
catalog:storefront:slug:{slug}
```

- `catalog:storefront:products` invalidates every storefront product entry.
- `catalog:storefront:slug:{slug}` invalidates one product by its canonical
  slug.

## Source

The authoritative source is Dapper reading from PostgreSQL. It projects only
Published products and Active variants.

On a cache miss, `HybridCache` executes the Dapper source and stores the
resulting public projection in L1 and L2. Concurrent requests for the same key
within an application instance may join the same cache factory execution.

## Negative Caching

Negative caching is disabled. When the source does not find a published
product, the temporary null cache entry is removed immediately. A later request
therefore executes the source again instead of reusing a cached not-found
result.

## Telemetry

The `Commerce.Catalog.Cache` meter emits these instruments:

| Instrument | Type | Unit | Description |
|---|---|---|---|
| `commerce.catalog.cache.requests` | `Counter<long>` | — | Adds `1` for each storefront cache request. |
| `commerce.catalog.cache.duration` | `Histogram<double>` | `s` | Records total cache operation duration in seconds. |

Both instruments use these bounded dimensions:

- `cache.name`: always `storefront-product`.
- `cache.outcome`: one of the outcomes below.

### Outcomes

| Outcome | Meaning |
|---|---|
| `source` | The cache factory executed the Dapper source and returned a product. |
| `cache_or_joined` | The product came from L1 or L2, or the request joined another in-process factory execution. |
| `not_found` | No published product was found; the cache entry is removed because negative caching is disabled. |
| `error` | The cache read, source query, or related operation failed with an exception. |

Metrics are recorded from a `finally` block, so successful reads, not-found
results, and technical failures all contribute request and duration telemetry.
