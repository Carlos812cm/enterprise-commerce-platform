using Catalog.Application.Abstractions.Caching;
using Catalog.Domain.Products;
using Catalog.Infrastructure.Caching;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace Catalog.Infrastructure.IntegrationTests;

public sealed class StorefrontCacheInvalidationSubscriberTests :
    IClassFixture<StorefrontCacheFixture>
{
    private readonly StorefrontCacheFixture _fixture;

    public StorefrontCacheInvalidationSubscriberTests(
        StorefrontCacheFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task
        SlugBroadcastReachesRemoteInvalidator()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        using var subscriberConnection =
            await ConnectionMultiplexer.ConnectAsync(
                _fixture.ConnectionString);

        using var broadcasterConnection =
            await ConnectionMultiplexer.ConnectAsync(
                _fixture.ConnectionString);

        var invalidator =
            new RecordingCacheInvalidator();

        await using var subscriber =
            new RedisStorefrontProductCacheInvalidationSubscriber(
                subscriberConnection,
                invalidator,
                NullLogger<
                    RedisStorefrontProductCacheInvalidationSubscriber>
                    .Instance);

        await subscriber.StartAsync(
            cancellationToken);

        var broadcaster =
            new RedisStorefrontProductCacheInvalidationBroadcaster(
                broadcasterConnection);

        var slug =
            ProductSlug.Create(
                string.Concat(
                    "subscriber-test-",
                    Guid.CreateVersion7()
                        .ToString("N")))
                .Value;

        await broadcaster.BroadcastBySlugAsync(
            slug,
            cancellationToken);

        var receivedSlug =
            await invalidator
                .SlugInvalidated
                .Task
                .WaitAsync(
                    TimeSpan.FromSeconds(5),
                    cancellationToken);

        Assert.Equal(
            slug.Value,
            receivedSlug.Value);

        Assert.False(
            invalidator
                .AllInvalidated
                .Task
                .IsCompleted);
    }

    [Fact]
    public async Task
        InvalidateAllBroadcastReachesRemoteInvalidator()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        using var subscriberConnection =
            await ConnectionMultiplexer.ConnectAsync(
                _fixture.ConnectionString);

        using var broadcasterConnection =
            await ConnectionMultiplexer.ConnectAsync(
                _fixture.ConnectionString);

        var invalidator =
            new RecordingCacheInvalidator();

        await using var subscriber =
            new RedisStorefrontProductCacheInvalidationSubscriber(
                subscriberConnection,
                invalidator,
                NullLogger<
                    RedisStorefrontProductCacheInvalidationSubscriber>
                    .Instance);

        await subscriber.StartAsync(
            cancellationToken);

        var broadcaster =
            new RedisStorefrontProductCacheInvalidationBroadcaster(
                broadcasterConnection);

        await broadcaster.BroadcastAllAsync(
            cancellationToken);

        var invalidated =
            await invalidator
                .AllInvalidated
                .Task
                .WaitAsync(
                    TimeSpan.FromSeconds(5),
                    cancellationToken);

        Assert.True(
            invalidated);

        Assert.False(
            invalidator
                .SlugInvalidated
                .Task
                .IsCompleted);
    }

    private sealed class RecordingCacheInvalidator :
        IStorefrontProductCacheInvalidator
    {
        public TaskCompletionSource<ProductSlug>
            SlugInvalidated
        {
            get;
        } =
            new(
                TaskCreationOptions
                    .RunContinuationsAsynchronously);

        public TaskCompletionSource<bool>
            AllInvalidated
        {
            get;
        } =
            new(
                TaskCreationOptions
                    .RunContinuationsAsynchronously);

        public ValueTask InvalidateBySlugAsync(
            ProductSlug slug,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(
                slug);

            cancellationToken
                .ThrowIfCancellationRequested();

            SlugInvalidated.TrySetResult(
                slug);

            return ValueTask.CompletedTask;
        }

        public ValueTask InvalidateAllAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            AllInvalidated.TrySetResult(
                true);

            return ValueTask.CompletedTask;
        }
    }
}
