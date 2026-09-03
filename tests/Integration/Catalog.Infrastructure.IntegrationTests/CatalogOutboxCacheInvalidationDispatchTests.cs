using Catalog.Application.Abstractions.Caching;
using Catalog.Contracts.Products;
using Catalog.Domain.Products;
using Catalog.Infrastructure.Caching;
using Catalog.Infrastructure.Persistence.Outbox;
using Catalog.Infrastructure.Persistence.Outbox.Processing;
using Xunit;

namespace Catalog.Infrastructure.IntegrationTests;

public sealed class
    CatalogOutboxCacheInvalidationDispatchTests
{
    private const string InvalidateEffect =
        "invalidate";

    private const string BroadcastEffect =
        "broadcast";

    private static readonly Guid ProductId =
        Guid.Parse(
            "019c28c0-31c2-7d95-b1c3-6c92e91a6155");

    private static readonly DateTimeOffset PublishedAtUtc =
        new(
            2026,
            8,
            29,
            12,
            0,
            0,
            TimeSpan.Zero);

    [Fact]
    public async Task
        SuccessfulInvalidationRunsLocallyBeforeBroadcast()
    {
        var effects =
            new List<string>();

        var cacheInvalidator =
            new RecordingCacheInvalidator(
                effects);

        var broadcaster =
            new RecordingBroadcaster(
                effects);

        var publisher =
            new RecordingPublisher();

        var dispatcher =
            new CatalogOutboxDispatcher(
                cacheInvalidator,
                broadcaster,
                publisher);

        var result =
            await dispatcher.DispatchAsync(
                CreateMessage(),
                TestContext.Current.CancellationToken);

        Assert.True(
            result.Succeeded);

        Assert.Equal(
            CatalogOutboxDispatchOutcome.Success,
            result.Outcome);

        Assert.Null(
            result.ErrorCode);

        Assert.Equal(
            1,
            cacheInvalidator.CallCount);

        Assert.Equal(
            1,
            broadcaster.CallCount);

        Assert.Equal(
            0,
            publisher.CallCount);

        Assert.Equal(
            "enterprise-monitor",
            cacheInvalidator.LastSlug?.Value);

        Assert.Equal(
            "enterprise-monitor",
            broadcaster.LastSlug?.Value);

        Assert.Equal(
            2,
            effects.Count);

        Assert.Equal(
            InvalidateEffect,
            effects[0]);

        Assert.Equal(
            BroadcastEffect,
            effects[1]);
    }

    [Fact]
    public async Task
        LocalInvalidationFailurePreventsBroadcast()
    {
        var effects =
            new List<string>();

        var cacheInvalidator =
            new RecordingCacheInvalidator(
                effects)
            {
                ExceptionToThrow =
                    new InvalidOperationException(
                        "Simulated local invalidation failure.")
            };

        var broadcaster =
            new RecordingBroadcaster(
                effects);

        var publisher =
            new RecordingPublisher();

        var dispatcher =
            new CatalogOutboxDispatcher(
                cacheInvalidator,
                broadcaster,
                publisher);

        var result =
            await dispatcher.DispatchAsync(
                CreateMessage(),
                TestContext.Current.CancellationToken);

        Assert.False(
            result.Succeeded);

        Assert.Equal(
            CatalogOutboxDispatchOutcome.TransientFailure,
            result.Outcome);

        Assert.Equal(
            CatalogOutboxDispatchFailureCodes
                .CacheInvalidationFailed,
            result.ErrorCode);

        Assert.Equal(
            1,
            cacheInvalidator.CallCount);

        Assert.Equal(
            0,
            broadcaster.CallCount);

        Assert.Equal(
            0,
            publisher.CallCount);

        Assert.Single(
            effects);

        Assert.Equal(
            InvalidateEffect,
            effects[0]);
    }

    [Fact]
    public async Task
        BroadcastFailureBecomesTransientFailureAfterLocalInvalidation()
    {
        var effects =
            new List<string>();

        var cacheInvalidator =
            new RecordingCacheInvalidator(
                effects);

        var broadcaster =
            new RecordingBroadcaster(
                effects)
            {
                ExceptionToThrow =
                    new InvalidOperationException(
                        "Simulated Redis backplane failure.")
            };

        var publisher =
            new RecordingPublisher();

        var dispatcher =
            new CatalogOutboxDispatcher(
                cacheInvalidator,
                broadcaster,
                publisher);

        var result =
            await dispatcher.DispatchAsync(
                CreateMessage(),
                TestContext.Current.CancellationToken);

        Assert.False(
            result.Succeeded);

        Assert.Equal(
            CatalogOutboxDispatchOutcome.TransientFailure,
            result.Outcome);

        Assert.Equal(
            CatalogOutboxDispatchFailureCodes
                .CacheInvalidationBroadcastFailed,
            result.ErrorCode);

        Assert.Equal(
            1,
            cacheInvalidator.CallCount);

        Assert.Equal(
            1,
            broadcaster.CallCount);

        Assert.Equal(
            0,
            publisher.CallCount);

        Assert.Equal(
            2,
            effects.Count);

        Assert.Equal(
            InvalidateEffect,
            effects[0]);

        Assert.Equal(
            BroadcastEffect,
            effects[1]);
    }

    private static
        DecodedStorefrontProductCacheInvalidation
        CreateMessage()
    {
        return new DecodedStorefrontProductCacheInvalidation(
            Guid.CreateVersion7(),
            new StorefrontProductCacheInvalidationV1(
                ProductId,
                "enterprise-monitor",
                PublishedAtUtc));
    }

    private sealed class RecordingCacheInvalidator :
        IStorefrontProductCacheInvalidator
    {
        private readonly List<string> _effects;

        public RecordingCacheInvalidator(
            List<string> effects)
        {
            ArgumentNullException.ThrowIfNull(
                effects);

            _effects = effects;
        }

        public int CallCount { get; private set; }

        public ProductSlug? LastSlug { get; private set; }

        public Exception? ExceptionToThrow { get; init; }

        public ValueTask InvalidateBySlugAsync(
            ProductSlug slug,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(
                slug);

            cancellationToken
                .ThrowIfCancellationRequested();

            CallCount++;
            LastSlug = slug;

            _effects.Add(
                InvalidateEffect);

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask InvalidateAllAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            throw new NotSupportedException(
                "InvalidateAllAsync is not used by these tests.");
        }
    }

    private sealed class RecordingBroadcaster :
        IStorefrontProductCacheInvalidationBroadcaster
    {
        private readonly List<string> _effects;

        public RecordingBroadcaster(
            List<string> effects)
        {
            ArgumentNullException.ThrowIfNull(
                effects);

            _effects = effects;
        }

        public int CallCount { get; private set; }

        public ProductSlug? LastSlug { get; private set; }

        public Exception? ExceptionToThrow { get; init; }

        public ValueTask BroadcastBySlugAsync(
            ProductSlug slug,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(
                slug);

            cancellationToken
                .ThrowIfCancellationRequested();

            CallCount++;
            LastSlug = slug;

            _effects.Add(
                BroadcastEffect);

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask BroadcastAllAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            throw new NotSupportedException(
                "BroadcastAllAsync is not used by these tests.");
        }
    }

    private sealed class RecordingPublisher :
        ICatalogProductPublishedPublisher
    {
        public int CallCount { get; private set; }

        public ValueTask<CatalogOutboxDispatchResult>
            PublishAsync(
                Guid outboxMessageId,
                ProductPublishedIntegrationEventV1 integrationEvent,
                CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(
                integrationEvent);

            cancellationToken
                .ThrowIfCancellationRequested();

            CallCount++;

            return ValueTask.FromResult(
                CatalogOutboxDispatchResult.Success);
        }
    }
}
