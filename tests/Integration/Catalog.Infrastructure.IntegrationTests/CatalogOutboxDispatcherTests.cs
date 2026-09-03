using Catalog.Application.Abstractions.Caching;
using Catalog.Contracts.Products;
using Catalog.Domain.Products;
using Catalog.Infrastructure.Persistence.Outbox;
using Catalog.Infrastructure.Persistence.Outbox.Processing;
using Xunit;

namespace Catalog.Infrastructure.IntegrationTests;

public sealed class CatalogOutboxDispatcherTests
{
    private static readonly Guid ProductId =
        Guid.Parse(
            "019c28c0-31c2-7d95-b1c3-6c92e91a6155");

    private static readonly DateTimeOffset PublishedAtUtc =
        new(
            2026,
            8,
            28,
            12,
            0,
            0,
            TimeSpan.Zero);

    [Fact]
    public async Task
        CacheInvalidationExecutesOnlyCacheEffect()
    {
        var cacheInvalidator =
            new RecordingCacheInvalidator();

        var publisher =
            new RecordingPublisher(
                CatalogOutboxDispatchResult.Success);

        var dispatcher =
            new CatalogOutboxDispatcher(
                cacheInvalidator,
                new NoOpStorefrontProductCacheInvalidationBroadcaster(),
                publisher);

        var message =
            new DecodedStorefrontProductCacheInvalidation(
                Guid.CreateVersion7(),
                new StorefrontProductCacheInvalidationV1(
                    ProductId,
                    "enterprise-monitor",
                    PublishedAtUtc));

        var result =
            await dispatcher.DispatchAsync(
                message,
                TestContext.Current.CancellationToken);

        Assert.True(
            result.Succeeded);

        Assert.Equal(
            1,
            cacheInvalidator.InvalidateBySlugCallCount);

        Assert.Equal(
            "enterprise-monitor",
            cacheInvalidator.LastSlug?.Value);

        Assert.Equal(
            0,
            publisher.CallCount);
    }

    [Fact]
    public async Task
        ProductPublishedExecutesOnlyPublisherEffect()
    {
        var cacheInvalidator =
            new RecordingCacheInvalidator();

        var publisher =
            new RecordingPublisher(
                CatalogOutboxDispatchResult.Success);

        var dispatcher =
            new CatalogOutboxDispatcher(
                cacheInvalidator,
                new NoOpStorefrontProductCacheInvalidationBroadcaster(),
                publisher);

        var outboxMessageId =
            Guid.CreateVersion7();

        var message =
            new DecodedProductPublished(
                outboxMessageId,
                new ProductPublishedIntegrationEventV1(
                    ProductId,
                    "enterprise-monitor",
                    PublishedAtUtc));

        var result =
            await dispatcher.DispatchAsync(
                message,
                TestContext.Current.CancellationToken);

        Assert.True(
            result.Succeeded);

        Assert.Equal(
            0,
            cacheInvalidator.InvalidateBySlugCallCount);

        Assert.Equal(
            1,
            publisher.CallCount);

        Assert.Equal(
            outboxMessageId,
            publisher.LastOutboxMessageId);

        Assert.Equal(
            ProductId,
            publisher.LastEvent?.ProductId);
    }

    [Fact]
    public async Task
        PublisherFailureResultIsPropagated()
    {
        var cacheInvalidator =
            new RecordingCacheInvalidator();

        var expected =
            CatalogOutboxDispatchResult
                .TransientFailure(
                    "catalog.rabbitmq.unavailable");

        var publisher =
            new RecordingPublisher(
                expected);

        var dispatcher =
            new CatalogOutboxDispatcher(
                cacheInvalidator,
                new NoOpStorefrontProductCacheInvalidationBroadcaster(),
                publisher);

        var message =
            new DecodedProductPublished(
                Guid.CreateVersion7(),
                new ProductPublishedIntegrationEventV1(
                    ProductId,
                    "enterprise-monitor",
                    PublishedAtUtc));

        var result =
            await dispatcher.DispatchAsync(
                message,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            expected,
            result);

        Assert.Equal(
            1,
            publisher.CallCount);

        Assert.Equal(
            0,
            cacheInvalidator.InvalidateBySlugCallCount);
    }

    [Fact]
    public async Task
        UnexpectedCacheExceptionBecomesTransientFailure()
    {
        var cacheInvalidator =
            new RecordingCacheInvalidator
            {
                ExceptionToThrow =
                    new InvalidOperationException(
                        "Simulated cache failure.")
            };

        var publisher =
            new RecordingPublisher(
                CatalogOutboxDispatchResult.Success);

        var dispatcher =
            new CatalogOutboxDispatcher(
                cacheInvalidator,
                new NoOpStorefrontProductCacheInvalidationBroadcaster(),
                publisher);

        var message =
            new DecodedStorefrontProductCacheInvalidation(
                Guid.CreateVersion7(),
                new StorefrontProductCacheInvalidationV1(
                    ProductId,
                    "enterprise-monitor",
                    PublishedAtUtc));

        var result =
            await dispatcher.DispatchAsync(
                message,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            CatalogOutboxDispatchOutcome.TransientFailure,
            result.Outcome);

        Assert.Equal(
            CatalogOutboxDispatchFailureCodes
                .CacheInvalidationFailed,
            result.ErrorCode);

        Assert.Equal(
            0,
            publisher.CallCount);
    }

    [Fact]
    public async Task
        UnexpectedPublisherExceptionBecomesTransientFailure()
    {
        var cacheInvalidator =
            new RecordingCacheInvalidator();

        var publisher =
            new RecordingPublisher(
                CatalogOutboxDispatchResult.Success)
            {
                ExceptionToThrow =
                    new InvalidOperationException(
                        "Simulated publisher failure.")
            };

        var dispatcher =
            new CatalogOutboxDispatcher(
                cacheInvalidator,
                new NoOpStorefrontProductCacheInvalidationBroadcaster(),
                publisher);

        var message =
            new DecodedProductPublished(
                Guid.CreateVersion7(),
                new ProductPublishedIntegrationEventV1(
                    ProductId,
                    "enterprise-monitor",
                    PublishedAtUtc));

        var result =
            await dispatcher.DispatchAsync(
                message,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            CatalogOutboxDispatchOutcome.TransientFailure,
            result.Outcome);

        Assert.Equal(
            CatalogOutboxDispatchFailureCodes
                .PublisherFailed,
            result.ErrorCode);

        Assert.Equal(
            0,
            cacheInvalidator.InvalidateBySlugCallCount);
    }

    [Fact]
    public async Task
        RequestedCancellationIsPropagated()
    {
        using var cancellationSource =
            new CancellationTokenSource();

        cancellationSource.Cancel();

        var cacheInvalidator =
            new RecordingCacheInvalidator
            {
                ExceptionToThrow =
                    new OperationCanceledException(
                        cancellationSource.Token)
            };

        var publisher =
            new RecordingPublisher(
                CatalogOutboxDispatchResult.Success);

        var dispatcher =
            new CatalogOutboxDispatcher(
                cacheInvalidator,
                new NoOpStorefrontProductCacheInvalidationBroadcaster(),
                publisher);

        var message =
            new DecodedStorefrontProductCacheInvalidation(
                Guid.CreateVersion7(),
                new StorefrontProductCacheInvalidationV1(
                    ProductId,
                    "enterprise-monitor",
                    PublishedAtUtc));

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () =>
                await dispatcher.DispatchAsync(
                    message,
                    cancellationSource.Token));
    }

    private sealed class RecordingCacheInvalidator :
        IStorefrontProductCacheInvalidator
    {
        public int InvalidateBySlugCallCount { get; private set; }

        public ProductSlug? LastSlug { get; private set; }

        public Exception? ExceptionToThrow { get; init; }

        public ValueTask InvalidateBySlugAsync(
            ProductSlug slug,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            InvalidateBySlugCallCount++;
            LastSlug = slug;

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask InvalidateAllAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            throw new NotSupportedException(
                "InvalidateAllAsync is not used by these tests.");
        }
    }

    private sealed class RecordingPublisher :
        ICatalogProductPublishedPublisher
    {
        private readonly CatalogOutboxDispatchResult _result;

        public RecordingPublisher(
            CatalogOutboxDispatchResult result)
        {
            ArgumentNullException.ThrowIfNull(
                result);

            _result = result;
        }

        public int CallCount { get; private set; }

        public Guid? LastOutboxMessageId { get; private set; }

        public ProductPublishedIntegrationEventV1?
            LastEvent
        { get; private set; }

        public Exception? ExceptionToThrow { get; init; }

        public ValueTask<CatalogOutboxDispatchResult>
            PublishAsync(
                Guid outboxMessageId,
                ProductPublishedIntegrationEventV1 integrationEvent,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CallCount++;
            LastOutboxMessageId =
                outboxMessageId;
            LastEvent =
                integrationEvent;

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return ValueTask.FromResult(
                _result);
        }
    }
}
