using Catalog.Application.Abstractions.Caching;
using Catalog.Application.Abstractions.Queries;
using Catalog.Application.Products.GetPublishedProductBySlug;
using Catalog.Application.Products.GetProductById;
using Catalog.Domain.Products;
using Catalog.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catalog.Infrastructure.IntegrationTests;

public sealed class StorefrontHybridCacheTests :
    IClassFixture<StorefrontCacheFixture>
{
    private readonly StorefrontCacheFixture _fixture;

    public StorefrontHybridCacheTests(
        StorefrontCacheFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task InvalidateBySlugReloadsProductFromSource()
    {
        var slug = ProductSlug.Create(
            string.Concat(
                "cached-product-",
                Guid.CreateVersion7()
                    .ToString("N"))).Value;

        var source =
            new MutableStorefrontProductSource(
                CreateProduct(
                    "Product Name A",
                    slug));

        await using var serviceProvider =
            CreateServiceProvider(source);

        await using var scope =
            serviceProvider.CreateAsyncScope();

        var reader =
            scope.ServiceProvider
                .GetRequiredService<
                    IStorefrontProductReader>();

        var invalidator =
            scope.ServiceProvider
                .GetRequiredService<
                    IStorefrontProductCacheInvalidator>();

        var initialProduct =
            await reader.GetBySlugAsync(
                slug,
                TestContext.Current
                    .CancellationToken);

        Assert.NotNull(initialProduct);
        Assert.Equal(
            "Product Name A",
            initialProduct.Name);
        Assert.Equal(1, source.ExecutionCount);

        source.ProductToReturn = CreateProduct(
            "Product Name B",
            slug);

        var cachedProduct =
            await reader.GetBySlugAsync(
                slug,
                TestContext.Current
                    .CancellationToken);

        Assert.NotNull(cachedProduct);
        Assert.Equal(
            "Product Name A",
            cachedProduct.Name);
        Assert.Equal(1, source.ExecutionCount);

        await invalidator.InvalidateBySlugAsync(
            slug,
            TestContext.Current
                .CancellationToken);

        var refreshedProduct =
            await reader.GetBySlugAsync(
                slug,
                TestContext.Current
                    .CancellationToken);

        Assert.NotNull(refreshedProduct);
        Assert.Equal(
            "Product Name B",
            refreshedProduct.Name);
        Assert.Equal(2, source.ExecutionCount);
    }

    private ServiceProvider CreateServiceProvider(
        MutableStorefrontProductSource source)
    {
        var services = new ServiceCollection();

        services.AddLogging();

        services.AddStackExchangeRedisCache(
            options =>
            {
                options.Configuration =
                    _fixture.ConnectionString;

                options.InstanceName = string.Concat(
                    "catalog-tests:",
                    Guid.CreateVersion7()
                        .ToString("N"),
                    ":");
            });

        services.AddHybridCache(options =>
        {
            options.MaximumKeyLength = 512;
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

    private static PublishedProductDetailsReadModel
        CreateProduct(
            string name,
            ProductSlug slug)
    {
        return new PublishedProductDetailsReadModel(
            Guid.CreateVersion7(),
            name,
            slug.Value,
            "Cached storefront product.",
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
            _productToReturn = productToReturn;
        }

        public PublishedProductDetailsReadModel
            ProductToReturn
        {
            get => Volatile.Read(
                ref _productToReturn);

            set
            {
                ArgumentNullException.ThrowIfNull(value);

                Volatile.Write(
                    ref _productToReturn,
                    value);
            }
        }

        public int ExecutionCount =>
            Volatile.Read(ref _executionCount);

        public Task<
            PublishedProductDetailsReadModel?>
            GetBySlugAsync(
                ProductSlug slug,
                CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(slug);

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
