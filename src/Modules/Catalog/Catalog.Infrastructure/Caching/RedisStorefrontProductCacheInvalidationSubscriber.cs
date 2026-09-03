using Catalog.Application.Abstractions.Caching;
using Catalog.Domain.Products;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Catalog.Infrastructure.Caching;

internal sealed partial class
    RedisStorefrontProductCacheInvalidationSubscriber :
    IAsyncDisposable
{
    private static readonly RedisChannel Channel =
        RedisChannel.Literal(
            StorefrontProductCacheInvalidationBackplane
                .ChannelName);

    private readonly IConnectionMultiplexer
        _connectionMultiplexer;

    private readonly IStorefrontProductCacheInvalidator
        _cacheInvalidator;

    private readonly ILogger<
        RedisStorefrontProductCacheInvalidationSubscriber>
        _logger;

    private readonly SemaphoreSlim _lifecycleGate =
        new(
            initialCount: 1,
            maxCount: 1);

    private ChannelMessageQueue? _subscription;
    private bool _disposed;

    public RedisStorefrontProductCacheInvalidationSubscriber(
        IConnectionMultiplexer connectionMultiplexer,
        IStorefrontProductCacheInvalidator cacheInvalidator,
        ILogger<
            RedisStorefrontProductCacheInvalidationSubscriber>
            logger)
    {
        ArgumentNullException.ThrowIfNull(
            connectionMultiplexer);

        ArgumentNullException.ThrowIfNull(
            cacheInvalidator);

        ArgumentNullException.ThrowIfNull(
            logger);

        _connectionMultiplexer =
            connectionMultiplexer;

        _cacheInvalidator =
            cacheInvalidator;

        _logger =
            logger;
    }

    public async ValueTask StartAsync(
        CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(
            cancellationToken);

        try
        {
            ObjectDisposedException.ThrowIf(
                _disposed,
                this);

            if (_subscription is not null)
            {
                return;
            }

            var subscriber =
                _connectionMultiplexer
                    .GetSubscriber();

            var subscription =
                await subscriber.SubscribeAsync(
                    Channel);

            subscription.OnMessage(
                HandleMessageAsync);

            _subscription =
                subscription;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycleGate.WaitAsync();

        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            var subscription =
                _subscription;

            _subscription = null;

            if (subscription is null)
            {
                return;
            }

            try
            {
                await subscription
                    .UnsubscribeAsync();

                await subscription
                    .Completion
                    .WaitAsync(
                        TimeSpan.FromSeconds(5));
            }
            catch (RedisException exception)
            {
                LogUnsubscribeFailure(
                    _logger,
                    exception);
            }
            catch (TimeoutException exception)
            {
                LogUnsubscribeFailure(
                    _logger,
                    exception);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task HandleMessageAsync(
        ChannelMessage channelMessage)
    {
        try
        {
            var message =
                channelMessage
                    .Message
                    .ToString();

            if (string.Equals(
                message,
                StorefrontProductCacheInvalidationBackplane
                    .InvalidateAllMessage,
                StringComparison.Ordinal))
            {
                await _cacheInvalidator
                    .InvalidateAllAsync(
                        CancellationToken.None);

                return;
            }

            var slugResult =
                ProductSlug.Create(
                    message);

            if (slugResult.IsFailure)
            {
                LogInvalidMessage(
                    _logger);

                return;
            }

            await _cacheInvalidator
                .InvalidateBySlugAsync(
                    slugResult.Value,
                    CancellationToken.None);
        }
        catch (Exception exception)
        {
            LogProcessingFailure(
                _logger,
                exception);
        }
    }

    [LoggerMessage(
        EventId = 2101,
        Level = LogLevel.Warning,
        Message =
            "Ignored an invalid storefront cache invalidation backplane message.")]
    private static partial void LogInvalidMessage(
        ILogger logger);

    [LoggerMessage(
        EventId = 2102,
        Level = LogLevel.Error,
        Message =
            "Failed to apply a storefront cache invalidation backplane message.")]
    private static partial void LogProcessingFailure(
        ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 2103,
        Level = LogLevel.Warning,
        Message =
            "Failed to unsubscribe the storefront cache invalidation backplane.")]
    private static partial void LogUnsubscribeFailure(
        ILogger logger,
        Exception exception);
}
