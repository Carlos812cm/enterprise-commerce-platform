using Catalog.Application.Abstractions.Caching;
using Catalog.Domain.Products;
using Catalog.Infrastructure.Caching;

namespace Catalog.Infrastructure.Persistence.Outbox.Processing;

internal sealed class CatalogOutboxDispatcher
{
    private readonly IStorefrontProductCacheInvalidator
        _cacheInvalidator;

    private readonly IStorefrontProductCacheInvalidationBroadcaster
        _cacheInvalidationBroadcaster;

    private readonly ICatalogProductPublishedPublisher
        _productPublishedPublisher;

    public CatalogOutboxDispatcher(
        IStorefrontProductCacheInvalidator cacheInvalidator,
        IStorefrontProductCacheInvalidationBroadcaster
            cacheInvalidationBroadcaster,
        ICatalogProductPublishedPublisher productPublishedPublisher)
    {
        ArgumentNullException.ThrowIfNull(
            cacheInvalidator);

        ArgumentNullException.ThrowIfNull(
            cacheInvalidationBroadcaster);

        ArgumentNullException.ThrowIfNull(
            productPublishedPublisher);

        _cacheInvalidator =
            cacheInvalidator;

        _cacheInvalidationBroadcaster =
            cacheInvalidationBroadcaster;

        _productPublishedPublisher =
            productPublishedPublisher;
    }

    public async ValueTask<CatalogOutboxDispatchResult>
        DispatchAsync(
            DecodedCatalogOutboxMessage message,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            message);

        return message switch
        {
            DecodedStorefrontProductCacheInvalidation
                cacheInvalidation =>
                    await DispatchCacheInvalidationAsync(
                        cacheInvalidation,
                        cancellationToken),

            DecodedProductPublished productPublished =>
                await DispatchProductPublishedAsync(
                    productPublished,
                    cancellationToken),

            _ =>
                CatalogOutboxDispatchResult
                    .PermanentFailure(
                        CatalogOutboxDispatchFailureCodes
                            .InvalidDecodedMessage)
        };
    }

    private async ValueTask<CatalogOutboxDispatchResult>
        DispatchCacheInvalidationAsync(
            DecodedStorefrontProductCacheInvalidation message,
            CancellationToken cancellationToken)
    {
        var slugResult =
            ProductSlug.Create(
                message.Payload.Slug);

        if (slugResult.IsFailure)
        {
            return CatalogOutboxDispatchResult
                .PermanentFailure(
                    CatalogOutboxDispatchFailureCodes
                        .InvalidDecodedMessage);
        }

        try
        {
            await _cacheInvalidator
                .InvalidateBySlugAsync(
                    slugResult.Value,
                    cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return CatalogOutboxDispatchResult
                .TransientFailure(
                    CatalogOutboxDispatchFailureCodes
                        .CacheInvalidationFailed);
        }

        try
        {
            await _cacheInvalidationBroadcaster
                .BroadcastBySlugAsync(
                    slugResult.Value,
                    cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return CatalogOutboxDispatchResult
                .TransientFailure(
                    CatalogOutboxDispatchFailureCodes
                        .CacheInvalidationBroadcastFailed);
        }

        return CatalogOutboxDispatchResult.Success;
    }

    private async ValueTask<CatalogOutboxDispatchResult>
        DispatchProductPublishedAsync(
            DecodedProductPublished message,
            CancellationToken cancellationToken)
    {
        try
        {
            return await _productPublishedPublisher
                .PublishAsync(
                    message.OutboxMessageId,
                    message.Payload,
                    cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return CatalogOutboxDispatchResult
                .TransientFailure(
                    CatalogOutboxDispatchFailureCodes
                        .PublisherFailed);
        }
    }
}
