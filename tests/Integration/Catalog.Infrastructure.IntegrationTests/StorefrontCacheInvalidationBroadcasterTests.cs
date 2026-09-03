using Catalog.Domain.Products;
using Catalog.Infrastructure.Caching;
using StackExchange.Redis;
using Xunit;

namespace Catalog.Infrastructure.IntegrationTests;

public sealed class StorefrontCacheInvalidationBroadcasterTests :
    IClassFixture<StorefrontCacheFixture>
{
    private readonly StorefrontCacheFixture _fixture;

    public StorefrontCacheInvalidationBroadcasterTests(
        StorefrontCacheFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task
        BroadcastBySlugPublishesCanonicalSlug()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        using var connection =
            await ConnectionMultiplexer.ConnectAsync(
                _fixture.ConnectionString);

        var subscriber =
            connection.GetSubscriber();

        var channel =
            RedisChannel.Literal(
                StorefrontProductCacheInvalidationBackplane
                    .ChannelName);

        var receivedMessage =
            new TaskCompletionSource<string>(
                TaskCreationOptions
                    .RunContinuationsAsynchronously);

        await subscriber.SubscribeAsync(
            channel,
            (_, message) =>
            {
                receivedMessage.TrySetResult(
                    message.ToString());
            });

        try
        {
            var broadcaster =
                new RedisStorefrontProductCacheInvalidationBroadcaster(
                    connection);

            var slug =
                ProductSlug.Create(
                    string.Concat(
                        "redis-backplane-",
                        Guid.CreateVersion7()
                            .ToString("N")))
                    .Value;

            await broadcaster.BroadcastBySlugAsync(
                slug,
                cancellationToken);

            var received =
                await receivedMessage
                    .Task
                    .WaitAsync(
                        TimeSpan.FromSeconds(5),
                        cancellationToken);

            Assert.Equal(
                slug.Value,
                received);
        }
        finally
        {
            await subscriber.UnsubscribeAsync(
                channel);
        }
    }
}
