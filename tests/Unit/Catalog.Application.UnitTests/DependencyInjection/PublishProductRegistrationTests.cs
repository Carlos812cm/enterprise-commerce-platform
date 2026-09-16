using Catalog.Application;
using Catalog.Application.Abstractions.Persistence;
using Catalog.Application.Abstractions.Queries;
using Catalog.Application.Products.GetProductById;
using Catalog.Application.Products.GetPublishedProductBySlug;
using Catalog.Application.Products.PublishProduct;
using Catalog.Domain.Products;
using Commerce.Application.Messaging;
using Commerce.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Catalog.Application.UnitTests.DependencyInjection;

public sealed class PublishProductRegistrationTests
{
    private static readonly DateTimeOffset PublicationTime =
        new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    private static readonly string[] ExpectedPublicationCalls =
        ["load", "update", "save"];

    [Fact]
    public void RepeatedRegistrationKeepsOneScopedHandlerAndKeyedInvoker()
    {
        var services = CreateServices();

        var handler = Assert.Single(
            services,
            descriptor =>
                !descriptor.IsKeyedService &&
                descriptor.ServiceType ==
                    typeof(ICommandHandler<PublishProductCommand, PublishProductResponse>));

        Assert.Equal(ServiceLifetime.Scoped, handler.Lifetime);
        Assert.Equal(typeof(PublishProductCommandHandler), handler.ImplementationType);

        var invoker = Assert.Single(
            services,
            descriptor =>
                descriptor.IsKeyedService &&
                object.Equals(descriptor.ServiceKey, typeof(PublishProductCommand)));

        Assert.Equal(ServiceLifetime.Scoped, invoker.Lifetime);

        var clock = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(TimeProvider));

        Assert.IsType<FakeTimeProvider>(clock.ImplementationInstance);
    }

    [Fact]
    public async Task DispatcherPublishesOnceWithRegisteredClockAndToken()
    {
        using var provider = BuildProvider(CreateServices());
        using var scope = provider.CreateScope();

        var state = scope.ServiceProvider.GetRequiredService<PublicationState>();
        var product = CreateDraft();
        state.Product = product;

        var dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
        using var cancellation = new CancellationTokenSource();

        var result = await dispatcher.DispatchAsync(
            new PublishProductCommand(product.Id.Value), cancellation.Token);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal(product.Id, result.Value.ProductId);
        Assert.Equal(ProductStatus.Published, result.Value.Status);
        Assert.Equal(PublicationTime, result.Value.PublishedAtUtc);
        Assert.Equal(PublicationTime, product.PublishedAtUtc);
        Assert.Equal(ProductVariantStatus.Active, Assert.Single(product.Variants).Status);
        Assert.Same(product, state.UpdatedProduct);
        Assert.Equal(cancellation.Token, state.ReadToken);
        Assert.Equal(cancellation.Token, state.SaveToken);
        Assert.Equal(1, state.SaveCount);
        Assert.Equal(ExpectedPublicationCalls, state.Calls);
    }

    [Fact]
    public async Task ScopedPipelineReusesInstancesAndIsolatesDifferentScopes()
    {
        using var provider = BuildProvider(CreateServices());
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var firstServices = firstScope.ServiceProvider;
        var secondServices = secondScope.ServiceProvider;
        var firstState = firstServices.GetRequiredService<PublicationState>();
        var secondState = secondServices.GetRequiredService<PublicationState>();
        var firstProduct = CreateDraft();
        var secondProduct = CreateDraft();
        firstState.Product = firstProduct;
        secondState.Product = secondProduct;

        var firstDispatcher = firstServices.GetRequiredService<ICommandDispatcher>();
        var secondDispatcher = secondServices.GetRequiredService<ICommandDispatcher>();
        var firstHandler = firstServices.GetRequiredService<
            ICommandHandler<PublishProductCommand, PublishProductResponse>>();
        var secondHandler = secondServices.GetRequiredService<
            ICommandHandler<PublishProductCommand, PublishProductResponse>>();

        Assert.Same(firstDispatcher, firstServices.GetRequiredService<ICommandDispatcher>());
        Assert.Same(
            firstHandler,
            firstServices.GetRequiredService<
                ICommandHandler<PublishProductCommand, PublishProductResponse>>());
        Assert.NotSame(firstDispatcher, secondDispatcher);
        Assert.NotSame(firstHandler, secondHandler);
        Assert.NotSame(firstState, secondState);
        Assert.NotSame(
            firstServices.GetRequiredService<IProductRepository>(),
            secondServices.GetRequiredService<IProductRepository>());
        Assert.NotSame(
            firstServices.GetRequiredService<ICatalogUnitOfWork>(),
            secondServices.GetRequiredService<ICatalogUnitOfWork>());

        var firstResult = await firstDispatcher.DispatchAsync(
            new PublishProductCommand(firstProduct.Id.Value),
            TestContext.Current.CancellationToken);

        Assert.True(firstResult.IsSuccess, firstResult.Error?.Code);
        Assert.Empty(secondState.Calls);
        Assert.Equal(ProductStatus.Draft, secondProduct.Status);

        var secondResult = await secondDispatcher.DispatchAsync(
            new PublishProductCommand(secondProduct.Id.Value),
            TestContext.Current.CancellationToken);

        Assert.True(secondResult.IsSuccess, secondResult.Error?.Code);
        Assert.Same(firstProduct, firstState.UpdatedProduct);
        Assert.Same(secondProduct, secondState.UpdatedProduct);
        Assert.Equal(1, firstState.SaveCount);
        Assert.Equal(1, secondState.SaveCount);
        Assert.Equal(ExpectedPublicationCalls, firstState.Calls);
        Assert.Equal(ExpectedPublicationCalls, secondState.Calls);
    }

    [Fact]
    public void RootProviderRejectsPublicationHandlerResolution()
    {
        using var provider = BuildProvider(CreateServices());

        Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredService<
                ICommandHandler<PublishProductCommand, PublishProductResponse>>());
    }

    [Fact]
    public async Task DispatcherPreservesConcurrencyConflictWithoutRetry()
    {
        using var provider = BuildProvider(CreateServices());
        using var scope = provider.CreateScope();

        var state = scope.ServiceProvider.GetRequiredService<PublicationState>();
        var product = CreateDraft();
        state.Product = product;
        state.SaveFailure = new CatalogOptimisticConcurrencyException();

        var dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
        var result = await dispatcher.DispatchAsync(
            new PublishProductCommand(product.Id.Value),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.NotNull(result.Error);
        Assert.Equal("Catalog.Product.ConcurrencyConflict", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(1, state.SaveCount);
        Assert.Equal(ExpectedPublicationCalls, state.Calls);
    }

    [Fact]
    public async Task MissingPublicationInvokerFailsBeforeRepositoryAccess()
    {
        var services = CreateServices();
        var invoker = Assert.Single(
            services,
            descriptor =>
                descriptor.IsKeyedService &&
                object.Equals(descriptor.ServiceKey, typeof(PublishProductCommand)));

        // Keep the handler registered; remove only its keyed dispatch entry.
        Assert.True(services.Remove(invoker));

        using var provider = BuildProvider(services);
        using var scope = provider.CreateScope();
        var state = scope.ServiceProvider.GetRequiredService<PublicationState>();
        var product = CreateDraft();
        state.Product = product;

        Assert.IsType<PublishProductCommandHandler>(
            scope.ServiceProvider.GetRequiredService<
                ICommandHandler<PublishProductCommand, PublishProductResponse>>());

        var dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.DispatchAsync(
                new PublishProductCommand(product.Id.Value),
                TestContext.Current.CancellationToken));

        Assert.Empty(state.Calls);
        Assert.Equal(ProductStatus.Draft, product.Status);
        Assert.Equal(0, state.SaveCount);
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(PublicationTime));
        services.AddScoped<PublicationState>();
        services.AddScoped<IProductRepository, RecordingProductRepository>();
        services.AddScoped<ICatalogUnitOfWork, RecordingUnitOfWork>();
        services.AddScoped<IProductSlugUniquenessChecker, UniqueSlugChecker>();
        services.AddScoped<IProductDetailsReader, EmptyProductDetailsReader>();
        services.AddScoped<IStorefrontProductReader, EmptyStorefrontProductReader>();

        // Exercise the production composition entry point, including repeat calls.
        services.AddCatalogApplication();
        services.AddCatalogApplication();
        return services;
    }

    private static ServiceProvider BuildProvider(ServiceCollection services)
    {
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    private static Product CreateDraft()
    {
        var product = Product.CreateDraft(
            ProductName.Create("Publication registration product").Value,
            ProductSlug.Create("publication-registration-product").Value,
            ProductDescription.Empty,
            PublicationTime.AddHours(-1));

        var added = product.AddVariant(
            Sku.Create("PUBLICATION-DI-001").Value,
            VariantOptionCombination.Empty,
            PublicationTime.AddMinutes(-30));

        Assert.True(added.IsSuccess, added.Error?.Code);
        _ = product.DequeueDomainEvents();
        return product;
    }

    private sealed class PublicationState
    {
        public PublicationState()
        {
        }

        public Product? Product { get; set; }
        public Product? UpdatedProduct { get; set; }
        public Exception? SaveFailure { get; set; }
        public List<string> Calls { get; } = [];
        public int SaveCount { get; set; }
        public CancellationToken ReadToken { get; set; }
        public CancellationToken SaveToken { get; set; }
    }

    private sealed class RecordingProductRepository(PublicationState state) : IProductRepository
    {
        public Task<Product?> GetByIdAsync(
            ProductId productId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            state.Calls.Add("load");
            state.ReadToken = cancellationToken;
            var product = state.Product;
            return Task.FromResult(product is not null && product.Id == productId ? product : null);
        }

        public void Add(Product product)
        {
            ArgumentNullException.ThrowIfNull(product);
            throw new InvalidOperationException("Publication must not add a new aggregate.");
        }

        public void Update(Product product)
        {
            ArgumentNullException.ThrowIfNull(product);
            state.Calls.Add("update");
            state.UpdatedProduct = product;
        }
    }

    private sealed class RecordingUnitOfWork(PublicationState state) : ICatalogUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            state.Calls.Add("save");
            state.SaveCount++;
            state.SaveToken = cancellationToken;
            return state.SaveFailure is { } failure
                ? Task.FromException(failure)
                : Task.CompletedTask;
        }
    }

    private sealed class UniqueSlugChecker : IProductSlugUniquenessChecker
    {
        public UniqueSlugChecker()
        {
        }

        public Task<bool> IsUniqueAsync(ProductSlug slug, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(slug);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        }
    }

    private sealed class EmptyProductDetailsReader : IProductDetailsReader
    {
        public EmptyProductDetailsReader()
        {
        }

        public Task<AdminProductDetailsReadModel?> GetByIdAsync(
            Guid productId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<AdminProductDetailsReadModel?>(null);
        }
    }

    private sealed class EmptyStorefrontProductReader : IStorefrontProductReader
    {
        public EmptyStorefrontProductReader()
        {
        }

        public Task<PublishedProductDetailsReadModel?> GetBySlugAsync(
            ProductSlug slug, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(slug);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<PublishedProductDetailsReadModel?>(null);
        }
    }
}
