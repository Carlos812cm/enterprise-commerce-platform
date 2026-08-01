using System.Diagnostics;
using Catalog.Application.Abstractions.Queries;
using Catalog.Application.Products.GetPublishedProductBySlug;
using Catalog.Domain.Products;
using Microsoft.Extensions.Caching.Hybrid;

namespace Catalog.Infrastructure.Caching;

internal sealed class HybridStorefrontProductReader(
    HybridCache cache,
    IStorefrontProductSource source)
    : IStorefrontProductReader
{
    private static readonly HybridCacheEntryOptions
        CacheOptions =
            new()
            {
                Expiration =
                    TimeSpan.FromMinutes(10),


                LocalCacheExpiration =
                    TimeSpan.FromSeconds(30)
            };

    public async Task<
        PublishedProductDetailsReadModel?>
        GetBySlugAsync(
            ProductSlug slug,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(slug);

        cancellationToken.ThrowIfCancellationRequested();

        var key =
            StorefrontProductCacheKeys
                .CreateProductKey(slug);

        var tags = new[]
        {
            StorefrontProductCacheKeys
                .AllProductsTag,

            StorefrontProductCacheKeys
                .CreateSlugTag(slug)
        };

        var sourceExecuted = 0;
        var outcome = "error";
        var startedTimestamp =
            Stopwatch.GetTimestamp();

        try
        {
            var entry =
                await cache.GetOrCreateAsync(
                        key,
                        async token =>
                        {
                            Interlocked.Exchange(
                                ref sourceExecuted,
                                1);

                            var product =
                                await source
                                    .GetBySlugAsync(
                                        slug,
                                        token)
                                    .ConfigureAwait(false);

                            return new StorefrontProductCacheEntry(
                                product);
                        },
                        options: CacheOptions,
                        tags: tags,
                        cancellationToken:
                            cancellationToken)
                    .ConfigureAwait(false);

            if (entry.Product is null)
            {
                outcome = "not_found";

                await cache
                    .RemoveAsync(
                        key,
                        cancellationToken)
                    .ConfigureAwait(false);

                return null;
            }

            outcome =
                Volatile.Read(
                    ref sourceExecuted) == 1
                    ? "source"
                    : "cache_or_joined";

            return entry.Product;
        }
        finally
        {
            CatalogCacheDiagnostics.Record(
                outcome,
                startedTimestamp);
        }
    }
}
