using Catalog.Domain.Products;
using StackExchange.Redis;

namespace Catalog.Infrastructure.Caching;

internal sealed class RedisStorefrontProductCacheInvalidationBroadcaster :
    IStorefrontProductCacheInvalidationBroadcaster
{
    private static readonly RedisChannel Channel =
        RedisChannel.Literal(
            StorefrontProductCacheInvalidationBackplane
                .ChannelName);

    private readonly IConnectionMultiplexer _connectionMultiplexer;

    public RedisStorefrontProductCacheInvalidationBroadcaster(
        IConnectionMultiplexer connectionMultiplexer)
    {
        ArgumentNullException.ThrowIfNull(
            connectionMultiplexer);

        _connectionMultiplexer =
            connectionMultiplexer;
    }

    public ValueTask BroadcastBySlugAsync(
        ProductSlug slug,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            slug);

        return BroadcastAsync(
            slug.Value,
            cancellationToken);
    }

    public ValueTask BroadcastAllAsync(
        CancellationToken cancellationToken)
    {
        return BroadcastAsync(
            StorefrontProductCacheInvalidationBackplane
                .InvalidateAllMessage,
            cancellationToken);
    }

    private async ValueTask BroadcastAsync(
        string message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var subscriber =
            _connectionMultiplexer.GetSubscriber();

        var publishTask =
            subscriber.PublishAsync(
                Channel,
                message);

        await publishTask.WaitAsync(
            cancellationToken);
    }
}
