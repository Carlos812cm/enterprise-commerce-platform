using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Catalog.Infrastructure.Caching;

internal static class CatalogCacheDiagnostics
{
    public const string InstrumentationName =
        "Commerce.Catalog.Cache";

    private static readonly Meter Meter =
        new(InstrumentationName);

    private static readonly Counter<long>
        CacheRequests =
            Meter.CreateCounter<long>(
                "commerce.catalog.cache.requests",
                description:
                "Number of Catalog cache requests grouped by cache and outcome.");

    private static readonly Histogram<double>
        CacheDuration =
            Meter.CreateHistogram<double>(
                "commerce.catalog.cache.duration",
                unit: "s",
                description:
                "Catalog cache operation duration in seconds.");

    public static void Record(
        string outcome,
        long startedTimestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            outcome);

        var tags = new TagList
        {
            {
                "cache.name",
                "storefront-product"
            },
            {
                "cache.outcome",
                outcome
            }
        };

        CacheRequests.Add(
            1,
            tags);

        CacheDuration.Record(
            Stopwatch
                .GetElapsedTime(startedTimestamp)
                .TotalSeconds,
            tags);
    }
}
