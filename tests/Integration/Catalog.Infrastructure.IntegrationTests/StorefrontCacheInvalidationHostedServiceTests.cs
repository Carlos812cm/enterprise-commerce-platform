using Catalog.Application.Abstractions.Caching;
using Catalog.Domain.Products;
using Catalog.Infrastructure;
using Catalog.Infrastructure.Caching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using Xunit;

namespace Catalog.Infrastructure.IntegrationTests;

public sealed class
    StorefrontCacheInvalidationHostedServiceTests :
    IClassFixture<StorefrontCacheFixture>
{
    private readonly StorefrontCacheFixture _fixture;

    public StorefrontCacheInvalidationHostedServiceTests(
        StorefrontCacheFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task
        HostedLifecycleStartsAndStopsRedisSubscription()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        using var apiConnection =
            await ConnectionMultiplexer.ConnectAsync(
                _fixture.ConnectionString);

        using var workerConnection =
            await ConnectionMultiplexer.ConnectAsync(
                _fixture.ConnectionString);

        var invalidator =
            new RecordingCacheInvalidator();

        var services =
            new ServiceCollection();

        services.AddLogging();

        services.AddSingleton<
            IConnectionMultiplexer>(
            apiConnection);

        services.AddSingleton<
            IStorefrontProductCacheInvalidator>(
            invalidator);

        services
            .AddCatalogStorefrontCacheInvalidationSubscriber();

        await using var provider =
            services.BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                });

        var hostedServices =
            provider
                .GetServices<IHostedService>()
                .ToArray();

        var hostedService =
            Assert.Single(
                hostedServices);

        await hostedService.StartAsync(
            cancellationToken);

        try
        {
            var broadcaster =
                new RedisStorefrontProductCacheInvalidationBroadcaster(
                    workerConnection);

            var slug =
                ProductSlug.Create(
                    string.Concat(
                        "hosted-backplane-",
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
        }
        finally
        {
            await hostedService.StopAsync(
                CancellationToken.None);
        }
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

            return ValueTask.CompletedTask;
        }
    }
}
