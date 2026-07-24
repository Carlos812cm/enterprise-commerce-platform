using Catalog.Application.Abstractions.Persistence;
using Catalog.Application.Products.CreateDraftProduct;
using Catalog.Domain.Products;
using Catalog.Infrastructure.Persistence;
using Commerce.Application.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Xunit;

namespace Catalog.Infrastructure.IntegrationTests;

public sealed class CreateDraftProductPersistenceTests :
    IClassFixture<CatalogPostgreSqlFixture>
{
    private static readonly DateTimeOffset FixedUtcNow =
        new(
            2026,
            7,
            22,
            12,
            0,
            0,
            TimeSpan.Zero);

    private readonly CatalogPostgreSqlFixture _fixture;

    public CreateDraftProductPersistenceTests(
        CatalogPostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CommandPersistsAndRehydratesDraftProduct()
    {
        await using var serviceProvider =
            _fixture.CreateServiceProvider(
                new FakeTimeProvider(
                    FixedUtcNow));

        CreateDraftProductResponse response;

        await using (var commandScope =
            serviceProvider.CreateAsyncScope())
        {
            var dispatcher =
                commandScope.ServiceProvider
                    .GetRequiredService<
                        ICommandDispatcher>();

            var result = await dispatcher.DispatchAsync(
                new CreateDraftProductCommand(
                    "  Enterprise   Keyboard ",
                    "enterprise-keyboard-persistence",
                    "First line\r\nSecond line"),
                TestContext.Current.CancellationToken);

            Assert.True(
                result.IsSuccess,
                result.Error?.Code);

            response = result.Value;
        }

        await using var verificationScope =
            serviceProvider.CreateAsyncScope();

        var repository =
            verificationScope.ServiceProvider
                .GetRequiredService<IProductRepository>();

        var persistedProduct =
            await repository.GetByIdAsync(
                response.ProductId,
                TestContext.Current.CancellationToken);

        Assert.NotNull(persistedProduct);

        Assert.Equal(
            ProductStatus.Draft,
            persistedProduct.Status);

        Assert.Equal(
            "Enterprise Keyboard",
            persistedProduct.Name.Value);

        Assert.Equal(
            "enterprise-keyboard-persistence",
            persistedProduct.Slug.Value);

        Assert.Equal(
            "First line\nSecond line",
            persistedProduct.Description.Value);

        Assert.Null(
            persistedProduct.PublishedAtUtc);

        Assert.Null(
            persistedProduct.DiscontinuedAtUtc);

        Assert.Empty(
            persistedProduct.OptionDefinitions);

        Assert.Empty(
            persistedProduct.Variants);

        Assert.Empty(
            persistedProduct.DomainEvents);
    }

    [Fact]
    public async Task SlugCheckerReadsDatabaseState()
    {
        await using var serviceProvider =
            _fixture.CreateServiceProvider(
                TimeProvider.System);

        var slug =
            ProductSlug.Create(
                "database-state-product").Value;

        await using (var writeScope =
            serviceProvider.CreateAsyncScope())
        {
            var repository =
                writeScope.ServiceProvider
                    .GetRequiredService<IProductRepository>();

            var unitOfWork =
                writeScope.ServiceProvider
                    .GetRequiredService<ICatalogUnitOfWork>();

            var product = Product.CreateDraft(
                ProductName.Create(
                    "Database State Product").Value,
                slug,
                ProductDescription.Empty,
                FixedUtcNow);

            repository.Add(product);

            await unitOfWork.SaveChangesAsync(
                TestContext.Current.CancellationToken);
        }

        await using var checkScope =
            serviceProvider.CreateAsyncScope();

        var checker =
            checkScope.ServiceProvider
                .GetRequiredService<
                    IProductSlugUniquenessChecker>();

        var isUnique = await checker.IsUniqueAsync(
            slug,
            TestContext.Current.CancellationToken);

        Assert.False(isUnique);
    }

    [Fact]
    public async Task UniqueConstraintClosesConcurrentSlugRace()
    {
        await using var serviceProvider =
            _fixture.CreateServiceProvider(
                TimeProvider.System);

        var slug =
            ProductSlug.Create(
                "database-unique-product").Value;

        var firstProduct = Product.CreateDraft(
            ProductName.Create(
                "First Product").Value,
            slug,
            ProductDescription.Empty,
            FixedUtcNow);

        await using (var firstScope =
            serviceProvider.CreateAsyncScope())
        {
            var productRepository =
                firstScope.ServiceProvider
                    .GetRequiredService<IProductRepository>();

            var unitOfWork =
                firstScope.ServiceProvider
                    .GetRequiredService<
                        ICatalogUnitOfWork>();

            productRepository.Add(firstProduct);

            await unitOfWork.SaveChangesAsync(
                TestContext.Current.CancellationToken);
        }

        var secondProduct = Product.CreateDraft(
            ProductName.Create(
                "Second Product").Value,
            slug,
            ProductDescription.Empty,
            FixedUtcNow.AddMinutes(1));

        await using var secondScope =
            serviceProvider.CreateAsyncScope();

        var secondProductRepository =
            secondScope.ServiceProvider
                .GetRequiredService<IProductRepository>();

        var secondUnitOfWork =
            secondScope.ServiceProvider
                .GetRequiredService<ICatalogUnitOfWork>();

        secondProductRepository.Add(secondProduct);

        var exception =
            await Assert.ThrowsAsync<
                DbUpdateException>(
                () => secondUnitOfWork
                    .SaveChangesAsync(
                        TestContext.Current
                            .CancellationToken));

        var postgresException =
            Assert.IsType<PostgresException>(
                exception.InnerException);

        Assert.Equal(
            PostgresErrorCodes.UniqueViolation,
            postgresException.SqlState);

        Assert.Equal(
            "ux_products_slug",
            postgresException.ConstraintName);
    }
}
