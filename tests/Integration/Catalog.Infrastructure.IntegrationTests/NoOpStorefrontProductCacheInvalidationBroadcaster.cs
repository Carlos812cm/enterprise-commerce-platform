using Catalog.Domain.Products;
using Catalog.Infrastructure.Caching;

namespace Catalog.Infrastructure.IntegrationTests;

internal sealed class
    NoOpStorefrontProductCacheInvalidationBroadcaster :
    IStorefrontProductCacheInvalidationBroadcaster
{
    public ValueTask BroadcastBySlugAsync(
        ProductSlug slug,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            slug);

        cancellationToken
            .ThrowIfCancellationRequested();

        return ValueTask.CompletedTask;
    }

    public ValueTask BroadcastAllAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken
            .ThrowIfCancellationRequested();

        return ValueTask.CompletedTask;
    }
}
