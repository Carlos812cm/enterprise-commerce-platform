using Catalog.Application.Abstractions.Caching;
using Catalog.Application.Abstractions.Queries;
using Catalog.Application.Products.GetPublishedProductBySlug;
using Catalog.Application.Products.GetProductById;
using Catalog.Domain.Products;
using Catalog.Infrastructure;
using Catalog.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace Catalog.Infrastructure.IntegrationTests;

public sealed class StorefrontCacheCrossProcessInvalidationTests :
    IClassFixture<StorefrontCacheFixture>
{
    private readonly StorefrontCacheFixture _fixture;

    public StorefrontCacheCrossProcessInvalidationTests(
        StorefrontCacheFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task
        BroadcastInvalidatesHotL1AcrossIndependentServiceProviders()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        var slug =
            ProductSlug.Create(
                string.Concat(
                    "cross-process-cache-",
                    Guid.CreateVersion7()
                        .ToString("N")))
                .Value;

        var source =
            new MutableStorefrontProductSource(
                CreateProduct(
                    "Product Name A",
                    slug));

        var redisInstanceName =
            string.Concat(
                "catalog-cross-process:",
                Guid.CreateVersion7()
                    .ToString("N"),
                ":");

        using var apiConnectionA =
            await ConnectionMultiplexer.ConnectAsync(
                _fixture.ConnectionString);

        using var apiConnectionB =
            await ConnectionMultiplexer.ConnectAsync(
                _fixture.ConnectionString);

        using var workerConnection =
            await ConnectionMultiplexer.ConnectAsync(
                _fixture.ConnectionString);

        await using var apiA =
            CreateServiceProvider(
                source,
                apiConnectionA,
                redisInstanceName);

        await using var apiB =
            CreateServiceProvider(
                source,
                apiConnectionB,
                redisInstanceName);

        await using var apiAScope =
            apiA.CreateAsyncScope();

        await using var apiBScope =
            apiB.CreateAsyncScope();

        var readerA =
            apiAScope.ServiceProvider
                .GetRequiredService<
                    IStorefrontProductReader>();

        var readerB =
            apiBScope.ServiceProvider
                .GetRequiredService<
                    IStorefrontProductReader>();

        var invalidatorA =
            apiA.GetRequiredService<
                IStorefrontProductCacheInvalidator>();

        var invalidatorB =
            apiB.GetRequiredService<
                IStorefrontProductCacheInvalidator>();

        await using var subscriberA =
            new RedisStorefrontProductCacheInvalidationSubscriber(
                apiConnectionA,
                invalidatorA,
                NullLogger<
                    RedisStorefrontProductCacheInvalidationSubscriber>
                    .Instance);

        await using var subscriberB =
            new RedisStorefrontProductCacheInvalidationSubscriber(
                apiConnectionB,
                invalidatorB,
                NullLogger<
                    RedisStorefrontProductCacheInvalidationSubscriber>
                    .Instance);

        await subscriberA.StartAsync(
            cancellationToken);

        await subscriberB.StartAsync(
            cancellationToken);

        var distributedCache =
            apiA.GetRequiredService<
                IDistributedCache>();

        var distributedCacheKey =
            StorefrontProductCacheKeys
                .CreateProductKey(slug);

        var initialA =
            await readerA.GetBySlugAsync(
                slug,
                cancellationToken);

        await WaitForDistributedCacheEntryAsync(
            distributedCache,
            distributedCacheKey,
            cancellationToken);

        var initialB =
            await readerB.GetBySlugAsync(
                slug,
                cancellationToken);

        Assert.NotNull(initialA);
        Assert.NotNull(initialB);

        Assert.Equal(
            "Product Name A",
            initialA.Name);

        Assert.Equal(
            "Product Name A",
            initialB.Name);

        Assert.Equal(
            1,
            source.ExecutionCount);

        await distributedCache.RemoveAsync(
            distributedCacheKey,
            cancellationToken);

        Assert.Null(
            await distributedCache.GetAsync(
                distributedCacheKey,
                cancellationToken));

        source.ProductToReturn =
            CreateProduct(
                "Product Name B",
                slug);

        var stillCachedA =
            await readerA.GetBySlugAsync(
                slug,
                cancellationToken);

        var stillCachedB =
            await readerB.GetBySlugAsync(
                slug,
                cancellationToken);

        Assert.NotNull(stillCachedA);
        Assert.NotNull(stillCachedB);

        Assert.Equal(
            "Product Name A",
            stillCachedA.Name);

        Assert.Equal(
            "Product Name A",
            stillCachedB.Name);

        Assert.Equal(
            1,
            source.ExecutionCount);

        var broadcaster =
            new RedisStorefrontProductCacheInvalidationBroadcaster(
                workerConnection);

        await broadcaster.BroadcastBySlugAsync(
            slug,
            cancellationToken);

        await WaitForBothReadersToRefreshAsync(
            readerA,
            readerB,
            slug,
            cancellationToken);

        var refreshedA =
            await readerA.GetBySlugAsync(
                slug,
                cancellationToken);

        var refreshedB =
            await readerB.GetBySlugAsync(
                slug,
                cancellationToken);

        Assert.NotNull(refreshedA);
        Assert.NotNull(refreshedB);

        Assert.Equal(
            "Product Name B",
            refreshedA.Name);

        Assert.Equal(
            "Product Name B",
            refreshedB.Name);

        Assert.True(
            source.ExecutionCount >= 2);
    }

    private static ServiceProvider CreateServiceProvider(
        MutableStorefrontProductSource source,
        ConnectionMultiplexer connectionMultiplexer,
        string redisInstanceName)
    {
        var services =
            new ServiceCollection();

        services.AddLogging();

        services.AddSingleton<
            IConnectionMultiplexer>(
            connectionMultiplexer);

        services.AddStackExchangeRedisCache(
            options =>
            {
                options.Configuration =
                    connectionMultiplexer
                        .Configuration;

                options.InstanceName =
                    redisInstanceName;
            });

        services.AddHybridCache(
            options =>
            {
                options.MaximumKeyLength =
                    512;

                options.MaximumPayloadBytes =
                    2 * 1024 * 1024;
            });

        services.AddSingleton<
            IStorefrontProductSource>(
            source);

        services.AddSingleton<
            IProductDetailsReader,
            EmptyProductDetailsReader>();

        services.AddCatalogInfrastructure();

        return services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
    }

    private static async Task
        WaitForDistributedCacheEntryAsync(
            IDistributedCache distributedCache,
            string cacheKey,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            distributedCache);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            cacheKey);

        var deadline =
            TimeProvider.System.GetUtcNow() +
            TimeSpan.FromSeconds(10);

        while (
            TimeProvider.System.GetUtcNow() <
            deadline)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            var cachedValue =
                await distributedCache.GetAsync(
                    cacheKey,
                    cancellationToken);

            if (cachedValue is not null)
            {
                return;
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(25),
                cancellationToken);
        }

        throw new TimeoutException(
            "The initial storefront value was not written to Redis L2.");
    }
    private static async Task
        WaitForBothReadersToRefreshAsync(
            IStorefrontProductReader readerA,
            IStorefrontProductReader readerB,
            ProductSlug slug,
            CancellationToken cancellationToken)
    {
        var deadline =
            TimeProvider.System.GetUtcNow() +
            TimeSpan.FromSeconds(5);

        while (
            TimeProvider.System.GetUtcNow() <
            deadline)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            var productA =
                await readerA.GetBySlugAsync(
                    slug,
                    cancellationToken);

            var productB =
                await readerB.GetBySlugAsync(
                    slug,
                    cancellationToken);

            if (
                productA?.Name ==
                    "Product Name B" &&
                productB?.Name ==
                    "Product Name B")
            {
                return;
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(50),
                cancellationToken);
        }

        throw new TimeoutException(
            "Both API cache instances did not observe the invalidation signal.");
    }

    private static PublishedProductDetailsReadModel
        CreateProduct(
            string name,
            ProductSlug slug)
    {
        return new PublishedProductDetailsReadModel(
            Guid.CreateVersion7(),
            name,
            slug.Value,
            "Cross-process storefront product.",
            1,
            [],
            []);
    }

    private sealed class EmptyProductDetailsReader :
        IProductDetailsReader
    {
        public Task<AdminProductDetailsReadModel?>
            GetByIdAsync(
                Guid productId,
                CancellationToken cancellationToken)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            return Task.FromResult<
                AdminProductDetailsReadModel?>(
                null);
        }
    }

    private sealed class MutableStorefrontProductSource :
        IStorefrontProductSource
    {
        private PublishedProductDetailsReadModel
            _productToReturn;

        private int _executionCount;

        public MutableStorefrontProductSource(
            PublishedProductDetailsReadModel
                productToReturn)
        {
            _productToReturn =
                productToReturn;
        }

        public PublishedProductDetailsReadModel
            ProductToReturn
        {
            get =>
                Volatile.Read(
                    ref _productToReturn);

            set
            {
                ArgumentNullException.ThrowIfNull(
                    value);

                Volatile.Write(
                    ref _productToReturn,
                    value);
            }
        }

        public int ExecutionCount =>
            Volatile.Read(
                ref _executionCount);

        public Task<
            PublishedProductDetailsReadModel?>
            GetBySlugAsync(
                ProductSlug slug,
                CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(
                slug);

            cancellationToken
                .ThrowIfCancellationRequested();

            Interlocked.Increment(
                ref _executionCount);

            return Task.FromResult<
                PublishedProductDetailsReadModel?>(
                Volatile.Read(
                    ref _productToReturn));
        }
    }
}
