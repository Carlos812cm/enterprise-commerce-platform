using Microsoft.Extensions.Hosting;

namespace Catalog.Infrastructure.Caching;

internal sealed class
    StorefrontProductCacheInvalidationHostedService(
        RedisStorefrontProductCacheInvalidationSubscriber
            subscriber)
    : IHostedService
{
    public Task StartAsync(
        CancellationToken cancellationToken)
    {
        return subscriber
            .StartAsync(
                cancellationToken)
            .AsTask();
    }

    public Task StopAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken
            .ThrowIfCancellationRequested();

        return subscriber
            .DisposeAsync()
            .AsTask();
    }
}
