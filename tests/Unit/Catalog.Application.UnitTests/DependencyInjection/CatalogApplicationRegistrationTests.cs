using Catalog.Application;
using Catalog.Application.Abstractions.Persistence;
using Catalog.Application.Products.CreateDraftProduct;
using Catalog.Domain.Products;
using Commerce.Application.Messaging;
using Commerce.Application.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Catalog.Application.UnitTests.DependencyInjection;

public sealed class CatalogApplicationRegistrationTests
{
    [Fact]
    public async Task AddCatalogApplicationRegistersCreateDraftProductPipeline()
    {
        var repository =
            new RecordingProductRepository();

        var slugChecker =
            new UniqueSlugChecker();

        var unitOfWork =
            new RecordingUnitOfWork();

        var timeProvider =
            new FakeTimeProvider(
                new DateTimeOffset(
                    2026,
                    7,
                    20,
                    12,
                    0,
                    0,
                    TimeSpan.Zero));

        var services = new ServiceCollection();

        services.AddLogging();

        services.AddSingleton<IProductRepository>(
            repository);

        services.AddSingleton<
            IProductSlugUniquenessChecker>(
            slugChecker);

        services.AddSingleton<ICatalogUnitOfWork>(
            unitOfWork);

        services.AddSingleton<TimeProvider>(
            timeProvider);

        services.AddCatalogApplication();
        services.AddCatalogApplication();

        using var serviceProvider =
            services.BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                });

        using var scope =
            serviceProvider.CreateScope();

        var dispatcher =
            scope.ServiceProvider
                .GetRequiredService<ICommandDispatcher>();

        var result = await dispatcher.DispatchAsync(
            new CreateDraftProductCommand(
                "Enterprise Monitor",
                "enterprise-monitor",
                "Premium display."),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.NotNull(
            repository.AddedProduct);

        Assert.Equal(
            result.Value.ProductId,
            repository.AddedProduct.Id);

        Assert.Equal(
            1,
            repository.AddCallCount);

        Assert.Equal(
            1,
            unitOfWork.SaveCallCount);
    }

    private sealed class RecordingProductRepository :
        IProductRepository
    {
        public Product? AddedProduct { get; private set; }

        public int AddCallCount { get; private set; }

        public void Add(Product product)
        {
            ArgumentNullException.ThrowIfNull(product);

            AddedProduct = product;
            AddCallCount++;
        }

        public Task<Product?> GetByIdAsync(
        ProductId productId,
        CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<Product?>(null);
        }

        public void Update(Product product)
        {
            ArgumentNullException.ThrowIfNull(product);
        }
    }

    private sealed class UniqueSlugChecker :
        IProductSlugUniquenessChecker
    {
        public Task<bool> IsUniqueAsync(
            ProductSlug slug,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(slug);

            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(true);
        }
    }

    private sealed class RecordingUnitOfWork :
        ICatalogUnitOfWork
    {
        public int SaveCallCount { get; private set; }

        public Task SaveChangesAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            SaveCallCount++;

            return Task.CompletedTask;
        }
    }
}
